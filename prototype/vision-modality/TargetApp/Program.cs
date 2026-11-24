// PROTOTYPE for ReparoHome#49: throwaway. TargetApp [x y] [--tag name]
// Logs every click (with its offset from the control's centre), key, character, wheel and drag to app.log, and writes
// each control's physical screen rectangle to rects.json so the probe can aim at it.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;

static class Program
{
    static readonly string Dir = AppContext.GetData("REPARO_DIR") as string ?? @"C:\Users\reparo\UiPath\reparo\proto49";
    static readonly Dictionary<string, Control> Named = new();
    static Form Win;
    static string Tag = "app";

    // A lenient encoder: a lone surrogate (half an emoji, between its two WM_CHARs) must not throw inside WndProc.
    static readonly System.Text.Encoding Utf8 = new System.Text.UTF8Encoding(false, false);
    public static void Log(string s) => File.AppendAllText(Path.Combine(Dir, "app.log"), $"{DateTime.Now:HH:mm:ss.fff} [{Tag}] {s}\r\n", Utf8);

    [STAThread]
    static void Main(string[] args)
    {
        int ti = Array.IndexOf(args, "--tag"); if (ti >= 0) Tag = args[ti + 1];
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Win = new Form { Text = "Reparo vision target", StartPosition = FormStartPosition.Manual, Size = new Size(900, 640), Font = new Font("Segoe UI", 10) };
        if (args.Length >= 2 && int.TryParse(args[0], out int x) && int.TryParse(args[1], out int y)) Win.Location = new Point(x, y); else Win.Location = new Point(120, 90);
        bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Win.Text += elevated ? " (ELEVATED)" : "";

        var click = Add(new Button { Text = "Click me", Bounds = new Rectangle(20, 20, 160, 44) }, "click");
        click.MouseDown += (s, e) => LogMouse("click", e);
        click.Click += (s, e) => Log("click: Click");
        var dbl = Add(new Label { Text = "Double-click me", BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(200, 20, 160, 44) }, "double");
        dbl.DoubleClick += (s, e) => Log("double: DoubleClick");
        var right = Add(new Label { Text = "Right-click me", BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(380, 20, 160, 44) }, "right");
        right.MouseUp += (s, e) => Log($"right: MouseUp {e.Button}");
        var hover = Add(new Label { Text = "Hover me", BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(560, 20, 160, 44) }, "hover");
        hover.MouseEnter += (s, e) => { hover.BackColor = Color.Gold; Log($"hover: MouseEnter at {Cursor.Position}"); };
        hover.MouseLeave += (s, e) => hover.BackColor = SystemColors.Control;
        var tiny = Add(new Button { Text = "", Bounds = new Rectangle(740, 30, 8, 8), BackColor = Color.Red }, "tiny");
        tiny.MouseDown += (s, e) => LogMouse("tiny", e);

        var text = Add(new KeyBox { Multiline = true, Bounds = new Rectangle(20, 90, 520, 150), ScrollBars = ScrollBars.Vertical }, "text");
        var list = Add(new ListBox { Bounds = new Rectangle(560, 90, 300, 240) }, "list");
        for (int i = 0; i < 300; i++) list.Items.Add($"row {i}");
        list.MouseWheel += (s, e) => Log($"list: MouseWheel delta {e.Delta}, top row now {list.TopIndex}");
        int lastTop = 0; var poll = new Timer { Interval = 200 }; poll.Tick += (s, e) => { if (list.TopIndex != lastTop) { lastTop = list.TopIndex; Log($"list: top row {lastTop}"); } }; poll.Start();
        var hpanel = Add(new HWheelPanel { Bounds = new Rectangle(560, 350, 300, 80), AutoScroll = true, BorderStyle = BorderStyle.FixedSingle }, "hscroll");
        hpanel.Controls.Add(new Label { Text = string.Join("  ", Enumerable.Range(0, 60).Select(i => $"col{i}")), AutoSize = true, Location = new Point(0, 10) });
        hpanel.Scroll += (s, e) => Log($"hscroll: {e.ScrollOrientation} {e.NewValue}");
        hpanel.MouseWheel += (s, e) => Log($"hscroll: wheel {e.Delta}");

        var drag = Add(new Panel { Bounds = new Rectangle(20, 260, 520, 170), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.WhiteSmoke }, "drag");
        // The drag's ends are drawn, not controls, so the panel itself gets the MouseDown and the capture.
        drag.Paint += (s, e) => { e.Graphics.DrawString("drag from here", Win.Font, Brushes.Black, 10, 10); e.Graphics.DrawString("drop here", Win.Font, Brushes.Black, 400, 140); };
        Point? down = null; int moves = 0;
        drag.MouseDown += (s, e) => { down = Cursor.Position; moves = 0; Log($"drag: down at {Cursor.Position}"); };
        drag.MouseMove += (s, e) => { if (down != null) moves++; };
        drag.MouseUp += (s, e) => { Log($"drag: up at {Cursor.Position} after {moves} moves (from {down})"); down = null; };
        drag.SendToBack();

        var open = Add(new Button { Text = "Open dialog", Bounds = new Rectangle(20, 450, 160, 44) }, "open");
        open.Click += (s, e) => OpenDialog();
        var quit = Add(new Button { Text = "Close app", Bounds = new Rectangle(200, 450, 160, 44) }, "quit");
        quit.Click += (s, e) => { Log("quit: closing"); Win.Close(); };

        Win.Shown += (s, e) => { WriteRects(); Log($"shown at {Win.Bounds} (physical), dpi {Win.DeviceDpi}, elevated {elevated}"); };
        Win.Move += (s, e) => WriteRects();
        Win.DpiChanged += (s, e) => { Log($"dpi changed {e.DeviceDpiOld} -> {e.DeviceDpiNew}"); };
        Win.Resize += (s, e) => WriteRects();
        Win.FormClosed += (s, e) => Log("closed");
        Application.Run(Win);
    }

    static T Add<T>(T c, string name) where T : Control { Win.Controls.Add(c); Named[name] = c; c.Name = name; return c; }

    static void LogMouse(string name, MouseEventArgs e)
    {
        var c = Named[name]; var r = c.RectangleToScreen(c.ClientRectangle); var p = Cursor.Position;
        int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
        Log($"{name}: MouseDown {e.Button} at {p}, {p.X - cx:+0;-0},{p.Y - cy:+0;-0} from centre of {r}");
    }

    static void OpenDialog()
    {
        var d = new Form { Text = "Reparo dialog", StartPosition = FormStartPosition.Manual, Location = new Point(Win.Right - 120, Win.Bottom - 160), Size = new Size(360, 220), Font = Win.Font };
        var ok = new Button { Text = "OK in dialog", Bounds = new Rectangle(20, 20, 160, 44), Name = "dialogok" };
        ok.MouseDown += (s, e) => { var p = Cursor.Position; Log($"dialogok: MouseDown at {p}"); };
        ok.Click += (s, e) => { Log("dialogok: Click, closing dialog"); d.Close(); };
        d.Controls.Add(ok); Named["dialogok"] = ok;
        d.Shown += (s, e) => { WriteRects(); Log($"dialog shown at {d.Bounds}, outside the main window: {!Win.Bounds.Contains(d.Bounds)}"); };
        d.FormClosed += (s, e) => { Named.Remove("dialogok"); WriteRects(); };
        d.Show(Win);
    }

    static void WriteRects()
    {
        var drag = Named["drag"]; var o = drag.PointToScreen(Point.Empty);
        var extra = new[] { $"\"dragfrom\": [{o.X + 10}, {o.Y + 10}, {o.X + 110}, {o.Y + 30}]", $"\"dropto\": [{o.X + 400}, {o.Y + 140}, {o.X + 470}, {o.Y + 160}]" };
        var parts = extra.Concat(Named.Where(kv => !kv.Value.IsDisposed && kv.Value.IsHandleCreated).Select(kv =>
        { var r = kv.Value.RectangleToScreen(kv.Value.ClientRectangle); return $"\"{kv.Key}\": [{r.Left}, {r.Top}, {r.Right}, {r.Bottom}]"; }));
        File.WriteAllText(Path.Combine(Dir, "rects.json"), "{" + string.Join(", ", parts) + "}");
    }
}

/// A Panel that logs WM_MOUSEHWHEEL, which WinForms' AutoScroll ignores.
class HWheelPanel : Panel
{
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x020E) { int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF); Program.Log($"hscroll: WM_MOUSEHWHEEL delta {delta}"); HorizontalScroll.Value = Math.Clamp(HorizontalScroll.Value + delta, 0, HorizontalScroll.Maximum); }
        base.WndProc(ref m);
    }
}

/// A TextBox that logs the raw key messages: VK and scan code of each WM_KEYDOWN, and each WM_CHAR.
class KeyBox : TextBox
{
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case 0x0100: case 0x0104: // WM_KEYDOWN, WM_SYSKEYDOWN
                { long lp = m.LParam.ToInt64(); string ext = ((lp >> 24) & 1) == 1 ? " ext" : ""; Program.Log($"text: KEYDOWN vk 0x{m.WParam.ToInt64():X2} scan 0x{(lp >> 16) & 0xFF:X2}{ext} mods {Control.ModifierKeys}"); break; }
            case 0x0101: case 0x0105:
                { Program.Log($"text: KEYUP vk 0x{m.WParam.ToInt64():X2}"); break; }
            case 0x0102: // WM_CHAR
                { int c = (int)m.WParam.ToInt64(); string shown = c >= 32 ? ((char)c).ToString() : "^" + (char)(c + 64); Program.Log($"text: CHAR U+{c:X4} '{shown}'"); break; }
        }
        base.WndProc(ref m);
        if (m.Msg == 0x0102 || m.Msg == 0x0302 /*WM_PASTE*/) Program.Log($"text: now \"{Text.Replace("\r\n", "\\n")}\"");
    }
}
