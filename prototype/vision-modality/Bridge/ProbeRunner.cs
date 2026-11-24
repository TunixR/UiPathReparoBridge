// PROTOTYPE for ReparoHome#49: throwaway, never merged.
// Plays a script of wire-shaped Recovery requests (screenshot, action) against the vision Modality and logs everything.
// A box written "@name" aims at the centre of TargetApp's control `name`, read from its rects.json (physical screen
// pixels) and turned into screenshot pixels, the way Reparo would read it off the screenshot.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Reparo.UiPathBridge.Vision;
using static Reparo.UiPathBridge.Vision.Native;

namespace Reparo.UiPathBridge
{
    public static class ProbeRunner
    {
        public static void Run(string scriptPath, string outDir, string host)
        {
            Directory.CreateDirectory(outDir);
            using var log = new StreamWriter(Path.Combine(outDir, "probe.log"), append: true) { AutoFlush = true };
            void L(string s) => log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}");
            L($"=== probe {Path.GetFileName(scriptPath)} in {host}, pid {Environment.ProcessId}, {RuntimeInformation.FrameworkDescription}");
            var settings = new VisionSettings();
            VisionSession session = null;
            string rects = Path.Combine(outDir, "..", "..", "rects.json"); // proto49\\rects.json, written by TargetApp
            int shots = 0;
            foreach (var step in JsonDocument.Parse(File.ReadAllText(scriptPath)).RootElement.EnumerateArray())
            {
                string what = step.GetProperty("do").GetString();
                try
                {
                    switch (what)
                    {
                        case "info": Info(L); break;
                        case "settings":
                            if (step.TryGetProperty("type_method", out var tm)) settings.TypeMethod = Enum.Parse<TypeMethod>(tm.GetString(), true);
                            if (step.TryGetProperty("check_integrity", out var ci)) settings.CheckIntegrity = ci.GetBoolean();
                            if (step.TryGetProperty("scroll_delta", out var sd)) settings.ScrollDelta = sd.GetInt32();
                            L($"settings: type {settings.TypeMethod}, integrity check {settings.CheckIntegrity}, scroll {settings.ScrollDelta}");
                            break;
                        case "session": session = new VisionSession(settings) { Log = s => L("  " + s) }; L($"session on {session.Current()}"); break;
                        case "observe":
                            {
                                var sw = Stopwatch.StartNew();
                                var jpg = session.Observe();
                                string name = $"{++shots:00}-{(step.TryGetProperty("save", out var sv) ? sv.GetString() : "shot")}.jpg";
                                File.WriteAllBytes(Path.Combine(outDir, name), jpg);
                                using var img = System.Drawing.Image.FromStream(new MemoryStream(jpg));
                                L($"observe: {name} {img.Width}x{img.Height}, {jpg.Length} bytes, {sw.ElapsedMilliseconds} ms");
                                break;
                            }
                        case "act":
                            {
                                string json = Resolve(step.GetProperty("action").GetRawText(), rects, session, L);
                                var reply = session.Act(JsonDocument.Parse(json).RootElement);
                                L($"act {json} -> {JsonSerializer.Serialize(reply)}");
                                break;
                            }
                        case "release": session.ReleaseHeld(); L("released held keys"); break;
                        case "sleep": System.Threading.Thread.Sleep(step.GetProperty("ms").GetInt32()); break;
                        case "note": L("# " + step.GetProperty("text").GetString()); break;
                        default: L($"unknown step {what}"); break;
                    }
                }
                catch (SessionLost e) { L($"{what}: SESSION LOST: {e.Message} (an Infrastructure failure)"); }
                catch (Exception e) { L($"{what}: EXCEPTION {e.GetType().Name}: {e.Message}"); }
            }
            L("=== done");
        }

        /// "@name" in a box -> the centre of that control in the last screenshot's pixels.
        static string Resolve(string json, string rectsPath, VisionSession s, Action<string> L)
        {
            if (!json.Contains("\"@")) return json;
            var rects = JsonDocument.Parse(File.ReadAllText(rectsPath)).RootElement;
            var scr = s.Current();
            // "@name" -> [x, y] in screenshot pixels; "@name%" -> the same point as fractions of the screenshot;
            // "@name#" -> the control's whole box [x1, y1, x2, y2], as a GUI agent's bounding box.
            return System.Text.RegularExpressions.Regex.Replace(json, "\"@([A-Za-z0-9_]+)([%#]?)\"", m =>
            {
                var r = rects.GetProperty(m.Groups[1].Value).EnumerateArray().Select(e => e.GetInt32()).ToArray();
                int x = (r[0] + r[2]) / 2 - scr.Rect.Left, y = (r[1] + r[3]) / 2 - scr.Rect.Top;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                return m.Groups[2].Value switch
                {
                    "%" => string.Format(inv, "[{0:0.0000}, {1:0.0000}]", (x + 0.5) / scr.Width, (y + 0.5) / scr.Height),
                    "#" => $"[{r[0] - scr.Rect.Left}, {r[1] - scr.Rect.Top}, {r[2] - scr.Rect.Left}, {r[3] - scr.Rect.Top}]",
                    _ => $"[{x}, {y}]",
                };
            });
        }

        static void Info(Action<string> L)
        {
            IntPtr thr = GetThreadDpiAwarenessContext();
            IntPtr proc = GetDpiAwarenessContextForProcess(IntPtr.Zero);
            string Aw(IntPtr c) => GetAwarenessFromDpiAwarenessContext(c) switch { 0 => "unaware", 1 => "system-aware", 2 => "per-monitor", _ => "?" } + $" (dpi {GetDpiFromDpiAwarenessContext(c)})";
            L($"info: process DPI awareness {Aw(proc)}, this thread {Aw(thr)}");
            string Metrics() => $"virtual screen ({GetSystemMetrics(SM_XVIRTUALSCREEN)},{GetSystemMetrics(SM_YVIRTUALSCREEN)}) {GetSystemMetrics(SM_CXVIRTUALSCREEN)}x{GetSystemMetrics(SM_CYVIRTUALSCREEN)}, {GetSystemMetrics(SM_CMONITORS)} monitor(s)";
            L($"info: as the thread is: {Metrics()}");
            IntPtr old = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            L($"info: pinned per-monitor v2 (set returned {(old == IntPtr.Zero ? "FAILED" : "ok")}): {Metrics()}");
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() }; GetMonitorInfo(h, ref mi);
                GetDpiForMonitor(h, 0, out uint dx, out _);
                L($"info: monitor {mi.szDevice} {mi.rcMonitor} primary={(mi.dwFlags & 1) == 1} dpi {dx} ({dx * 100 / 96}%)"); return true;
            }, IntPtr.Zero);
            IntPtr fg = GetForegroundWindow(); GetWindowRect(fg, out var wr);
            L($"info: foreground '{VisionSession.Title(fg)}' {wr}, window dpi {GetDpiForWindow(fg)}, layout 0x{GetKeyboardLayout(GetWindowThreadProcessId(fg, out _)).ToInt64():X}");
            SetThreadDpiAwarenessContext(old);
            ProcessIdToSessionId((uint)Environment.ProcessId, out int sid);
            L($"info: session {sid}, user {Environment.UserName}, elevated {new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)}");
        }
    }
}
