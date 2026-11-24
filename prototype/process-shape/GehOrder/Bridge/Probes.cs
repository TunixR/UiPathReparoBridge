// RESEARCH for ReparoHome#47: when does UiPath's Global Exception Handler run, relative to a FaultCallback?
using System;
using System.Activities;
using System.Activities.Tracking;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Markup;

namespace Reparo.GehOrder
{
    public static class Trace
    {
        public const string PathOut = @"C:\Users\reparo\UiPath\reparo\geh47\trace.log";
        public static void W(string s)
        {
            try { File.AppendAllText(PathOut, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine); } catch { }
        }
        /// Identity of an exception object, to tell one fault from another across log lines.
        public static string Id(Exception e) => e == null ? "null" : e.GetType().Name + "@" + RuntimeHelpers.GetHashCode(e).ToString("x");
    }

    /// Logs fault propagation and the state changes of the Click activities.
    public sealed class FaultTracker : TrackingParticipant
    {
        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {
            if (record is FaultPropagationRecord f)
                Trace.W($"  track: fault {Trace.Id(f.Fault)} from '{f.FaultSource?.Name}' -> handler '{f.FaultHandler?.Name ?? "(none)"}' isSource={f.IsFaultSource}");
            else if (record is ActivityStateRecord a && (a.Activity.Name.StartsWith("Click") || a.State == "Faulted" || a.State == "Canceled"))
                Trace.W($"  track: {a.State} '{a.Activity.Name}' id={a.Activity.Id}");
        }
    }

    /// Writes a line to the trace; also registers the FaultTracker for the run.
    public sealed class Mark : NativeActivity
    {
        public InArgument<string> Text { get; set; }
        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.AddDefaultExtensionProvider(() => new FaultTracker());
        }
        protected override void Execute(NativeActivityContext context) => Trace.W(Text.Get(context));
    }

    /// A container that schedules its Body with a FaultCallback, like the prototype's Reparo Process.
    /// Handle=true: the callback calls HandleFault (Reparo would start a Recovery). false: it lets the fault propagate.
    [ContentProperty("Body")]
    public sealed class FaultScope : NativeActivity
    {
        public Activity Body { get; set; }
        public bool Handle { get; set; } = true;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            metadata.AddChild(Body);
            metadata.AddDefaultExtensionProvider(() => new FaultTracker());
        }

        protected override void Execute(NativeActivityContext context)
        {
            Trace.W($"scope '{DisplayName}': schedules '{Body.DisplayName}' with a FaultCallback (Handle={Handle})");
            context.ScheduleActivity(Body, OnDone, OnFault);
        }

        private void OnDone(NativeActivityContext context, ActivityInstance done) =>
            Trace.W($"scope '{DisplayName}': body ended, state {done.State}");

        private void OnFault(NativeActivityFaultContext context, Exception e, ActivityInstance from)
        {
            Trace.W($"FAULTCALLBACK of '{DisplayName}' gets {Trace.Id(e)} propagated from '{from.Activity.DisplayName}': {e.Message.Split('\n')[0]} -> {(Handle ? "HandleFault" : "not handled, propagates")}");
            if (Handle) context.HandleFault();
        }
    }
}
