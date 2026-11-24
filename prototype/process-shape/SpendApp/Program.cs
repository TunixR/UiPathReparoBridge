// PROTOTYPE for ReparoHome#46. Desktop spending approval, seeded with the Playwright example's "full" case.
// Args: --late-approve <id>   hides Approve for 6 s the first time that request opens (a UI failure to recover).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

record Req(int Id, int Priority, string Requester, int Amount) { public string Status = "Pending"; public string Reason; }

class SpendForm : Form
{
    readonly List<Req> reqs = new() { new(101, 4, "Dan", 400), new(102, 2, "Ben", 800), new(103, 5, "Eva", 200), new(104, 1, "Ana", 300), new(105, 3, "Cleo", 450) };
    int budget = 1000;
    readonly int lateApprove;
    readonly HashSet<int> opened = new();
    readonly Label budgetLbl = L("budget"), pendingLbl = L("pending"), title = L("title"), amount = L("amount"), status = L("status");
    readonly FlowLayoutPanel list = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true };
    readonly FlowLayoutPanel detail = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Visible = false };
    readonly FlowLayoutPanel reasons = new() { AutoSize = true, Visible = false, FlowDirection = FlowDirection.TopDown };
    readonly Button approve = B("Approve"), reject = B("Reject"), reason = B("Reason"), back = B("Back to requests");
    Req current;

    static Label L(string name) => new() { Name = name, AutoSize = true, Font = new Font("Segoe UI", 12) };
    static Button B(string text) => new() { Text = text, Name = text.Replace(" ", ""), AutoSize = true, Font = new Font("Segoe UI", 12) };
    static void Log(string s) { try { File.AppendAllText(@"C:\Users\reparo\UiPath\reparo\proto46\app.log", DateTime.Now.ToString("HH:mm:ss.fff") + " APP " + s + Environment.NewLine); } catch { } }

    public SpendForm(int lateApprove)
    {
        this.lateApprove = lateApprove;
        Text = "Spending requests"; Size = new Size(700, 600); StartPosition = FormStartPosition.Manual; Location = new Point(100, 100);
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        header.Controls.Add(new Label { Text = "Remaining budget:", AutoSize = true, Font = new Font("Segoe UI", 12) });
        header.Controls.Add(budgetLbl);
        foreach (var r in new[] { "Over the approval limit", "Budget exhausted" })
        {
            var o = B(r); o.Click += (_, _) => { current.Reason = r; reason.Text = "Reason: " + r; reasons.Visible = false; Log($"reason {current.Id} {r}"); };
            reasons.Controls.Add(o);
        }
        reason.Click += (_, _) => reasons.Visible = !reasons.Visible;
        approve.Click += (_, _) => Decide("Approved");
        reject.Click += (_, _) => { if (current.Reason == null) { MessageBox.Show("Pick a reason first"); return; } Decide("Rejected"); };
        back.Click += (_, _) => { Log("back"); ShowList(); };
        detail.Controls.AddRange(new Control[] { title, amount, approve, reject, reason, reasons, status, back });
        Controls.Add(list); Controls.Add(detail); Controls.Add(header);
        ShowList();
    }

    void Decide(string s)
    {
        if (current.Status != "Pending") return;
        current.Status = s;
        if (s == "Approved") budget -= current.Amount;
        Log($"decide {current.Id} {s} reason={current.Reason} budget={budget}");
        Refresh2();
    }

    void ShowList()
    {
        detail.Visible = false; list.Visible = true; list.Controls.Clear();
        var pending = reqs.Where(r => r.Status == "Pending").OrderBy(r => r.Priority).ToList();
        pendingLbl.Text = string.Join(",", pending.Select(r => r.Id));
        list.Controls.Add(new Label { Text = "Spending requests", Name = "heading", AutoSize = true, Font = new Font("Segoe UI", 16) });
        list.Controls.Add(new Label { Text = "Pending, by priority:", AutoSize = true, Font = new Font("Segoe UI", 12) });
        list.Controls.Add(pendingLbl);
        foreach (var r in reqs.OrderBy(r => r.Priority))
        {
            var b = B($"Open request {r.Id}"); var rr = r;
            b.Click += (_, _) => { Log("open " + rr.Id); ShowDetail(rr); };
            list.Controls.Add(b);
        }
        Refresh2();
    }

    void ShowDetail(Req r)
    {
        current = r; list.Visible = false; detail.Visible = true; reasons.Visible = false;
        title.Text = $"Request {r.Id}"; amount.Text = r.Amount.ToString(); reason.Text = "Reason";
        if (!detail.Controls.Contains(approve)) { detail.Controls.Add(approve); detail.Controls.SetChildIndex(approve, 2); }
        if (r.Id == lateApprove && opened.Add(r.Id))
        {
            // Removed, not hidden: a Classic selector still finds a hidden WinForms control.
            detail.Controls.Remove(approve); Log($"approve removed for {r.Id}");
            var t = new Timer { Interval = 6000 };
            t.Tick += (_, _) => { t.Stop(); if (current == r) { detail.Controls.Add(approve); detail.Controls.SetChildIndex(approve, 2); } Log("approve back"); };
            t.Start();
        }
        Refresh2();
    }

    public void Open(int id) => ShowDetail(reqs.First(r => r.Id == id));

    void Refresh2() { budgetLbl.Text = budget.ToString(); if (current != null) status.Text = current.Status; }

    [STAThread]
    static void Main(string[] args)
    {
        int late = 0, open = 0;
        for (int i = 0; i < args.Length - 1; i++) { if (args[i] == "--late-approve") late = int.Parse(args[i + 1]); if (args[i] == "--open") open = int.Parse(args[i + 1]); }
        Application.EnableVisualStyles();
        Log("start late-approve=" + late + " open=" + open);
        var f = new SpendForm(late);
        if (open != 0) f.Open(open);
        Application.Run(f);
    }
}
