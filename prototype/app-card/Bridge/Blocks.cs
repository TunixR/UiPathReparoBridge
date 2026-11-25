// PROTOTYPE for ReparoHome#66: keep the failed screen when a UiPath Use Application/Browser card would close the app.
// Builds on #48's Blocks.cs (Continuation). What changes:
//  - RecoverAt = Event (default): Reparo Event's own FaultCallback runs the Recovery, inside any card that holds the
//    Event, so the card never sees the fault while Reparo works. RecoverAt = Process keeps #48's behaviour.
//  - After `done`: a target that is this Event at the same passes runs the Event's body again in place (still inside
//    the card). Any other target "jumps": the Event completes, and each Bridge block above completes too until one
//    holds the target, which re-enters it. A UiPath scope in between (a card) just completes normally. No signal
//    exception crosses the author's activities.
//  - UiPath scopes may sit between Bridge blocks (#64): Holds/PathTo look through non-Bridge activities.
//  - Probes for the other options: the cards' Open/Close modes are read at run start, and optionally overridden
//    (CardCloseOverride); the TrackingParticipant can hold the workflow thread on the first fault record (TrackHoldMs).
using System;
using System.Activities;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Markup;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Reparo.UiPathBridge
{
    internal sealed class Target
    {
        public string EventId;
        public List<int> Passes = new();
        public override string ToString() => EventId + Suffix(Passes);
        public static string Suffix(IEnumerable<int> p) => p.Any() ? " " + string.Concat(p.Select(i => "#" + i)) : "";
    }

    /// Variables <-> JSON, the way the Bridge would do it (unchanged from #48).
    internal static class Json
    {
        public static readonly JsonSerializer S = JsonSerializer.CreateDefault();

        public static JToken Parse(string text)
        {
            using var r = new JsonTextReader(new StringReader(text)) { FloatParseHandling = FloatParseHandling.Decimal, DateParseHandling = DateParseHandling.None };
            return JToken.ReadFrom(r);
        }

        public static string Text(object value) => value == null ? "null" : JToken.FromObject(value, S).ToString(Formatting.None);

        public static object To(JToken json, Type declared, object live)
        {
            if (json.Type == JTokenType.Null) return null;
            if (declared == typeof(DataTable) && live is DataTable schema)
            {
                var t = schema.Clone();
                foreach (JObject row in json)
                {
                    var r = t.NewRow();
                    foreach (DataColumn c in t.Columns)
                        r[c] = row.TryGetValue(c.ColumnName, out var v) && v.Type != JTokenType.Null ? v.ToObject(c.DataType, S) : DBNull.Value;
                    t.Rows.Add(r);
                }
                return t;
            }
            return json.ToObject(declared, S);
        }

        public static string WhyNotCarried(object value, Type declared, out string text)
        {
            text = null;
            try
            {
                text = Text(value);
                var back = To(Parse(text), declared, value);
                if (value != null && back != null && back.GetType() != value.GetType()) return $"comes back as {back.GetType().Name}, was {value.GetType().Name}";
                if (Text(back) != text) return "JSON differs after the round trip";
                if (value is System.Security.SecureString) return "a SecureString serialises to its length only";
                return null;
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
        }
    }

    internal sealed class Snapshot
    {
        public Dictionary<string, string> Carried = new();
        public Dictionary<string, object> Live = new();
        public HashSet<string> NotCarried = new();
    }

    internal sealed class RunState
    {
        public const string Key = "Reparo.Run";
        public ReparoProcess Process;
        public string File;
        public Dictionary<object, string> IdRefs = new();
        public Dictionary<string, Type> VarTypes = new();
        public Target Target;                              // set while a Continuation is entering (or jumping up to enter)
        public Dictionary<string, object> Restore;
        public List<int> Passes = new();
        public List<string> UiLog = new();
        public ReparoEvent CurrentEvent;
        public List<int> CurrentPasses;
        public Snapshot AtEventStart;
        public Snapshot AfterLastEvent;
        public bool Recovered;
        public bool Rethrown;                              // the original UI failure is on its way out: leave it alone
        public bool Finished;                              // Reparo finished the Process: every block above completes
        public ExceptionDispatchInfo Original;
        public string Outcome = "completed";
        public string IdOf(Activity a) => File + "#" + (IdRefs.TryGetValue(a, out var r) ? r : "pos" + a.Id);
    }

    public abstract class ReparoNode : NativeActivity
    {
        internal static RunState Run(NativeActivityContext c) => (RunState)c.Properties.Find(RunState.Key);

        internal static PropertyDescriptor Prop(NativeActivityContext c, string name) =>
            c.DataContext.GetProperties().Find(name, false)
            ?? throw new InvalidOperationException("Reparo: no Variable named '" + name + "' in scope");

        internal static Snapshot Take(NativeActivityContext c, RunState run, bool verbose)
        {
            var s = new Snapshot();
            foreach (PropertyDescriptor p in c.DataContext.GetProperties())
            {
                if (!run.VarTypes.ContainsKey(p.Name)) continue;
                var v = p.GetValue(c.DataContext);
                var why = Json.WhyNotCarried(v, p.PropertyType, out var text);
                s.Live[p.Name] = v;
                if (why == null) s.Carried[p.Name] = text;
                else { s.NotCarried.Add(p.Name); if (verbose) Trace.W($"    Variable {p.Name} ({p.PropertyType.Name}) not carried: {why}"); }
            }
            return s;
        }

        internal static void RestoreIfPending(NativeActivityContext c, RunState run)
        {
            if (run.Restore == null) return;
            foreach (var kv in run.Restore)
            {
                Prop(c, kv.Key).SetValue(c.DataContext, kv.Value);
                var shown = Json.Text(kv.Value);
                Trace.W($"  restored {kv.Key} = {(shown.Length > 140 ? shown.Substring(0, 140) + "..." : shown)}");
            }
            run.Restore = null;
        }

        internal abstract IEnumerable<Activity> BridgeChildren();

        /// The topmost Bridge blocks at or under `a`, looking through UiPath activities (a card, a Sequence).
        internal static IEnumerable<ReparoNode> TopBridge(Activity a)
        {
            if (a is ReparoNode n) { yield return n; yield break; }
            foreach (var ch in WorkflowInspectionServices.GetActivities(a))
                foreach (var b in TopBridge(ch)) yield return b;
        }

        internal static bool Holds(RunState run, Activity a, string eventId) =>
            TopBridge(a).Any(n => n is ReparoEvent e ? run.IdOf(e) == eventId : n.BridgeChildren().Any(ch => Holds(run, ch, eventId)));

        internal bool HoldsTarget(RunState run) => run.Target != null && Holds(run, this, run.Target.EventId);

        internal static int StartIndex(RunState run, IList<Activity> list)
        {
            if (run.Target == null) return 0;
            for (int i = 0; i < list.Count; i++)
                if (Holds(run, list[i], run.Target.EventId)) return i;
            throw new InvalidOperationException("Reparo: target " + run.Target + " is not under this block");
        }

        /// A block entered while a jump or a finish is under way, that doesn't hold the target, does nothing.
        internal bool Skip(RunState run) => run.Finished || (run.Target != null && !HoldsTarget(run));

        internal static void RequireBridgeBlocks(NativeActivityMetadata m, string owner, IEnumerable<Activity> list, params Type[] allowed)
        {
            foreach (var a in list)
                if (a == null || !allowed.Any(t => t.IsInstanceOfType(a)))
                    m.AddValidationError($"Reparo: {owner} may hold only {string.Join(", ", allowed.Select(t => t.Name))}, not {a?.GetType().Name}. Put UiPath activities inside a Reparo Event.");
        }

        internal static bool IsUiFailure(Exception e)
        {
            for (var t = e.GetType(); t != null; t = t.BaseType)
                if (t.FullName == "UiPath.Core.CoreAPIException" || (t.Namespace ?? "").StartsWith("UiPath.UIAutomationNext.Exceptions")) return true;
            return false;
        }
    }

    /// Runs a list of children one after another, entering at the Continuation's target.
    public abstract class ReparoSequenceBlock : ReparoNode
    {
        private readonly Variable<int> index = new();
        private readonly Variable<int> list = new();
        protected readonly Variable<int> depth = new();

        protected abstract IList<Activity> ListAt(int which);

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.AddImplementationVariable(index);
            metadata.AddImplementationVariable(list);
            metadata.AddImplementationVariable(depth);
        }

        protected void RunList(NativeActivityContext c, int which)
        {
            var run = Run(c);
            var l = ListAt(which);
            list.Set(c, which);
            int i = StartIndex(run, l);
            if (i >= l.Count) { ListDone(c); return; }
            index.Set(c, i);
            c.ScheduleActivity(l[i], OnChildDone);
        }

        private void OnChildDone(NativeActivityContext c, ActivityInstance done)
        {
            if (done.State != ActivityInstanceState.Closed) return;
            var run = Run(c);
            if (run.Finished) return;                                   // complete: Reparo finished the Process
            if (run.Target != null)                                     // a jump coming up from an Event
            {
                if (!HoldsTarget(run)) { Trace.W($"  jump passes up through {GetType().Name} '{DisplayName}'"); return; }
                Trace.W($"  jump re-enters at {GetType().Name} '{DisplayName}'");
                while (run.Passes.Count > depth.Get(c)) run.Passes.RemoveAt(run.Passes.Count - 1);
                RestoreIfPending(c, run);
                Reenter(c);
                return;
            }
            var l = ListAt(list.Get(c));
            int i = index.Get(c) + 1;
            if (i < l.Count) { index.Set(c, i); c.ScheduleActivity(l[i], OnChildDone); }
            else ListDone(c);
        }

        protected virtual void ListDone(NativeActivityContext c) { }
        protected abstract void Reenter(NativeActivityContext c);
    }

    /// The Recovery's wait for Reparo (as #48): a fake Reparo that writes the payload, reports the UI while it "acts",
    /// then answers from fake-done.json (or "retry the failed Event").
    public sealed class ReparoRecoveryWait : AsyncCodeActivity<string>
    {
        public InArgument<string> Payload { get; set; }
        public InArgument<string> FakeDonePath { get; set; }

        protected override IAsyncResult BeginExecute(AsyncCodeActivityContext context, AsyncCallback callback, object state)
        {
            var payload = Payload.Get(context);
            var donePath = FakeDonePath.Get(context);
            Trace.W($"  Recovery wait starts on thread {Environment.CurrentManagedThreadId}");
            var cts = new CancellationTokenSource();
            context.UserState = cts;
            var task = Task.Run(() => FakeReparo(payload, donePath, cts.Token), cts.Token);
            var tcs = new TaskCompletionSource<string>(state);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerExceptions);
                else if (t.IsCanceled) tcs.TrySetCanceled();
                else tcs.TrySetResult(t.Result);
                callback?.Invoke(tcs.Task);
            });
            return tcs.Task;
        }

        protected override string EndExecute(AsyncCodeActivityContext context, IAsyncResult result) => ((Task<string>)result).Result;

        protected override void Cancel(AsyncCodeActivityContext context)
        {
            Trace.W("  Recovery wait CANCELED");
            (context.UserState as CancellationTokenSource)?.Cancel();
            base.Cancel(context);
        }

        internal static string Window() => Screen.Describe();

        private static string FakeReparo(string payload, string donePath, CancellationToken ct)
        {
            var dir = Path.GetDirectoryName(Trace.PathOut);
            File.WriteAllText(Path.Combine(dir, "recovery-1.json"), JToken.Parse(payload).ToString(Formatting.Indented));
            Trace.W($"  fake Reparo: payload written; first screenshot: {Screen.Shot("recovery-start")}; UI {Window()}");
            JObject done = null;
            if (!string.IsNullOrEmpty(donePath) && File.Exists(donePath)) { done = JObject.Parse(File.ReadAllText(donePath)); File.Delete(donePath); }
            int delay = done?["delay_s"]?.Value<int>() ?? 8;
            for (int i = 0; i < delay; i++)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(1000);
                if (i == 1 && done?["ui_command"] != null)
                {
                    File.WriteAllText(Path.Combine(dir, "app-cmd.txt"), done["ui_command"].Value<string>());
                    Trace.W($"  fake Reparo acts on the UI: {done["ui_command"]}");
                }
                if (i == delay / 2) Trace.W($"  fake Reparo, mid-Recovery: UI {Window()}");
            }
            var p = JObject.Parse(payload);
            var content = new JObject
            {
                ["success"] = true,
                ["process_finished"] = false,
                ["continue_from_model_event_id"] = p["errored_act"]["model_event_id"],
                ["loop_iteration"] = p["loop_iteration"],
                ["variables"] = p["variables"],
            };
            if (done != null)
                foreach (var prop in done.Properties())
                    if (prop.Name == "variables") foreach (var v in ((JObject)prop.Value).Properties()) ((JObject)content["variables"])[v.Name] = v.Value;
                    else if (prop.Name != "delay_s" && prop.Name != "ui_command") content[prop.Name] = prop.Value;
            Trace.W("  fake Reparo answers done: " + content.ToString(Formatting.None));
            return content.ToString(Formatting.None);
        }
    }

    /// The Recovery itself, shared by Reparo Event (RecoverAt = Event) and Reparo Process (RecoverAt = Process).
    internal static class Recovery
    {
        /// The payload, or null when this failure is not handed to Reparo (then it propagates untouched).
        public static string Begin(NativeActivityFaultContext c, RunState run, Exception e, ActivityInstance from, string where)
        {
            var ev = run.CurrentEvent;
            Trace.W($"FAULT reached {where}: {e.GetType().Name}: {e.Message.Split('\n')[0]} (in Event {(ev == null ? "none" : run.IdOf(ev))}, via {from.Activity.DisplayName})");
            Trace.W($"  UI at the fault: {ReparoRecoveryWait.Window()}");
            if (run.Rethrown) { Trace.W("  the original UI failure, rethrown by the Bridge: left to propagate"); return null; }
            if (ev == null || !ReparoNode.IsUiFailure(e) || run.Recovered)
            {
                if (run.Recovered) run.Outcome = "continuation_failed";
                Trace.W($"  not handed to Reparo ({(run.Recovered ? "the run's one Recovery is spent" : "not a UI failure in an Event")}): left to propagate; outcome {run.Outcome}");
                return null;
            }
            c.HandleFault();
            run.Recovered = true;
            run.Original = ExceptionDispatchInfo.Capture(e);
            var eventId = run.IdOf(ev);
            var request = new JObject
            {
                ["contract_version"] = 2,
                ["task_name"] = run.Process.DisplayName,
                ["errored_act"] = new JObject { ["model_event_id"] = eventId, ["event_name"] = ev.DisplayName + Target.Suffix(run.CurrentPasses), ["error_code"] = e.GetType().Name, ["error_description"] = e.Message.Split('\n')[0] },
                ["loop_iteration"] = new JArray(run.CurrentPasses),
                ["variables"] = new JObject(run.AtEventStart.Carried.Select(kv => new JProperty(kv.Key, Json.Parse(kv.Value)))),
                ["ui_log"] = new JArray(run.UiLog),
            };
            Trace.W($"  Recovery requested by {where} for {eventId}{Target.Suffix(run.CurrentPasses)}");
            return request.ToString(Formatting.None);
        }

        /// What to do with Reparo's answer: "rethrow", "finish", "here" (this Event, same passes) or "jump".
        public static string Decide(RunState run, JObject a, ReparoEvent self)
        {
            string rejected = null;
            if (a["success"]?.Value<bool>() != true) { run.Outcome = "recovery_failed"; rejected = "the Recovery failed"; }
            else if (a["process_finished"]?.Value<bool>() == true) { run.Finished = true; Trace.W("  Reparo finished the Process: every block above completes"); return "finish"; }
            else rejected = run.Process.Check(run, a);
            if (rejected != null)
            {
                if (run.Outcome == "completed") run.Outcome = "continuation_rejected";
                Trace.W($"  NO Continuation: {rejected}. outcome {run.Outcome}. Rethrowing the original {run.Original.SourceException.GetType().Name}");
                run.Rethrown = true;
                ReparoProcess.LastRethrown = run.Original.SourceException;
                return "rethrow";
            }
            if (self != null && run.Target.EventId == run.IdOf(self) && run.Target.Passes.SequenceEqual(run.CurrentPasses)) return "here";
            return "jump";
        }
    }

    [ContentProperty("Nodes")]
    [DisplayName("Reparo Process")]
    public sealed class ReparoProcess : ReparoNode
    {
        public Collection<Activity> Nodes { get; } = new();
        public Collection<Variable> Variables { get; } = new();
        public InArgument<string> FakeDonePath { get; set; }
        /// PROTOTYPE: "Event" (Reparo Event's FaultCallback runs the Recovery) or "Process" (#48).
        public string RecoverAt { get; set; } = "Event";
        /// PROTOTYPE: when set, every card's CloseMode is overwritten with this value at run start.
        public string CardCloseOverride { get; set; }
        /// PROTOTYPE: the TrackingParticipant sleeps this long on the first fault record it sees.
        public int TrackHoldMs { get; set; }
        internal string FakeDonePathValue;

        private readonly Variable<int> index = new();
        private readonly Variable<string> payload = new();
        private readonly Variable<string> donePath = new();
        private ReparoRecoveryWait wait;

        internal override IEnumerable<Activity> BridgeChildren() => Nodes;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.SetChildrenCollection(Nodes);
            metadata.SetVariablesCollection(Variables);
            metadata.AddImplementationVariable(index);
            metadata.AddImplementationVariable(payload);
            metadata.AddImplementationVariable(donePath);
            wait ??= new ReparoRecoveryWait { DisplayName = "Reparo Recovery", Payload = new InArgument<string>(payload), FakeDonePath = new InArgument<string>(donePath) };
            metadata.AddImplementationChild(wait);
            metadata.AddDefaultExtensionProvider(() => new ProbeTracker());
            // PROTOTYPE: CardCloseOverride before WF prepares the cards (a parent's CacheMetadata runs before its
            // children's), walking the definition by reflection, since GetActivities would prepare them first.
            if (CardCloseOverride != null && !overridden)
            {
                overridden = true;
                foreach (var card in Walk(Nodes).Where(a => a.GetType().Name == "NApplicationCard"))
                    Trace.W($"  CacheMetadata override of '{card.DisplayName}' CloseMode -> {CardCloseOverride}: {CardModes.SetClose(card, CardCloseOverride)}");
            }
        }

        private bool overridden;

        private static IEnumerable<Activity> Walk(IEnumerable<Activity> list)
        {
            foreach (var a in list.Where(x => x != null))
            {
                yield return a;
                foreach (var p in a.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    object v; try { v = p.GetValue(a); } catch { continue; }
                    IEnumerable<Activity> kids = v switch
                    {
                        Activity one => new[] { one },
                        ActivityDelegate d => d.GetType().GetProperty("Handler")?.GetValue(d) is Activity h ? new[] { h } : Array.Empty<Activity>(),
                        IEnumerable<Activity> many => many,
                        _ => Array.Empty<Activity>(),
                    };
                    foreach (var k in Walk(kids)) yield return k;
                }
            }
        }

        protected override void Execute(NativeActivityContext c)
        {
            var run = new RunState { Process = this };
            var root = RootOf(this);
            run.File = root.GetType().Name + ".xaml";
            foreach (var pn in new[] { "IdRefDictionary", "ConstructorIdRefDictionary" })
                if (root.GetType().GetProperty(pn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(root) is Dictionary<object, string> d)
                    foreach (var kv in d) run.IdRefs[kv.Key] = kv.Value;
            foreach (var v in Variables) run.VarTypes[v.Name] = v.Type;
            c.Properties.Add(RunState.Key, run);
            donePath.Set(c, FakeDonePath.Get(c));
            FakeDonePathValue = FakeDonePath.Get(c);
            ProbeTracker.HoldMs = TrackHoldMs;
            ProbeTracker.Held = false;
            Trace.W($"=== Process '{DisplayName}' id={run.IdOf(this)}; RecoverAt={RecoverAt}; CardCloseOverride={CardCloseOverride ?? "-"}; TrackHoldMs={TrackHoldMs}");
            Describe(run, this, "", false);
            Start(c, run);
        }

        private void Start(NativeActivityContext c, RunState run)
        {
            int i = StartIndex(run, Nodes);
            if (i >= Nodes.Count) { Finish(run); return; }
            index.Set(c, i);
            c.ScheduleActivity(Nodes[i], OnChildDone, OnFault);
        }

        private void OnChildDone(NativeActivityContext c, ActivityInstance done)
        {
            var run = Run(c);
            if (done.State != ActivityInstanceState.Closed) return;
            if (run.Finished) { Finish(run); return; }
            if (run.Target != null) { Trace.W("  jump reaches Reparo Process: entering at " + run.Target + "; UI " + ReparoRecoveryWait.Window()); run.Passes.Clear(); Start(c, run); return; }
            int i = index.Get(c) + 1;
            if (i < Nodes.Count) { index.Set(c, i); c.ScheduleActivity(Nodes[i], OnChildDone, OnFault); return; }
            Finish(run);
        }

        private static void Finish(RunState run)
        {
            Trace.W("=== Process completed. UI log:");
            foreach (var l in run.UiLog) Trace.W("  " + l);
            Trace.W("report_result outcome: " + run.Outcome + "; UI " + ReparoRecoveryWait.Window());
        }

        // RecoverAt = Process: #48's path, kept to compare. With RecoverAt = Event this only lets failures through.
        private void OnFault(NativeActivityFaultContext c, Exception e, ActivityInstance from)
        {
            var run = Run(c);
            if (RecoverAt != "Process")
            {
                Trace.W($"FAULT reached Reparo Process: {e.GetType().Name} ({(run.Rethrown ? "the rethrown original" : run.Recovered ? "after the Recovery" : "not handled at an Event")}); UI {ReparoRecoveryWait.Window()}");
                if (run.Recovered && !run.Rethrown) run.Outcome = "continuation_failed";
                return;
            }
            var p = Recovery.Begin(c, run, e, from, "Reparo Process");
            if (p == null) return;
            payload.Set(c, p);
            c.ScheduleActivity<string>(wait, OnAnswer);
        }

        private void OnAnswer(NativeActivityContext c, ActivityInstance done, string answer)
        {
            var run = Run(c);
            switch (Recovery.Decide(run, JObject.Parse(answer), null))
            {
                case "rethrow": run.Original.Throw(); break;
                case "finish": Finish(run); break;
                default: Trace.W("Continuation: entering at " + run.Target + "; UI " + ReparoRecoveryWait.Window()); run.Passes.Clear(); run.CurrentEvent = null; Start(c, run); break;
            }
        }

        /// Reparo's Continuation, checked against this Process (as #48); null when it can be entered.
        internal string Check(RunState run, JObject a)
        {
            const string answer = "Reparo's Continuation";
            var id = a["continue_from_model_event_id"]?.Value<string>();
            if (string.IsNullOrEmpty(id)) return $"{answer} names no Event and does not finish the Process";
            var path = PathTo(run, this, id, new List<ReparoNode>());
            if (path == null) return $"{answer} names {id}, which is not an Event of {run.File}";
            var loops = path.OfType<ReparoForEach>().ToList();
            var passes = (a["loop_iteration"] as JArray)?.Select(x => x.Value<int>()).ToList() ?? new List<int>();
            if (passes.Count != loops.Count) return $"{answer} gives {passes.Count} loop passes for {id}, which is inside {loops.Count} loops";
            var restore = new Dictionary<string, object>();
            var returned = a["variables"] as JObject ?? new JObject();
            foreach (var kv in run.VarTypes)
            {
                run.AtEventStart.Live.TryGetValue(kv.Key, out var live);
                if (run.AtEventStart.NotCarried.Contains(kv.Key)) { restore[kv.Key] = live; continue; }
                var json = returned.TryGetValue(kv.Key, out var r) ? r : Json.Parse(run.AtEventStart.Carried[kv.Key]);
                try { restore[kv.Key] = Json.To(json, kv.Value, live); }
                catch (Exception e) { return $"{answer} sets Variable {kv.Key} to {json.ToString(Formatting.None)}, which is not a {kv.Value.Name} ({e.GetType().Name})"; }
            }
            for (int d = 0; d < loops.Count; d++)
            {
                if (!(restore.TryGetValue(loops[d].Over, out var list) && list is IList items)) return $"loop {loops[d].DisplayName} has no list Variable {loops[d].Over}";
                if (passes[d] < 0 || passes[d] >= items.Count) return $"{answer} names pass {passes[d]} of loop {loops[d].DisplayName}, but {loops[d].Over} has {items.Count} items";
            }
            run.Target = new Target { EventId = id, Passes = passes };
            run.Restore = restore;
            return null;
        }

        private static List<ReparoNode> PathTo(RunState run, ReparoNode at, string id, List<ReparoNode> path)
        {
            if (at is ReparoEvent e) return run.IdOf(e) == id ? path : null;
            foreach (var ch in at.BridgeChildren().SelectMany(TopBridge))
            {
                var found = PathTo(run, ch, id, new List<ReparoNode>(path) { ch });
                if (found != null) return found;
            }
            return null;
        }

        public static Exception LastRethrown;

        private static Activity RootOf(Activity a)
        {
            var parent = typeof(Activity).GetProperty("Parent", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var p = a; p != null; p = parent.GetValue(p) as Activity) a = p;
            return a;
        }

        /// The tree, with every UiPath card's Open/Close modes (and the override, when set).
        private void Describe(RunState run, Activity a, string indent, bool inEvent)
        {
            var tn = a.GetType().Name;
            if (a is ReparoNode || tn == "NApplicationCard")
            {
                var extra = "";
                if (tn == "NApplicationCard")
                {
                    extra = $" [card {(inEvent ? "INSIDE an Event" : "holding Bridge blocks")}; {CardModes.Read(a)}]";
                }
                Trace.W($"  {indent}{tn} '{a.DisplayName}' {run.IdOf(a)}{extra}");
                indent += "  ";
            }
            foreach (var ch in WorkflowInspectionServices.GetActivities(a))
                if (!(ch is ReparoRecoveryWait)) Describe(run, ch, indent, inEvent || a is ReparoEvent);
        }
    }

    /// PROTOTYPE: read and write a card's modes by reflection (the Bridge's validation would read the same at design time).
    internal static class CardModes
    {
        static readonly string[] Names = { "OpenMode", "CloseMode", "AttachMode", "WindowResize", "HealingAgentBehavior" };

        public static string Read(Activity card) => string.Join(", ", Names.Select(n =>
        {
            var p = card.GetType().GetProperty(n);
            if (p == null) return n + "=?";
            var v = p.GetValue(card);
            return $"{n}:{p.PropertyType.Name}={Show(v)}";
        }));

        static string Show(object v)
        {
            if (v is Argument arg) { var ex = arg.Expression; if (ex == null) return "(unset)"; var lit = ex.GetType().GetProperty("Value"); return ex.GetType().Name + "(" + (lit?.GetValue(ex) ?? ex.ToString()) + ")"; }
            return v?.ToString() ?? "null";
        }

        public static string SetClose(Activity card, string value)
        {
            try
            {
                var p = card.GetType().GetProperty("CloseMode");
                var t = p.PropertyType;
                if (t.IsEnum) p.SetValue(card, Enum.Parse(t, value));
                else if (typeof(InArgument).IsAssignableFrom(t))
                {
                    var et = t.GetGenericArguments()[0];
                    var ev = et.IsEnum ? Enum.Parse(et, value) : Convert.ChangeType(value, et);
                    p.SetValue(card, Activator.CreateInstance(t, ev));
                }
                else return "unsupported type " + t.FullName;
                return "set";
            }
            catch (Exception e) { return "FAILED " + e.GetType().Name + ": " + e.Message; }
        }
    }

    [ContentProperty("Events")]
    [DisplayName("Reparo Activity")]
    public sealed class ReparoActivity : ReparoSequenceBlock
    {
        public Collection<Activity> Events { get; } = new();
        internal override IEnumerable<Activity> BridgeChildren() => Events;
        protected override IList<Activity> ListAt(int which) => Events;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.SetChildrenCollection(Events);
            RequireBridgeBlocks(metadata, "Reparo Activity '" + DisplayName + "'", Events, typeof(ReparoEvent));
        }

        protected override void Execute(NativeActivityContext c)
        {
            var run = Run(c);
            if (Skip(run)) { Trace.W($"Activity '{DisplayName}' skipped (jump or finish under way)"); return; }
            depth.Set(c, run.Passes.Count);
            RestoreIfPending(c, run);
            Trace.W($"Activity '{DisplayName}' entered{Target.Suffix(run.Passes)}");
            RunList(c, 0);
        }

        protected override void Reenter(NativeActivityContext c) => RunList(c, 0);
    }

    [ContentProperty("Body")]
    [DisplayName("Reparo Event")]
    public sealed class ReparoEvent : ReparoNode
    {
        public Activity Body { get; set; }
        internal override IEnumerable<Activity> BridgeChildren() => Enumerable.Empty<Activity>();

        private readonly Variable<bool> recovering = new();
        private readonly Variable<bool> bodyEnded = new();
        private readonly Variable<string> answer = new();
        private readonly Variable<string> payload = new();
        private readonly Variable<string> donePath = new();
        private ReparoRecoveryWait wait;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.SetChildrenCollection(Body == null ? new Collection<Activity>() : new Collection<Activity> { Body });
            foreach (var v in new[] { (Variable)recovering, bodyEnded, answer, payload, donePath }) metadata.AddImplementationVariable(v);
            wait ??= new ReparoRecoveryWait { DisplayName = "Reparo Recovery (Event)", Payload = new InArgument<string>(payload), FakeDonePath = new InArgument<string>(donePath) };
            metadata.AddImplementationChild(wait);
        }

        protected override void Execute(NativeActivityContext c)
        {
            var run = Run(c);
            if (Skip(run)) { Trace.W($"  Event '{DisplayName}' skipped (jump or finish under way)"); return; }
            RestoreIfPending(c, run);
            StartBody(c, run);
        }

        private void StartBody(NativeActivityContext c, RunState run)
        {
            var id = run.IdOf(this);
            if (run.Target != null && run.Target.EventId == id) { Trace.W("  Continuation reached " + run.Target); run.Target = null; }
            run.CurrentEvent = this;
            run.CurrentPasses = run.Passes.ToList();
            run.AtEventStart = Take(c, run, false);
            recovering.Set(c, false);
            bodyEnded.Set(c, false);
            answer.Set(c, null);
            Trace.W($"  Event '{DisplayName}'{Target.Suffix(run.CurrentPasses)} {id} starts; UI {ReparoRecoveryWait.Window()}");
            if (Body == null) { Completed(run); return; }
            if (run.Process.RecoverAt == "Event") c.ScheduleActivity(Body, OnBodyDone, OnBodyFault);
            else c.ScheduleActivity(Body, OnBodyDone);
        }

        private void Completed(RunState run)
        {
            run.UiLog.Add($"{DisplayName}{Target.Suffix(run.CurrentPasses)}  [{run.IdOf(this)}]");
            run.CurrentEvent = null;
        }

        private void OnBodyDone(NativeActivityContext c, ActivityInstance done)
        {
            var run = Run(c);
            if (recovering.Get(c))
            {
                Trace.W($"  the Event's faulted body ended: {done.State}; UI {ReparoRecoveryWait.Window()}");
                bodyEnded.Set(c, true);
                Proceed(c, run);
                return;
            }
            if (done.State != ActivityInstanceState.Closed) return;
            Completed(run);
        }

        private void OnBodyFault(NativeActivityFaultContext c, Exception e, ActivityInstance from)
        {
            var run = Run(c);
            var p = Recovery.Begin(c, run, e, from, $"Reparo Event '{DisplayName}'");
            if (p == null) return;
            recovering.Set(c, true);
            payload.Set(c, p);
            donePath.Set(c, run.Process.FakeDonePathValue);
            c.ScheduleActivity<string>(wait, OnAnswer);
        }

        private void OnAnswer(NativeActivityContext c, ActivityInstance done, string a)
        {
            if (done.State != ActivityInstanceState.Closed) return;
            answer.Set(c, a);
            Proceed(c, Run(c));
        }

        private void Proceed(NativeActivityContext c, RunState run)
        {
            if (!bodyEnded.Get(c) || answer.Get(c) == null) return;
            var a = JObject.Parse(answer.Get(c));
            recovering.Set(c, false);
            answer.Set(c, null);
            switch (Recovery.Decide(run, a, this))
            {
                case "rethrow": run.Original.Throw(); break;
                case "finish": run.CurrentEvent = null; break;            // complete; the blocks above complete too
                case "here":
                    Trace.W($"Continuation in place: running '{DisplayName}' again inside its scope; UI {ReparoRecoveryWait.Window()}");
                    RestoreIfPending(c, run);
                    StartBody(c, run);
                    break;
                default:
                    Trace.W($"Continuation jumps from '{DisplayName}' to {run.Target}: completing upward to the block that holds it; UI {ReparoRecoveryWait.Window()}");
                    run.CurrentEvent = null;
                    break;
            }
        }
    }

    [ContentProperty("Nodes")]
    [DisplayName("Reparo For Each")]
    public sealed class ReparoForEach : ReparoSequenceBlock
    {
        public string Over { get; set; }
        public string Item { get; set; }
        public Collection<Activity> Nodes { get; } = new();
        private readonly Variable<int> pass = new();

        internal override IEnumerable<Activity> BridgeChildren() => Nodes;
        protected override IList<Activity> ListAt(int which) => Nodes;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.AddImplementationVariable(pass);
            metadata.SetChildrenCollection(Nodes);
        }

        protected override void Execute(NativeActivityContext c)
        {
            var run = Run(c);
            if (Skip(run)) return;
            depth.Set(c, run.Passes.Count);
            RestoreIfPending(c, run);
            Reenter(c);
        }

        protected override void Reenter(NativeActivityContext c)
        {
            var run = Run(c);
            int d = depth.Get(c);
            StartPass(c, run.Target != null && run.Target.Passes.Count > d ? run.Target.Passes[d] : 0);
        }

        private void StartPass(NativeActivityContext c, int p)
        {
            var run = Run(c);
            var items = Prop(c, Over).GetValue(c.DataContext) as IList ?? throw new InvalidOperationException($"Reparo For Each: {Over} is not a list");
            int d = depth.Get(c);
            while (run.Passes.Count > d) run.Passes.RemoveAt(run.Passes.Count - 1);
            if (p >= items.Count) return;
            pass.Set(c, p);
            run.Passes.Add(p);
            Prop(c, Item).SetValue(c.DataContext, items[p]);
            Trace.W($"For Each '{DisplayName}' pass {p}: {Item} = {items[p]}");
            RunList(c, 0);
        }

        protected override void ListDone(NativeActivityContext c) => StartPass(c, pass.Get(c) + 1);
    }
}
