// PROTOTYPE for ReparoHome#66: what is on screen at each step, and what a TrackingParticipant can do before a card's
// fault handler runs.
using System;
using System.Activities.Tracking;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace Reparo.UiPathBridge
{
    public static class Trace
    {
        public static string PathOut = @"C:\Users\reparo\UiPath\reparo\proto48\trace.log";
        public static void W(string s)
        {
            try { File.AppendAllText(PathOut, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine); } catch { }
        }
    }

    public static class Screen
    {
        /// The target apps: SpendApp, and the browsers a Use Browser card may open.
        public static string Describe()
        {
            var parts = new[] { "SpendApp", "msedge", "chrome", "firefox" }.SelectMany(n => Process.GetProcessesByName(n))
                .Where(p => p.ProcessName == "SpendApp" || p.MainWindowHandle != IntPtr.Zero)
                .Select(p => $"{p.ProcessName}[{p.Id}] '{Short(p.MainWindowTitle)}'").ToList();
            return parts.Count == 0 ? "NO TARGET APP" : string.Join("; ", parts);
        }

        static string Short(string s) => s.Length > 50 ? s.Substring(0, 50) + "..." : s;

        static int n;
        /// A screenshot of the primary monitor, as Reparo's first `screenshot` would get; returns the file name.
        public static string Shot(string label)
        {
            try
            {
                var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using var bmp = new Bitmap(b.Width, b.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                var name = $"shot-{Interlocked.Increment(ref n)}-{label}.png";
                bmp.Save(Path.Combine(Path.GetDirectoryName(Trace.PathOut), name), ImageFormat.Png);
                return name;
            }
            catch (Exception e) { return "shot failed: " + e.Message; }
        }
    }

    /// Logs fault records and state changes, with what is on screen; optionally holds the workflow thread on the
    /// first fault record, to see whether a card's fault handler waits for it.
    public sealed class ProbeTracker : TrackingParticipant
    {
        public static int HoldMs;
        public static bool Held;

        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {
            if (record is ActivityStateRecord a)
            {
                if (a.State != "Faulted" && !(a.Activity.TypeName.EndsWith("NApplicationCard") && a.State == "Closed")) return;
                Trace.W($"TRACK state {a.State} {a.Activity.TypeName.Split('.').Last()} '{a.Activity.Name}' (thread {Environment.CurrentManagedThreadId}); UI {Screen.Describe()}");
            }
            else if (record is FaultPropagationRecord f)
            {
                Trace.W($"TRACK fault from '{f.FaultSource?.Name}' to handler '{f.FaultHandler?.Name}' src={f.IsFaultSource} (thread {Environment.CurrentManagedThreadId}); UI {Screen.Describe()}");
                if (HoldMs > 0 && !Held)
                {
                    Held = true;
                    Trace.W($"TRACK holding the workflow thread {HoldMs} ms; screenshot {Screen.Shot("in-track")}");
                    Thread.Sleep(HoldMs);
                    Trace.W($"TRACK hold over; UI {Screen.Describe()}");
                }
            }
        }
    }
}
