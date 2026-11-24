// PROTOTYPE for ReparoHome#48: Continuation on the Bridge's own blocks (shape from ReparoHome#46).
// What changes from #46's Blocks.cs:
//  - the Recovery waits in an async child activity (ReparoRecoveryWait), not inside the FaultCallback;
//  - Reparo's `done` is checked against the Process (Event id, one pass per Reparo For Each, pass in range,
//    Variables that convert to their declared type); a rejection or a failed Recovery rethrows the original
//    UI exception, so the author's own handlers outside Reparo Process see it as if Reparo weren't there;
//  - at most one Recovery per run; no cold start;
//  - Variables go through Newtonsoft JSON (text, as on the wire). Only a Variable whose value survives the round
//    trip is carried to Reparo; any other keeps its live value from the failing Event's start.
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

    /// Variables <-> JSON, the way the Bridge would do it.
    internal static class Json
    {
        public static readonly JsonSerializer S = JsonSerializer.CreateDefault();

        public static JToken Parse(string text)
        {
            using var r = new JsonTextReader(new StringReader(text)) { FloatParseHandling = FloatParseHandling.Decimal, DateParseHandling = DateParseHandling.None };
            return JToken.ReadFrom(r);
        }

        public static string Text(object value) => value == null ? "null" : JToken.FromObject(value, S).ToString(Formatting.None);

        /// JSON -> the declared type. A DataTable takes its columns from the live value, so column types survive.
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

        /// Null when `value` survives value -> JSON text -> declared type -> JSON text unchanged, else why not.
        public static string WhyNotCarried(object value, Type declared, out string text)
        {
            text = null;
            try
            {
                text = Text(value);
                var back = To(Parse(text), declared, value);
                if (value != null && back != null && back.GetType() != value.GetType()) return $"comes back as {back.GetType().Name}, was {value.GetType().Name}";
                var again = Text(back);
                if (again != text) return "JSON differs after the round trip";
                if (value is System.Security.SecureString) return "a SecureString serialises to its length only";
                return null;
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
        }
    }

    internal sealed class Snapshot
    {
        public Dictionary<string, string> Carried = new();   // name -> JSON text, what Reparo receives
        public Dictionary<string, object> Live = new();      // name -> the value itself (a reference), for every Variable
        public HashSet<string> NotCarried = new();           // kept from Live on a Continuation, never sent to Reparo
    }

    /// Shared by every block of one run, as an execution property.
    internal sealed class RunState
    {
        public const string Key = "Reparo.Run";
        public string File;
        public Dictionary<object, string> IdRefs = new();
        public Dictionary<string, Type> VarTypes = new();
        public Target Target;                              // set while a Continuation is still entering
        public Dictionary<string, object> Restore;          // Variables to write on the way in (already converted)
        public List<int> Passes = new();                   // current pass of each enclosing loop, outermost first
        public List<string> UiLog = new();
        public ReparoEvent CurrentEvent;
        public List<int> CurrentPasses;
        public Snapshot AtEventStart;
        public Snapshot AfterLastEvent;

        // The one Recovery of the run.
        public bool Recovered;                             // a later UI failure is never handed over
        public ExceptionDispatchInfo Original;             // the UI failure, rethrown when Reparo can't continue the run
        public ActivityInstance Faulted;                   // the Process child the fault came through
        public bool FaultedClosed;
        public JObject Answer;                             // Reparo's done.content, once it arrives
        public string Outcome = "completed";               // what report_result would send

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

        /// The first block a Continuation enters writes the Variables decided when Reparo answered.
        internal static void RestoreIfPending(NativeActivityContext c, RunState run)
        {
            if (run.Restore == null) return;
            foreach (var kv in run.Restore)
            {
                var p = Prop(c, kv.Key);
                p.SetValue(c.DataContext, kv.Value);
                var shown = kv.Value is DataTable t ? $"DataTable[{t.Rows.Count} rows; {string.Join(",", t.Columns.Cast<DataColumn>().Select(x => x.ColumnName + ":" + x.DataType.Name))}]" : Json.Text(kv.Value);
                Trace.W($"  restored {kv.Key} = {(shown.Length > 140 ? shown.Substring(0, 140) + "..." : shown)}");
            }
            run.Restore = null;
        }

        /// The Bridge blocks directly under a block, for walking the definition.
        internal abstract IEnumerable<Activity> BridgeChildren();

        internal bool Holds(RunState run, string eventId) =>
            this is ReparoEvent e ? run.IdOf(e) == eventId : BridgeChildren().OfType<ReparoNode>().Any(n => n.Holds(run, eventId));

        /// Where to start in `list`: the child on the way to the Continuation's target, else the first.
        internal static int StartIndex(RunState run, IList<Activity> list)
        {
            if (run.Target == null) return 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i] is ReparoNode n && n.Holds(run, run.Target.EventId)) return i;
            throw new InvalidOperationException("Reparo: target " + run.Target + " is not under this block");
        }

        internal static void RequireBridgeBlocks(NativeActivityMetadata m, string owner, IEnumerable<Activity> list, params Type[] allowed)
        {
            foreach (var a in list)
                if (a == null || !allowed.Any(t => t.IsInstanceOfType(a)))
                    m.AddValidationError($"Reparo: {owner} may hold only {string.Join(", ", allowed.Select(t => t.Name))}, not {a?.GetType().Name}. Put UiPath activities inside a Reparo Event.");
        }
    }

    /// Runs a list of children one after another, entering at the Continuation's target.
    public abstract class ReparoSequenceBlock : ReparoNode
    {
        private readonly Variable<int> index = new();
        private readonly Variable<int> list = new();   // which list is running (Reparo If has two)

        protected abstract IList<Activity> ListAt(int which);

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.AddImplementationVariable(index);
            metadata.AddImplementationVariable(list);
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
            if (done.State != ActivityInstanceState.Closed) return;   // canceled: a Continuation re-enters from the Process
            var l = ListAt(list.Get(c));
            int i = index.Get(c) + 1;
            if (i < l.Count) { index.Set(c, i); c.ScheduleActivity(l[i], OnChildDone); }
            else ListDone(c);
        }

        /// Called when the running list ends; a loop starts its next pass here.
        protected virtual void ListDone(NativeActivityContext c) { }
    }

    /// The Recovery's wait for Reparo: async, so the workflow thread stays free (Stop, cancellation) while it runs.
    /// PROTOTYPE: a fake Reparo. It writes the payload to recovery-1.json, reports what the UI looks like while it
    /// "acts", optionally sends a UI command to SpendApp, then answers from fake-done.json (or "retry the failed Event").
    public sealed class ReparoRecoveryWait : AsyncCodeActivity<string>
    {
        public InArgument<string> Payload { get; set; }
        public InArgument<string> FakeDonePath { get; set; }

        protected override IAsyncResult BeginExecute(AsyncCodeActivityContext context, AsyncCallback callback, object state)
        {
            var payload = Payload.Get(context);
            var donePath = FakeDonePath.Get(context);
            Trace.W($"  Recovery wait starts on thread {Environment.CurrentManagedThreadId} (the workflow thread); fake Reparo runs off it");
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

        protected override string EndExecute(AsyncCodeActivityContext context, IAsyncResult result)
        {
            Trace.W($"  Recovery wait ends on thread {Environment.CurrentManagedThreadId}");
            return ((Task<string>)result).Result;
        }

        protected override void Cancel(AsyncCodeActivityContext context)
        {
            Trace.W("  Recovery wait CANCELED (the real Bridge sends `abort` here)");
            (context.UserState as CancellationTokenSource)?.Cancel();
            base.Cancel(context);
        }

        internal static string Window()
        {
            var ps = System.Diagnostics.Process.GetProcessesByName("SpendApp");
            if (ps.Length == 0) return "SpendApp NOT RUNNING";
            return string.Join("; ", ps.Select(p => $"SpendApp pid {p.Id} window '{p.MainWindowTitle}' handle {(p.MainWindowHandle == IntPtr.Zero ? "none" : "present")}"));
        }

        private static string FakeReparo(string payload, string donePath, CancellationToken ct)
        {
            var dir = Path.GetDirectoryName(Trace.PathOut);
            File.WriteAllText(Path.Combine(dir, "recovery-1.json"), JToken.Parse(payload).ToString(Formatting.Indented));
            Trace.W($"  fake Reparo on thread {Environment.CurrentManagedThreadId}: payload written (recovery-1.json); UI now: {Window()}");
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

    [ContentProperty("Nodes")]
    [DisplayName("Reparo Process")]
    public sealed class ReparoProcess : ReparoNode
    {
        public Collection<Activity> Nodes { get; } = new();
        public Collection<Variable> Variables { get; } = new();
        /// PROTOTYPE: a file standing in for Reparo's `done`; when absent, the fake Reparo retries the failed Event.
        public InArgument<string> FakeDonePath { get; set; }
        /// PROTOTYPE: cancel the faulted branch before the Recovery waits (true) or after Reparo answers (false).
        public bool CancelBeforeRecovery { get; set; }

        private readonly Variable<int> index = new();
        private readonly Variable<string> payload = new();
        private readonly Variable<string> donePath = new();
        private ReparoRecoveryWait wait;

        internal override IEnumerable<Activity> BridgeChildren() => Nodes;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);   // the arguments
            metadata.SetChildrenCollection(Nodes);
            metadata.SetVariablesCollection(Variables);
            metadata.AddImplementationVariable(index);
            metadata.AddImplementationVariable(payload);
            metadata.AddImplementationVariable(donePath);
            wait ??= new ReparoRecoveryWait { DisplayName = "Reparo Recovery", Payload = new InArgument<string>(payload), FakeDonePath = new InArgument<string>(donePath) };
            metadata.AddImplementationChild(wait);
            metadata.AddDefaultExtensionProvider(() => new ProbeTracker());
            RequireBridgeBlocks(metadata, "Reparo Process", Nodes, typeof(ReparoActivity), typeof(ReparoForEach), typeof(ReparoIf));
        }

        protected override void Execute(NativeActivityContext c)
        {
            var run = new RunState();
            var root = RootOf(this);
            run.File = root.GetType().Name + ".xaml";
            foreach (var pn in new[] { "IdRefDictionary", "ConstructorIdRefDictionary" })
                if (root.GetType().GetProperty(pn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(root) is Dictionary<object, string> d)
                    foreach (var kv in d) run.IdRefs[kv.Key] = kv.Value;
            foreach (var v in Variables) run.VarTypes[v.Name] = v.Type;
            c.Properties.Add(RunState.Key, run);
            donePath.Set(c, FakeDonePath.Get(c));
            Trace.W($"=== Process '{DisplayName}' id={run.IdOf(this)}; CancelBeforeRecovery={CancelBeforeRecovery}; Variables: {string.Join(", ", run.VarTypes.Select(kv => kv.Key + ":" + kv.Value.Name))}");
            Describe(run, this, "");
            Start(c, run);
        }

        private void Start(NativeActivityContext c, RunState run)
        {
            int i = StartIndex(run, Nodes);
            if (i >= Nodes.Count) { Finish(c, run); return; }
            index.Set(c, i);
            c.ScheduleActivity(Nodes[i], OnChildDone, OnFault);
        }

        private void OnChildDone(NativeActivityContext c, ActivityInstance done)
        {
            var run = Run(c);
            if (run.Faulted != null && done == run.Faulted)
            {
                run.FaultedClosed = true;
                Trace.W($"  faulted branch '{done.Activity.DisplayName}' ended: {done.State}; UI {ReparoRecoveryWait.Window()}");
                Proceed(c, run);
                return;
            }
            int i = index.Get(c) + 1;
            if (i < Nodes.Count) { index.Set(c, i); c.ScheduleActivity(Nodes[i], OnChildDone, OnFault); return; }
            Finish(c, run);
        }

        private void Finish(NativeActivityContext c, RunState run)
        {
            Trace.W("=== Process completed. UI log:");
            foreach (var l in run.UiLog) Trace.W("  " + l);
            Trace.W("Variables after the last Event: " + JsonConvert.SerializeObject(run.AfterLastEvent?.Carried));
            Trace.W("report_result outcome: " + run.Outcome);
        }

        private void OnFault(NativeActivityFaultContext c, Exception e, ActivityInstance from)
        {
            var run = Run(c);
            var ev = run.CurrentEvent;
            Trace.W($"FAULT reached Reparo Process: {e.GetType().FullName}: {e.Message.Split('\n')[0]} (in Event {(ev == null ? "none" : run.IdOf(ev))}, via {from.Activity.DisplayName}, state {from.State}) on thread {Environment.CurrentManagedThreadId}");
            Trace.W($"  UI at the fault: {ReparoRecoveryWait.Window()}");
            if (ev == null || !IsUiFailure(e) || run.Recovered)
            {
                if (run.Recovered) run.Outcome = "continuation_failed";
                Trace.W($"  not handed to Reparo ({(run.Recovered ? "the run's one Recovery is spent" : "not a UI failure in an Event")}): left to propagate; report_result outcome {run.Outcome}");
                return;
            }
            c.HandleFault();
            run.Recovered = true;
            run.Original = ExceptionDispatchInfo.Capture(e);
            run.Faulted = from;
            var eventId = run.IdOf(ev);
            var request = new JObject
            {
                ["contract_version"] = 2,
                ["task_name"] = DisplayName,
                ["errored_act"] = new JObject { ["model_event_id"] = eventId, ["event_name"] = ev.DisplayName + Target.Suffix(run.CurrentPasses), ["error_code"] = e.GetType().Name, ["error_description"] = e.Message.Split('\n')[0] },
                ["loop_iteration"] = new JArray(run.CurrentPasses),
                ["variables"] = new JObject(run.AtEventStart.Carried.Select(kv => new JProperty(kv.Key, Json.Parse(kv.Value)))),
                ["ui_log"] = new JArray(run.UiLog),
            };
            Trace.W($"  Recovery requested for {eventId}{Target.Suffix(run.CurrentPasses)}; carried Variables: {string.Join(", ", run.AtEventStart.Carried.Keys)}; kept live: {string.Join(", ", run.AtEventStart.NotCarried)}");
            payload.Set(c, request.ToString(Formatting.None));
            if (CancelBeforeRecovery) { Trace.W("  canceling the faulted branch BEFORE the Recovery waits"); c.CancelChild(from); }
            c.ScheduleActivity<string>(wait, OnAnswer, OnWaitFault);
        }

        private void OnWaitFault(NativeActivityFaultContext c, Exception e, ActivityInstance from)
        {
            Trace.W("  the Recovery wait failed: " + e.Message);
            var run = Run(c);
            c.HandleFault();
            run.Answer = new JObject { ["success"] = false };
            run.Outcome = "recovery_unavailable";
            if (!run.FaultedClosed) c.CancelChild(run.Faulted);
            Proceed(c, run);
        }

        private void OnAnswer(NativeActivityContext c, ActivityInstance done, string answer)
        {
            var run = Run(c);
            if (done.State != ActivityInstanceState.Closed) return;
            run.Answer = JObject.Parse(answer);
            Trace.W($"  Reparo answered; faulted branch is {(run.FaultedClosed ? "already ended" : "still open: canceling it now")}");
            if (!run.FaultedClosed) c.CancelChild(run.Faulted);
            Proceed(c, run);
        }

        /// Once Reparo has answered and the faulted branch has ended: continue, finish, or rethrow the UI failure.
        private void Proceed(NativeActivityContext c, RunState run)
        {
            if (run.Answer == null || !run.FaultedClosed) return;
            var a = run.Answer;
            run.Answer = null;
            run.Faulted = null;
            string rejected = null;
            if (a["success"]?.Value<bool>() != true) { if (run.Outcome == "completed") run.Outcome = "recovery_failed"; rejected = "the Recovery failed"; }
            else if (a["process_finished"]?.Value<bool>() == true)
            {
                Trace.W("  Reparo finished the Process itself: no more Events run");
                Finish(c, run);
                return;
            }
            else rejected = Check(c, run, a);
            if (rejected != null)
            {
                if (run.Outcome == "completed") run.Outcome = "continuation_rejected";
                Trace.W($"  NO Continuation: {rejected}. report_result outcome {run.Outcome}. Rethrowing the original {run.Original.SourceException.GetType().Name}");
                LastRethrown = run.Original.SourceException;
                run.Original.Throw();
            }
            Trace.W("Continuation: entering at " + run.Target + "; UI " + ReparoRecoveryWait.Window());
            Start(c, run);
        }

        /// Reparo's Continuation, checked against this Process; null when it can be entered (Target and Restore set).
        private string Check(NativeActivityContext c, RunState run, JObject a)
        {
            const string answer = "Reparo's Continuation";
            var id = a["continue_from_model_event_id"]?.Value<string>();
            if (string.IsNullOrEmpty(id)) return $"{answer} names no Event and does not finish the Process";
            var path = PathTo(run, this, id, new List<ReparoNode>());
            if (path == null) return $"{answer} names {id}, which is not an Event of {run.File}";
            var loops = path.OfType<ReparoForEach>().ToList();
            var passes = (a["loop_iteration"] as JArray)?.Select(x => x.Value<int>()).ToList() ?? new List<int>();
            if (passes.Count != loops.Count) return $"{answer} gives {passes.Count} loop passes for {id}, which is inside {loops.Count} loops";

            // The Variables to resume with: the failing Event's start, overridden key by key by Reparo's.
            var restore = new Dictionary<string, object>();
            var returned = a["variables"] as JObject ?? new JObject();
            foreach (var kv in run.VarTypes)
            {
                run.AtEventStart.Live.TryGetValue(kv.Key, out var live);
                if (run.AtEventStart.NotCarried.Contains(kv.Key))
                {
                    if (returned.ContainsKey(kv.Key)) Trace.W($"  warning: Reparo returned {kv.Key}, which was never sent; ignored");
                    restore[kv.Key] = live;
                    continue;
                }
                var json = returned.TryGetValue(kv.Key, out var r) ? r : Json.Parse(run.AtEventStart.Carried[kv.Key]);
                try { restore[kv.Key] = Json.To(json, kv.Value, live); }
                catch (Exception e) { return $"{answer} sets Variable {kv.Key} to {json.ToString(Formatting.None)}, which is not a {kv.Value.Name} ({e.GetType().Name})"; }
            }
            foreach (var extra in returned.Properties().Where(p => !run.VarTypes.ContainsKey(p.Name)))
                Trace.W($"  warning: Reparo returned {extra.Name}, which is not a Variable of the Process; ignored");

            for (int d = 0; d < loops.Count; d++)
            {
                if (!(restore.TryGetValue(loops[d].Over, out var list) && list is IList items)) return $"loop {loops[d].DisplayName} has no list Variable {loops[d].Over}";
                if (passes[d] < 0 || passes[d] >= items.Count) return $"{answer} names pass {passes[d]} of loop {loops[d].DisplayName}, but {loops[d].Over} has {items.Count} items";
            }
            run.Target = new Target { EventId = id, Passes = passes };
            run.Restore = restore;
            run.Passes.Clear();
            run.CurrentEvent = null;
            return null;
        }

        /// The blocks from here down to the Event with `id`, or null when no Event of this file has it.
        private static List<ReparoNode> PathTo(RunState run, ReparoNode at, string id, List<ReparoNode> path)
        {
            if (at is ReparoEvent e) return run.IdOf(e) == id ? path : null;
            foreach (var ch in at.BridgeChildren().OfType<ReparoNode>())
            {
                var found = PathTo(run, ch, id, new List<ReparoNode>(path) { ch });
                if (found != null) return found;
            }
            return null;
        }

        /// PROTOTYPE: the exception the Bridge rethrew last, so the author's handler can show it is the same object.
        public static Exception LastRethrown;

        private static bool IsUiFailure(Exception e)
        {
            for (var t = e.GetType(); t != null; t = t.BaseType)
                if (t.FullName == "UiPath.Core.CoreAPIException" || (t.Namespace ?? "").StartsWith("UiPath.UIAutomationNext.Exceptions")) return true;
            return false;
        }

        private static Activity RootOf(Activity a)
        {
            var parent = typeof(Activity).GetProperty("Parent", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var p = a; p != null; p = parent.GetValue(p) as Activity) a = p;
            return a;
        }

        private static void Describe(RunState run, Activity a, string indent)
        {
            Trace.W($"  {indent}{a.GetType().Name} '{a.DisplayName}' {run.IdOf(a)}");
            if (a is ReparoNode n) foreach (var ch in n.BridgeChildren()) Describe(run, ch, indent + "  ");
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
            RestoreIfPending(c, run);
            Trace.W($"Activity '{DisplayName}' entered{Target.Suffix(run.Passes)}");
            RunList(c, 0);
        }
    }

    [ContentProperty("Body")]
    [DisplayName("Reparo Event")]
    public sealed class ReparoEvent : ReparoNode
    {
        public Activity Body { get; set; }
        internal override IEnumerable<Activity> BridgeChildren() => Enumerable.Empty<Activity>();

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.SetChildrenCollection(Body == null ? new Collection<Activity>() : new Collection<Activity> { Body });
        }

        protected override void Execute(NativeActivityContext c)
        {
            var run = Run(c);
            RestoreIfPending(c, run);
            var id = run.IdOf(this);
            if (run.Target != null && run.Target.EventId == id) { Trace.W("  Continuation reached " + run.Target); run.Target = null; }
            run.CurrentEvent = this;
            run.CurrentPasses = run.Passes.ToList();
            run.AtEventStart = Take(c, run, run.UiLog.Count == 0);
            Trace.W($"  Event '{DisplayName}'{Target.Suffix(run.CurrentPasses)} {id} starts; Variables {Short(JsonConvert.SerializeObject(run.AtEventStart.Carried))}");
            if (Body != null) c.ScheduleActivity(Body, OnBodyDone); else OnBodyDone(c, null);
        }

        private static string Short(string s) => s.Length > 300 ? s.Substring(0, 300) + "..." : s;

        private void OnBodyDone(NativeActivityContext c, ActivityInstance done)
        {
            if (done != null && done.State != ActivityInstanceState.Closed) return;
            var run = Run(c);
            run.UiLog.Add($"{DisplayName}{Target.Suffix(run.CurrentPasses)}  [{run.IdOf(this)}]");
            run.AfterLastEvent = Take(c, run, false);
            run.CurrentEvent = null;
        }
    }

    /// For each item of a list Variable (named by Over), bound to the item Variable (named by Item).
    /// The list is re-read on every pass, as in the Playwright Bridge.
    [ContentProperty("Nodes")]
    [DisplayName("Reparo For Each")]
    public sealed class ReparoForEach : ReparoSequenceBlock
    {
        public string Over { get; set; }
        public string Item { get; set; }
        public Collection<Activity> Nodes { get; } = new();
        private readonly Variable<int> pass = new();
        private readonly Variable<int> depth = new();

        internal override IEnumerable<Activity> BridgeChildren() => Nodes;
        protected override IList<Activity> ListAt(int which) => Nodes;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.AddImplementationVariable(pass);
            metadata.AddImplementationVariable(depth);
            metadata.SetChildrenCollection(Nodes);
            if (string.IsNullOrEmpty(Over) || string.IsNullOrEmpty(Item)) metadata.AddValidationError("Reparo For Each needs Over and Item (Variable names)");
            RequireBridgeBlocks(metadata, "Reparo For Each", Nodes, typeof(ReparoActivity), typeof(ReparoForEach), typeof(ReparoIf));
        }

        protected override void Execute(NativeActivityContext c)
        {
            var run = Run(c);
            RestoreIfPending(c, run);
            int d = run.Passes.Count;
            depth.Set(c, d);
            int p = run.Target != null && run.Target.Passes.Count > d ? run.Target.Passes[d] : 0;
            StartPass(c, p);
        }

        private void StartPass(NativeActivityContext c, int p)
        {
            var run = Run(c);
            var items = Prop(c, Over).GetValue(c.DataContext) as IList
                ?? throw new InvalidOperationException($"Reparo For Each: Variable {Over} is not a list");
            int d = depth.Get(c);
            while (run.Passes.Count > d) run.Passes.RemoveAt(run.Passes.Count - 1);
            if (p >= items.Count) { Trace.W($"For Each '{DisplayName}' done after {p} passes"); return; }
            pass.Set(c, p);
            run.Passes.Add(p);
            Prop(c, Item).SetValue(c.DataContext, items[p]);
            Trace.W($"For Each '{DisplayName}' pass {p}: {Item} = {items[p]}");
            RunList(c, 0);
        }

        protected override void ListDone(NativeActivityContext c) => StartPass(c, pass.Get(c) + 1);
    }

    /// An exclusive gateway: Then when Condition holds, else Else (nest a Reparo If in Else for "else if").
    [ContentProperty("Then")]
    [DisplayName("Reparo If")]
    public sealed class ReparoIf : ReparoSequenceBlock
    {
        [RequiredArgument] public InArgument<bool> Condition { get; set; }
        public Collection<Activity> Then { get; } = new();
        public Collection<Activity> Else { get; } = new();

        internal override IEnumerable<Activity> BridgeChildren() => Then.Concat(Else);
        protected override IList<Activity> ListAt(int which) => which == 0 ? Then : Else;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.SetChildrenCollection(new Collection<Activity>(Then.Concat(Else).ToList()));
            RequireBridgeBlocks(metadata, "Reparo If", Then.Concat(Else), typeof(ReparoActivity), typeof(ReparoForEach), typeof(ReparoIf));
        }

        protected override void Execute(NativeActivityContext c)
        {
            var run = Run(c);
            RestoreIfPending(c, run);
            int picked = Condition.Get(c) ? 0 : 1;
            int arm = picked;
            if (run.Target != null)
            {
                // Reparo's decision wins: enter the arm that holds the target, whatever the condition says.
                arm = Then.OfType<ReparoNode>().Any(n => n.Holds(run, run.Target.EventId)) ? 0 : 1;
                if (arm != picked) Trace.W($"  warning: the Continuation enters {(arm == 0 ? "Then" : "Else")} of '{DisplayName}', but its condition picks {(picked == 0 ? "Then" : "Else")}");
            }
            Trace.W($"If '{DisplayName}': {(arm == 0 ? "Then" : "Else")}{(run.Target != null ? " (entering for the Continuation)" : "")}");
            RunList(c, arm);
        }
    }
}
