// PROTOTYPE for ReparoHome#46: shape 1, the Bridge's own blocks.
// Reparo Process > Reparo Activity > Reparo Event, with Reparo For Each and Reparo If between Events.
// Every UiPath activity sits inside an Event. Variables are the UiPath variables declared on Reparo Process.
// Recovery is simulated: a UI failure is "sent" to a file and a fake `done` names the Event to continue at.
using System;
using System.Activities;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows.Markup;

namespace Reparo.UiPathBridge
{
    internal sealed class Target
    {
        public string EventId;
        public List<int> Passes = new();
        public override string ToString() => EventId + Suffix(Passes);
        public static string Suffix(IEnumerable<int> p) => p.Any() ? " " + string.Concat(p.Select(i => "#" + i)) : "";
    }

    /// Shared by every block of one run, as an execution property.
    internal sealed class RunState
    {
        public const string Key = "Reparo.Run";
        public string File;
        public Dictionary<object, string> IdRefs = new();
        public HashSet<string> VarNames = new();
        public Target Target;                              // set while a Continuation is still entering
        public Dictionary<string, JsonElement> Restore;     // Variables to write on the way in
        public List<int> Passes = new();                   // current pass of each enclosing loop, outermost first
        public List<string> UiLog = new();
        public ReparoEvent CurrentEvent;
        public List<int> CurrentPasses;
        public Dictionary<string, object> VarsAtEventStart;
        public bool ContinuationPending;
        public int Recoveries;

        public string IdOf(Activity a) => File + "#" + (IdRefs.TryGetValue(a, out var r) ? r : "pos" + a.Id);
    }

    public abstract class ReparoNode : NativeActivity
    {
        internal static RunState Run(NativeActivityContext c) => (RunState)c.Properties.Find(RunState.Key);

        internal static PropertyDescriptor Prop(NativeActivityContext c, string name) =>
            c.DataContext.GetProperties().Find(name, false)
            ?? throw new InvalidOperationException("Reparo: no Variable named '" + name + "' in scope");

        internal static Dictionary<string, object> Snapshot(NativeActivityContext c, RunState run)
        {
            var d = new Dictionary<string, object>();
            foreach (PropertyDescriptor p in c.DataContext.GetProperties())
                if (run.VarNames.Contains(p.Name)) d[p.Name] = p.GetValue(c.DataContext);
            return d;
        }

        /// The first block a Continuation enters writes the Variables Reparo handed back.
        internal static void RestoreIfPending(NativeActivityContext c, RunState run)
        {
            if (run.Restore == null) return;
            foreach (var kv in run.Restore)
            {
                var p = Prop(c, kv.Key);
                p.SetValue(c.DataContext, JsonSerializer.Deserialize(kv.Value.GetRawText(), p.PropertyType));
            }
            Trace.W("  restored Variables: " + string.Join(", ", run.Restore.Keys));
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

    [ContentProperty("Nodes")]
    [DisplayName("Reparo Process")]
    public sealed class ReparoProcess : ReparoNode
    {
        public Collection<Activity> Nodes { get; } = new();
        public Collection<Variable> Variables { get; } = new();

        /// Cold start: the Event to start at (its IdRef), its loop passes ("2" or "1,0"), and Variables as JSON.
        public InArgument<string> ContinueFrom { get; set; }
        public InArgument<string> ContinuePasses { get; set; }
        public InArgument<string> ContinueVariables { get; set; }
        /// A file standing in for Reparo's `done`; when absent, the fake Reparo continues at the failed Event.
        public InArgument<string> FakeDonePath { get; set; }

        private readonly Variable<int> index = new();

        internal override IEnumerable<Activity> BridgeChildren() => Nodes;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);   // the arguments
            metadata.SetChildrenCollection(Nodes);
            metadata.SetVariablesCollection(Variables);
            metadata.AddImplementationVariable(index);
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
            foreach (var v in Variables) run.VarNames.Add(v.Name);
            c.Properties.Add(RunState.Key, run);
            Trace.W($"=== Process '{DisplayName}' id={run.IdOf(this)}; {run.IdRefs.Count} IdRefs known; Variables: {string.Join(", ", run.VarNames)}");
            Describe(run, this, "");

            var from = ContinueFrom.Get(c);
            if (!string.IsNullOrEmpty(from))
            {
                run.Target = new Target { EventId = from.Contains('#') ? from : run.File + "#" + from };
                var passes = ContinuePasses.Get(c);
                if (!string.IsNullOrEmpty(passes)) run.Target.Passes = passes.Split(',').Select(int.Parse).ToList();
                var vars = ContinueVariables.Get(c);
                if (!string.IsNullOrEmpty(vars)) run.Restore = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(vars);
                Trace.W("cold start at " + run.Target);
            }
            Start(c, run);
        }

        private void Start(NativeActivityContext c, RunState run)
        {
            int i = StartIndex(run, Nodes);
            if (i >= Nodes.Count) return;
            index.Set(c, i);
            c.ScheduleActivity(Nodes[i], OnChildDone, OnFault);
        }

        private void OnChildDone(NativeActivityContext c, ActivityInstance done)
        {
            var run = Run(c);
            if (run.ContinuationPending)
            {
                run.ContinuationPending = false;
                Trace.W("Continuation: entering at " + run.Target);
                Start(c, run);
                return;
            }
            int i = index.Get(c) + 1;
            if (i < Nodes.Count) { index.Set(c, i); c.ScheduleActivity(Nodes[i], OnChildDone, OnFault); return; }
            Trace.W("=== Process completed. UI log:");
            foreach (var l in run.UiLog) Trace.W("  " + l);
            Trace.W("Variables at the end: " + JsonSerializer.Serialize(Snapshot(c, run)));
        }

        private void OnFault(NativeActivityFaultContext c, Exception e, ActivityInstance from)
        {
            var run = Run(c);
            var ev = run.CurrentEvent;
            Trace.W($"FAULT reached Reparo Process: {e.GetType().FullName}: {e.Message.Split('\n')[0]} (in Event {(ev == null ? "none" : run.IdOf(ev))}, via {from.Activity.DisplayName})");
            if (ev == null || !IsUiFailure(e) || run.Recoveries >= 3)
            {
                Trace.W("  not a UI failure in an Event (or too many Recoveries): left to propagate");
                return;
            }
            c.HandleFault();
            run.Recoveries++;
            var eventId = run.IdOf(ev);
            var request = new
            {
                task_name = DisplayName,
                errored_act = new { model_event_id = eventId, event_name = ev.DisplayName + Target.Suffix(run.CurrentPasses), error_code = e.GetType().Name, error_description = e.Message.Split('\n')[0] },
                loop_iteration = run.CurrentPasses,
                variables = run.VarsAtEventStart,
                ui_log = run.UiLog,
            };
            var dir = Path.GetDirectoryName(Trace.PathOut);
            File.WriteAllText(Path.Combine(dir, $"recovery-{run.Recoveries}.json"), JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }));
            Trace.W($"  Recovery {run.Recoveries} sent (recovery-{run.Recoveries}.json): {eventId}{Target.Suffix(run.CurrentPasses)}");

            // The fake Reparo: a done file, or "continue at the failed Event" after a pause.
            var target = new Target { EventId = eventId, Passes = run.CurrentPasses.ToList() };
            var restore = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(run.VarsAtEventStart));
            int delay = 8;
            var donePath = FakeDonePath.Get(c);
            if (!string.IsNullOrEmpty(donePath) && File.Exists(donePath))
            {
                var done = JsonDocument.Parse(File.ReadAllText(donePath)).RootElement;
                var t = done.GetProperty("continue_from_model_event_id").GetString();
                target = new Target { EventId = t.Contains('#') ? t : run.File + "#" + t, Passes = done.GetProperty("loop_iteration").EnumerateArray().Select(x => x.GetInt32()).ToList() };
                if (done.TryGetProperty("variables", out var vs)) foreach (var p in vs.EnumerateObject()) restore[p.Name] = p.Value.Clone();
                if (done.TryGetProperty("delay_s", out var ds)) delay = ds.GetInt32();
                File.Delete(donePath);   // one use
            }
            Trace.W($"  fake Reparo: waiting {delay} s, then done: continue at {target}");
            Thread.Sleep(delay * 1000);
            run.Target = target;
            run.Restore = restore;
            run.Passes.Clear();
            run.CurrentEvent = null;
            run.ContinuationPending = true;
            c.CancelChild(from);   // the faulted branch ends; OnChildDone re-enters at the target
        }

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
            run.VarsAtEventStart = Snapshot(c, run);
            Trace.W($"  Event '{DisplayName}'{Target.Suffix(run.CurrentPasses)} {id} starts; Variables {JsonSerializer.Serialize(run.VarsAtEventStart)}");
            if (Body != null) c.ScheduleActivity(Body, OnBodyDone); else OnBodyDone(c, null);
        }

        private void OnBodyDone(NativeActivityContext c, ActivityInstance done)
        {
            if (done != null && done.State != ActivityInstanceState.Closed) return;
            var run = Run(c);
            run.UiLog.Add($"{DisplayName}{Target.Suffix(run.CurrentPasses)}  [{run.IdOf(this)}]");
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
            int arm;
            if (run.Target != null) arm = Then.OfType<ReparoNode>().Any(n => n.Holds(run, run.Target.EventId)) ? 0 : 1;
            else arm = Condition.Get(c) ? 0 : 1;
            Trace.W($"If '{DisplayName}': {(arm == 0 ? "Then" : "Else")}{(run.Target != null ? " (entering for the Continuation)" : "")}");
            RunList(c, arm);
        }
    }
}
