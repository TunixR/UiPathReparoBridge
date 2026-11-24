// PROTOTYPE for ReparoHome#46: what can a package see at runtime inside a UiPath run?
using System;
using System.Activities;
using System.Activities.Tracking;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xaml;

namespace Reparo.UiPathBridge
{
    public static class Trace
    {
        public static string PathOut = @"C:\Users\reparo\UiPath\reparo\proto46\trace.log";
        public static void W(string s)
        {
            try { File.AppendAllText(PathOut, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine); } catch { }
        }
    }

    /// Counts what the executor hands a package-registered tracking participant.
    public sealed class ProbeTracker : TrackingParticipant
    {
        public int Records;
        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {
            Records++;
            if (record is ActivityStateRecord a)
                Trace.W($"TRACK state {a.State} {a.Activity.TypeName} '{a.Activity.Name}' id={a.Activity.Id} inst={a.Activity.InstanceId}");
            else if (record is FaultPropagationRecord f)
                Trace.W($"TRACK fault from '{f.FaultSource?.Name}' handler='{f.FaultHandler?.Name}' src={f.IsFaultSource} {f.Fault?.GetType().Name}");
            else
                Trace.W($"TRACK {record.GetType().Name}");
        }
    }

    public sealed class ReparoProbe : NativeActivity
    {
        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
            metadata.AddDefaultExtensionProvider(() => new ProbeTracker());
        }

        protected override void Execute(NativeActivityContext context)
        {
            Trace.W("=== probe in " + DisplayName + " runtime=" + Environment.Version + " pid=" + Environment.ProcessId + " session=" + System.Diagnostics.Process.GetCurrentProcess().SessionId);
            var tracker = context.GetExtension<ProbeTracker>();
            Trace.W("tracker extension: " + (tracker == null ? "null" : "present, records so far " + tracker.Records));
            Trace.W("ActivityInfo: Id=" + Id + " ActivityInstanceId=" + context.ActivityInstanceId);

            // 1. attached properties on this activity
            var props = AttachablePropertyServices.GetAttachedPropertyCount(this);
            Trace.W("attached props on self: " + props);
            if (props > 0)
            {
                var arr = new System.Collections.Generic.KeyValuePair<AttachableMemberIdentifier, object>[props];
                AttachablePropertyServices.CopyPropertiesTo(this, arr, 0);
                foreach (var kv in arr) Trace.W("  " + kv.Key + " = " + kv.Value);
            }

            // 2. walk to the root via the internal Parent property
            var parentProp = typeof(Activity).GetProperty("Parent", BindingFlags.Instance | BindingFlags.NonPublic);
            Activity a = this, root = this;
            int depth = 0;
            while (a != null)
            {
                Trace.W($"  up[{depth}] {a.GetType().FullName} '{a.DisplayName}' Id={a.Id} attached={AttachablePropertyServices.GetAttachedPropertyCount(a)}");
                root = a; a = parentProp?.GetValue(a) as Activity; depth++;
            }

            // 3. what the compiled root type exposes about IdRefs
            foreach (var t in new[] { root.GetType() }.Concat(root.GetType().Assembly.GetTypes().Take(50)))
            {
                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (m.Name.IndexOf("IdRef", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("MapFilePath", StringComparison.OrdinalIgnoreCase) >= 0)
                        Trace.W($"  member {t.FullName}.{m.Name} ({m.MemberType})");
            }
            Trace.W("root assembly: " + root.GetType().Assembly.FullName + " loc=" + root.GetType().Assembly.Location);
            foreach (var n in root.GetType().Assembly.GetManifestResourceNames()) Trace.W("  resource " + n);
        }
    }
}
