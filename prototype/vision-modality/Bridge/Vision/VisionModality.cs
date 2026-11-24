// PROTOTYPE for ReparoHome#49: throwaway, never merged.
// The UiPath Bridge's vision Modality: screenshots of one monitor in physical pixels, Recovery actions as SendInput.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using static Reparo.UiPathBridge.Vision.Native;

namespace Reparo.UiPathBridge.Vision
{
    public enum TypeMethod { Keys, Unicode, Paste }

    public sealed class VisionSettings
    {
        public TypeMethod TypeMethod = TypeMethod.Keys;
        public int ScrollDelta = 360;        // wheel units per `scroll` (120 = one notch)
        public int DragSteps = 20;
        public int JpegQuality = 85;
        public bool DrawPointer = true;      // as the Reference client does
        public bool CheckIntegrity = true;   // fail an action aimed at a window above the Robot's integrity level
    }

    /// The monitor a Recovery's screenshots show, chosen when the Recovery starts.
    public sealed class Screen
    {
        public string Device; public RECT Rect; public uint Dpi;
        public int Width => Rect.Right - Rect.Left; public int Height => Rect.Bottom - Rect.Top;
        public override string ToString() => $"{Device} {Rect} {Width}x{Height} dpi {Dpi}";
    }

    /// Runs `body` with the calling thread pinned to per-monitor-aware v2, so every rectangle, point and capture is in
    /// physical pixels whatever the host process (the UiPath Robot Executor) declares.
    internal readonly struct PhysicalPixels : IDisposable
    {
        readonly IntPtr _old;
        public PhysicalPixels(bool _) { _old = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        public void Dispose() { if (_old != IntPtr.Zero) SetThreadDpiAwarenessContext(_old); }
    }

    public sealed class VisionSession
    {
        readonly VisionSettings _s;
        readonly string _device;
        Screen _shot;                       // the monitor as the last screenshot saw it
        readonly HashSet<ushort> _held = new HashSet<ushort>(); // keys Reparo pressed with keydown and hasn't released
        public Action<string> Log = _ => { };

        public VisionSession(VisionSettings settings)
        {
            _s = settings;
            using (new PhysicalPixels(true))
            {
                IntPtr fg = GetForegroundWindow();
                IntPtr mon = fg != IntPtr.Zero ? MonitorFromWindow(fg, 1 /*DEFAULTTOPRIMARY*/) : MonitorFromPoint(default, 1);
                _device = Describe(mon).Device;
                Log($"session: foreground '{Title(fg)}' on {Describe(mon)}");
            }
        }

        public Screen Current()
        {
            using (new PhysicalPixels(true))
            {
                Screen found = null;
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
                {
                    var m = Describe(h); if (m.Device == _device) found = m; return true;
                }, IntPtr.Zero);
                return found ?? throw new InvalidOperationException($"the Recovery's monitor {_device} is gone");
            }
        }

        static Screen Describe(IntPtr mon)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(mon, ref mi);
            GetDpiForMonitor(mon, 0, out uint dx, out _);
            return new Screen { Device = mi.szDevice, Rect = mi.rcMonitor, Dpi = dx };
        }

        // ------------------------------------------------------------------ Observation

        /// A JPEG of the Recovery's monitor, physical pixels, pointer drawn in. Its pixels are the coordinates Reparo sends.
        public byte[] Observe()
        {
            SessionCheck();
            using (new PhysicalPixels(true))
            {
                var scr = Current();
                using var bmp = new Bitmap(scr.Width, scr.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    // BitBlt with CAPTUREBLT (layered windows too); .NET's CopyFromScreen refuses that flag combination.
                    IntPtr dst = g.GetHdc(), src = GetDC(IntPtr.Zero);
                    try
                    {
                        if (!BitBlt(dst, 0, 0, scr.Width, scr.Height, src, scr.Rect.Left, scr.Rect.Top, 0x00CC0020 | 0x40000000))
                            throw new SessionLost($"BitBlt failed (error {Marshal.GetLastWin32Error()}): nothing to capture");
                    }
                    finally { ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst); }
                    if (_s.DrawPointer) DrawPointer(g, scr);
                }
                _shot = scr;
                using var ms = new MemoryStream();
                var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var ps = new EncoderParameters(1); ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)_s.JpegQuality);
                bmp.Save(ms, enc, ps);
                return ms.ToArray();
            }
        }

        static void DrawPointer(Graphics g, Screen scr)
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref ci) || (ci.flags & 1) == 0) return;
            GetIconInfo(ci.hCursor, out var ii);
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            IntPtr hdc = g.GetHdc();
            try { DrawIconEx(hdc, ci.ptScreenPos.X - scr.Rect.Left - ii.xHotspot, ci.ptScreenPos.Y - scr.Rect.Top - ii.yHotspot, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3); }
            finally { g.ReleaseHdc(hdc); }
        }

        /// The session can show and take input: the input desktop is the user's Default desktop (not Winlogon: locked,
        /// UAC, Ctrl+Alt+Del) and the session is connected. Otherwise it's an Infrastructure failure.
        public void SessionCheck()
        {
            ProcessIdToSessionId((uint)Environment.ProcessId, out int sid);
            if (WTSQuerySessionInformation(IntPtr.Zero, sid, 8 /*WTSConnectState*/, out IntPtr buf, out _))
            {
                int state = Marshal.ReadInt32(buf); WTSFreeMemory(buf);
                if (state != 0 /*WTSActive*/) throw new SessionLost($"session {sid} isn't active (WTS state {state})");
            }
            IntPtr d = OpenInputDesktop(0, false, 0x0001 /*DESKTOP_READOBJECTS*/);
            if (d == IntPtr.Zero) throw new SessionLost($"the input desktop can't be opened (error {Marshal.GetLastWin32Error()}): locked, UAC or Ctrl+Alt+Del");
            try
            {
                var sb = new StringBuilder(64);
                GetUserObjectInformation(d, 2 /*UOI_NAME*/, sb, 128, out _);
                if (!string.Equals(sb.ToString(), "Default", StringComparison.OrdinalIgnoreCase))
                    throw new SessionLost($"the input desktop is '{sb}', not Default");
            }
            finally { CloseDesktop(d); }
        }

        // ------------------------------------------------------------------ Recovery actions

        /// One Recovery action -> {success, action_type, error?}. Never throws for a bad action; a lost session throws SessionLost.
        public Dictionary<string, object> Act(JsonElement action)
        {
            string type = action.TryGetProperty("action_type", out var t) ? t.GetString() : "unknown";
            try
            {
                SessionCheck();
                var inputs = action.TryGetProperty("action_inputs", out var i) ? i : default;
                using (new PhysicalPixels(true)) Perform(Alias(type), inputs);
                return new Dictionary<string, object> { ["success"] = true, ["action_type"] = type };
            }
            catch (SessionLost) { throw; }
            catch (Exception e) { return new Dictionary<string, object> { ["success"] = false, ["action_type"] = type, ["error"] = e.Message }; }
        }

        static string Alias(string t) => t switch { "click" => "left_single", "select" => "drag", "press" => "keydown", "release" => "keyup", _ => t };

        void Perform(string type, JsonElement inputs)
        {
            switch (type)
            {
                case "left_single": Click(Point(inputs, "start_box"), MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 1); break;
                case "left_double": Click(Point(inputs, "start_box"), MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 2); break;
                case "right_single": Click(Point(inputs, "start_box"), MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 1); break;
                case "hover": { var p = Point(inputs, "start_box"); Guard(p); Move(p); break; }
                case "drag":
                    {
                        var a = Point(inputs, "start_box"); var b = Point(inputs, "end_box"); Guard(a);
                        Move(a); Thread.Sleep(50); Mouse(MOUSEEVENTF_LEFTDOWN);
                        for (int k = 1; k <= _s.DragSteps; k++) { Move(new POINT { X = a.X + (b.X - a.X) * k / _s.DragSteps, Y = a.Y + (b.Y - a.Y) * k / _s.DragSteps }); Thread.Sleep(10); }
                        Mouse(MOUSEEVENTF_LEFTUP); break;
                    }
                case "scroll":
                    {
                        string dir = Str(inputs, "direction").ToLowerInvariant();
                        var p = Point(inputs, "start_box"); Guard(p); Move(p); Thread.Sleep(30);
                        (uint flag, int sign) = dir switch
                        {
                            "down" => (MOUSEEVENTF_WHEEL, -1), "up" => (MOUSEEVENTF_WHEEL, 1),
                            "right" => (MOUSEEVENTF_HWHEEL, 1), "left" => (MOUSEEVENTF_HWHEEL, -1),
                            _ => throw new ArgumentException($"Invalid scroll direction: {dir}. Must be 'up', 'down', 'left', or 'right'."),
                        };
                        for (int left = _s.ScrollDelta; left > 0; left -= 120) { Mouse(flag, sign * Math.Min(120, left)); Thread.Sleep(20); }
                        break;
                    }
                case "type": Type(Str(inputs, "content")); break;
                case "hotkey":
                    {
                        GuardForeground();
                        var hkl = ForegroundLayout();
                        var keys = Keys.SplitHotkey(KeyInput(inputs)).Select(n => Keys.Resolve(n, hkl)).ToList();
                        if (keys.Count == 0) throw new ArgumentException("hotkey requires one or more keys");
                        foreach (var k in keys) KeyEvent(k, false);
                        foreach (var k in Enumerable.Reverse(keys)) KeyEvent(k, true);
                        break;
                    }
                case "keydown": { GuardForeground(); var k = Keys.Resolve(SingleKey(inputs), ForegroundLayout()); KeyEvent(k, false); _held.Add(k.Vk); break; }
                case "keyup": { GuardForeground(); var k = Keys.Resolve(SingleKey(inputs), ForegroundLayout()); KeyEvent(k, true); _held.Remove(k.Vk); break; }
                case "wait": Thread.Sleep(TimeSpan.FromSeconds(Seconds(inputs))); break;
                default: throw new ArgumentException($"Unsupported action_type: {type}");
            }
        }

        /// Releases every key Reparo left down, when the Recovery ends, so the Continuation doesn't run with Shift held.
        public void ReleaseHeld() { foreach (var vk in _held.ToList()) KeyEvent(new Key(vk, vk is 0xA3 or 0xA5 or 0x5B or 0x5C), true); _held.Clear(); }

        // ---- Coordinates

        /// The physical screen point a `[x, y]` or `[x1, y1, x2, y2]` box aims at: the box's centre in the last
        /// screenshot's pixels, plus the monitor's origin. Values all <= 1.0 on an axis are a fraction of the screenshot.
        public POINT Point(JsonElement inputs, string field)
        {
            if (_shot == null) throw new InvalidOperationException("no screenshot yet: coordinates have no space");
            var now = Current();
            if (now.Rect.Left != _shot.Rect.Left || now.Rect.Top != _shot.Rect.Top || now.Width != _shot.Width || now.Height != _shot.Height)
                throw new InvalidOperationException($"the screen changed since the screenshot ({_shot.Rect} -> {now.Rect})");
            if (inputs.ValueKind != JsonValueKind.Object || !inputs.TryGetProperty(field, out var box) || box.ValueKind != JsonValueKind.Array)
                throw new ArgumentException($"{field} must be a list");
            var v = box.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            double x1, y1, x2, y2;
            if (v.Length == 2) { x1 = x2 = v[0]; y1 = y2 = v[1]; }
            else if (v.Length == 4) { x1 = v[0]; y1 = v[1]; x2 = v[2]; y2 = v[3]; }
            else throw new ArgumentException($"{field} must have length 2 or 4");
            double cx = Axis(x1, x2, _shot.Width), cy = Axis(y1, y2, _shot.Height);
            if (cx < 0 || cy < 0 || cx >= _shot.Width || cy >= _shot.Height) throw new ArgumentException($"{field} ({cx},{cy}) is outside the screenshot {_shot.Width}x{_shot.Height}");
            return new POINT { X = _shot.Rect.Left + (int)cx, Y = _shot.Rect.Top + (int)cy };
        }

        static double Axis(double lo, double hi, int extent) { double c = (lo + hi) / 2; return (lo > 1.0 || hi > 1.0) ? c : c * extent; }

        // ---- Integrity (UIPI): input to a higher-integrity window is dropped without a trace, so refuse it up front.

        void Guard(POINT p)
        {
            if (!_s.CheckIntegrity) return;
            IntPtr w = GetAncestor(WindowFromPoint(p), 2 /*GA_ROOT*/);
            CheckWindow(w, $"at ({p.X},{p.Y})");
        }

        void GuardForeground() { if (_s.CheckIntegrity) CheckWindow(GetForegroundWindow(), "in the foreground"); }

        void CheckWindow(IntPtr w, string where)
        {
            if (w == IntPtr.Zero) return;
            GetWindowThreadProcessId(w, out uint pid);
            int mine = IntegrityOf(GetCurrentProcess(), false), theirs;
            IntPtr h = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) theirs = 0x4000; // can't even query it: a system or protected process
            else { theirs = IntegrityOf(h, true); CloseHandle(h); }
            Log($"integrity: window '{Title(w)}' {where} pid {pid} level 0x{theirs:X} vs Robot 0x{mine:X}");
            if (theirs > mine) throw new InvalidOperationException($"the target window '{Title(w)}' runs elevated (integrity 0x{theirs:X} above the Robot's 0x{mine:X}); Windows would drop the input");
        }

        static int IntegrityOf(IntPtr process, bool other)
        {
            if (!OpenProcessToken(process, 0x0008 /*TOKEN_QUERY*/, out IntPtr tok)) return 0x4000;
            try
            {
                GetTokenInformation(tok, 25 /*TokenIntegrityLevel*/, IntPtr.Zero, 0, out int len);
                IntPtr buf = Marshal.AllocHGlobal(len);
                try
                {
                    if (!GetTokenInformation(tok, 25, buf, len, out _)) return 0x4000;
                    IntPtr sid = Marshal.ReadIntPtr(buf);
                    int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { CloseHandle(tok); }
        }

        // ---- Mouse

        void Click(POINT p, uint down, uint up, int times)
        {
            Guard(p); Move(p); Thread.Sleep(30);
            for (int n = 0; n < times; n++) { Mouse(down); Mouse(up); }
        }

        void Move(POINT p)
        {
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            // The centre of pixel (x, y) on a 0..65535 scale across the whole virtual desktop.
            int nx = (int)(((p.X - vx) * 2L + 1) * 65536 / (2L * vw)), ny = (int)(((p.Y - vy) * 2L + 1) * 65536 / (2L * vh));
            Send(new INPUT { type = INPUT_MOUSE, u = { mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } } });
            GetCursorPos(out var at);
            if (at.X != p.X || at.Y != p.Y) Log($"move: aimed ({p.X},{p.Y}) cursor at ({at.X},{at.Y})");
        }

        void Mouse(uint flags, int data = 0) => Send(new INPUT { type = INPUT_MOUSE, u = { mi = new MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)data) } } });

        // ---- Keyboard

        void KeyEvent(Key k, bool up)
        {
            uint scan = MapVirtualKeyEx(k.Vk, 0 /*VK_TO_VSC*/, ForegroundLayout());
            uint flags = (up ? KEYEVENTF_KEYUP : 0) | (k.Extended ? KEYEVENTF_EXTENDEDKEY : 0);
            Send(new INPUT { type = INPUT_KEYBOARD, u = { ki = new KEYBDINPUT { wVk = k.Vk, wScan = (ushort)scan, dwFlags = flags } } });
        }

        void Scan(ushort scan, bool extended, bool up) =>
            Send(new INPUT { type = INPUT_KEYBOARD, u = { ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0) } } });

        void Packet(char c, bool up) =>
            Send(new INPUT { type = INPUT_KEYBOARD, u = { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } } });

        static IntPtr ForegroundLayout() => GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));

        /// `type`: the text exactly, whatever Reparo holds down. "\n" is Enter, "\t" is Tab.
        void Type(string text)
        {
            GuardForeground();
            var held = _held.ToList();
            foreach (var vk in held) KeyEvent(new Key(vk), true); // a held Shift would change scan-code letters
            try
            {
                if (_s.TypeMethod == TypeMethod.Paste) { Paste(text); return; }
                var hkl = ForegroundLayout();
                int viaKeys = 0, viaPacket = 0;
                foreach (char c in text)
                {
                    if (c == '\r') continue;
                    if (c == '\n' || c == '\t') { var k = new Key((ushort)(c == '\n' ? 0x0D : 0x09)); KeyEvent(k, false); KeyEvent(k, true); continue; }
                    if (_s.TypeMethod == TypeMethod.Keys && TypeByScanCode(c, hkl)) viaKeys++;
                    else { Packet(c, false); Packet(c, true); viaPacket++; }
                    Thread.Sleep(5);
                }
                Log($"type: {viaKeys} chars as scan codes, {viaPacket} as VK_PACKET (layout 0x{hkl.ToInt64():X})");
            }
            finally { foreach (var vk in held) KeyEvent(new Key(vk), false); }
        }

        /// A character the layout makes with one key plus Shift and/or AltGr, sent as real scan codes. Dead-key
        /// characters (é on Spanish), IME text and characters off the layout return false and go as VK_PACKET.
        bool TypeByScanCode(char c, IntPtr hkl)
        {
            short r = VkKeyScanEx(c, hkl);
            if (r == -1) return false;
            int vk = r & 0xFF, mods = (r >> 8) & 0xFF;
            bool shift = (mods & 1) != 0, ctrl = (mods & 2) != 0, alt = (mods & 4) != 0;
            if ((mods & ~7) != 0 || ctrl != alt) return false;  // Hankaku etc., or Ctrl-only control characters
            ushort scan = (ushort)MapVirtualKeyEx((uint)vk, 0, hkl);
            if (scan == 0) return false;
            if (ctrl) { Scan(0x1D, false, false); Scan(0x38, true, false); } // AltGr = LCtrl + RAlt, as the keyboard driver sends it
            if (shift) Scan(0x2A, false, false);
            Scan(scan, false, false); Scan(scan, false, true);
            if (shift) Scan(0x2A, false, true);
            if (ctrl) { Scan(0x38, true, true); Scan(0x1D, false, true); }
            return true;
        }

        void Paste(string text)
        {
            string trailing = text.EndsWith("\n") ? "\n" : "";
            string body = trailing.Length > 0 ? text[..^1] : text;
            string saved = null; Exception err = null;
            var th = new Thread(() =>
            {
                try { if (System.Windows.Forms.Clipboard.ContainsText()) saved = System.Windows.Forms.Clipboard.GetText(); System.Windows.Forms.Clipboard.SetText(body); }
                catch (Exception e) { err = e; }
            });
            th.SetApartmentState(ApartmentState.STA); th.Start(); th.Join();
            if (err != null) throw err;
            var ctrl = new Key(0xA2); var v = new Key(0x56);
            KeyEvent(ctrl, false); KeyEvent(v, false); KeyEvent(v, true); KeyEvent(ctrl, true);
            Thread.Sleep(300);
            var back = new Thread(() => { try { if (saved != null) System.Windows.Forms.Clipboard.SetText(saved); else System.Windows.Forms.Clipboard.Clear(); } catch { } });
            back.SetApartmentState(ApartmentState.STA); back.Start(); back.Join();
            if (trailing.Length > 0) { var e = new Key(0x0D); KeyEvent(e, false); KeyEvent(e, true); }
            Log($"type: pasted {body.Length} chars, clipboard restored ({(saved == null ? "was empty" : "text")})");
        }

        // ---- Plumbing

        void Send(INPUT input)
        {
            uint n = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if (n != 1) throw new InvalidOperationException($"SendInput sent {n} of 1 (error {Marshal.GetLastWin32Error()})");
        }

        static string Str(JsonElement o, string f) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
        static string KeyInput(JsonElement o) { foreach (var f in new[] { "hotkey", "key", "press" }) { var s = Str(o, f); if (s != "") return s; } return ""; }
        static string SingleKey(JsonElement o) { var raw = KeyInput(o); var n = raw == " " ? raw : raw.Trim(); if (n == "") throw new ArgumentException("requires a key"); return n; }
        static double Seconds(JsonElement o)
        {
            foreach (var f in new[] { "seconds", "duration", "time" })
                if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(f, out var v))
                {
                    double s = v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : throw new ArgumentException("wait requires a numeric seconds/duration");
                    if (s < 0) throw new ArgumentException("wait seconds must be non-negative"); return s;
                }
            return 0;
        }
        internal static string Title(IntPtr w) { var sb = new StringBuilder(256); GetWindowText(w, sb, 256); return sb.ToString(); }
    }

    public sealed class SessionLost : Exception { public SessionLost(string m) : base(m) { } }
}
