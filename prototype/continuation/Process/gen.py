# PROTOTYPE generator for ReparoHome#48. Writes Main.xaml for one of two Processes:
#   python3 gen.py header.xaml spending                    > SpendingApproval/Main.xaml
#   python3 gen.py header.xaml card <default|always> <before|after> [attach]  > CardProbe/Main.xaml
# spending: #46's spending approval, plus a nested Reparo For Each over the fields it reads, a DataTable of
#   decisions, Variables of several types, and the author's own Try Catch around Reparo Process.
# card: one Event whose UiPath "Use Application/Browser" opened SpendApp itself, where Click Approve fails,
#   to see whether the app survives the fault, the wait for Reparo, and the canceled branch.
import sys, uuid
KIND = sys.argv[2]
ids = {}
def idref(kind):
    ids[kind] = ids.get(kind, 0) + 1
    return f'sap2010:WorkflowViewState.IdRef="{kind}_{ids[kind]}"'
def esc(s): return s.replace('&','&amp;').replace('<','&lt;').replace('>','&gt;').replace('"','&quot;')
ROOT = 'C:\\Users\\reparo\\UiPath\\reparo\\proto48'
EXE = ROOT + '\\app\\SpendApp.exe'
APP = "<wnd app='spendapp.exe' title='Spending requests' />"
def sel(ctrl): return esc(APP + f"<wnd ctrlname='{ctrl}' />")

def click(name, ctrl=None, expr=None, timeout=2000):
    s = sel(ctrl) if ctrl else esc('[' + expr + ']')
    return f'''<ui:Click DisplayName="{esc(name)}" {idref("Click")} ClickType="CLICK_SINGLE" MouseButton="BTN_LEFT" KeyModifiers="None">
  <ui:Click.Target><ui:Target Selector="{s}" TimeoutMS="{timeout}" WaitForReady="INTERACTIVE" /></ui:Click.Target>
</ui:Click>'''
def gettext(var, ctrl=None, expr=None):
    s = sel(ctrl) if ctrl else esc('[' + expr + ']')
    return f'''<ui:GetValue DisplayName="Get Text {esc(ctrl or 'field')}" {idref("GetValue")}>
  <ui:GetValue.Value><OutArgument x:TypeArguments="x:String">[{var}]</OutArgument></ui:GetValue.Value>
  <ui:GetValue.Target><ui:Target Selector="{s}" TimeoutMS="5000" WaitForReady="INTERACTIVE" /></ui:GetValue.Target>
</ui:GetValue>'''
def assign(to, typ, vb):
    return f'''<Assign {idref("Assign")}><Assign.To><OutArgument x:TypeArguments="{typ}">[{to}]</OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments="{typ}">{esc('['+vb+']')}</InArgument></Assign.Value></Assign>'''
def effect(vb):
    """A VB call for its side effect, assigned to a throwaway Boolean."""
    return assign("ok", "x:Boolean", f"({vb}) IsNot Nothing")
def trace(vb):
    return f'<InvokeMethod DisplayName="Trace" {idref("InvokeMethod")} MethodName="W" TargetType="r:Trace"><InArgument x:TypeArguments="x:String">{esc("["+vb+"]")}</InArgument></InvokeMethod>'
def seq(inner, local=()):
    v = ''.join(f'<Variable x:TypeArguments="{t}" Name="{n}" />' for n, t in local)
    vs = f'<Sequence.Variables>{v}</Sequence.Variables>' if v else ''
    return f'<Sequence {idref("Sequence")}>{vs}\n' + '\n'.join(inner) + '\n</Sequence>'
def event(name, *body, local=()):
    return f'<r:ReparoEvent DisplayName="{esc(name)}" {idref("ReparoEvent")}>\n' + seq(body, local) + '\n</r:ReparoEvent>'
def activity(name, *events):
    return f'<r:ReparoActivity DisplayName="{esc(name)}" {idref("ReparoActivity")}>\n' + '\n'.join(events) + '\n</r:ReparoActivity>'
def foreach(name, over, item, *nodes):
    return f'<r:ReparoForEach DisplayName="{esc(name)}" Over="{over}" Item="{item}" {idref("ReparoForEach")}>\n' + '\n'.join(nodes) + '\n</r:ReparoForEach>'
def if_(name, cond, then, els):
    return (f'<r:ReparoIf DisplayName="{esc(name)}" Condition="{esc("["+cond+"]")}" {idref("ReparoIf")}>\n'
            + '\n'.join(then) + '\n<r:ReparoIf.Else>\n' + '\n'.join(els) + '\n</r:ReparoIf.Else>\n</r:ReparoIf>')
def start_app(args_vb):
    return (f'<InvokeMethod DisplayName="Start SpendApp" {idref("InvokeMethod")} MethodName="Start" TargetType="sd:Process"><InArgument x:TypeArguments="x:String">{EXE}</InArgument><InArgument x:TypeArguments="x:String">{esc("["+args_vb+"]")}</InArgument></InvokeMethod>'
            f'<Delay Duration="00:00:03" {idref("Delay")} />')
def log_decision(decision):
    return event(f"Log the {decision}", effect(f'decisions.Rows.Add(request_id, "{decision}", amount)'), local=(("ok", "x:Boolean"),))
def reject(activity_name, reason):
    return activity(activity_name,
        event("Open the Reason dropdown", click("Click Reason", "Reason")),
        event(f"Pick {reason}", click(f"Click {reason}", reason.replace(' ', ''))),
        event("Click Reject", click("Click Reject", "Reject")),
        log_decision("rejection"))

def card(body, close):
    """UiPath's Modern "Use Application/Browser" on SpendApp; it opens the app when it isn't open."""
    g = str(uuid.uuid4())
    close_attr = '' if close == 'default' else f' CloseMode="{close.capitalize()}"'
    return f'''<uix:NApplicationCard AttachMode="ByInstance" DisplayName="Use SpendApp" HealingAgentBehavior="Job" OpenMode="IfNotOpen"{close_attr} {idref("NApplicationCard")} ScopeGuid="{g}" Version="V2">
  <uix:NApplicationCard.Body>
    <ActivityAction x:TypeArguments="x:Object">
      <ActivityAction.Argument><DelegateInArgument x:TypeArguments="x:Object" Name="WSSessionData" /></ActivityAction.Argument>
      {seq([body])}
    </ActivityAction>
  </uix:NApplicationCard.Body>
  <uix:NApplicationCard.TargetApp>
    <uix:TargetApp FilePath="{EXE}" Selector="{esc(APP)}" Version="V3">
      <uix:TargetApp.Arguments><InArgument x:TypeArguments="x:String">--late-approve 104 --open 104</InArgument></uix:TargetApp.Arguments>
      <uix:TargetApp.WorkingDirectory><InArgument x:TypeArguments="x:String" /></uix:TargetApp.WorkingDirectory>
    </uix:TargetApp>
  </uix:NApplicationCard.TargetApp>
</uix:NApplicationCard>'''

VARS_SPENDING = '''  <Variable x:TypeArguments="x:Int32" Default="500" Name="approval_limit" />
  <Variable x:TypeArguments="scg:List(x:Int32)" Name="pending" />
  <Variable x:TypeArguments="x:Int32" Name="request_id" />
  <Variable x:TypeArguments="scg:List(x:String)" Default="[New List(Of String) From {&quot;amount&quot;, &quot;budget&quot;}]" Name="fields" />
  <Variable x:TypeArguments="x:String" Name="field" />
  <Variable x:TypeArguments="scg:Dictionary(x:String, x:Int32)" Default="[New Dictionary(Of String, Int32)]" Name="values" />
  <Variable x:TypeArguments="x:Int32" Name="amount" />
  <Variable x:TypeArguments="x:Int32" Name="remaining_budget" />
  <Variable x:TypeArguments="sdt:DataTable" Name="decisions" />
  <Variable x:TypeArguments="s:DateTimeOffset" Default="[DateTimeOffset.Now]" Name="started" />
  <Variable x:TypeArguments="s:String[]" Default="[{&quot;proto48&quot;}]" Name="tags" />
  <Variable x:TypeArguments="ss:SecureString" Default="[New System.Net.NetworkCredential(&quot;&quot;, &quot;s3cret&quot;).SecurePassword]" Name="contact" />
'''

if KIND == 'spending':
    field_sel = '"' + APP.replace('"', '""') + "<wnd ctrlname='\" + field + \"' />\""
    open_sel = '"' + APP.replace('"', '""') + "<wnd ctrlname='Openrequest\" + request_id.ToString + \"' />\""
    nodes = [
      activity("Open the spending requests",
        event("Open the requests window", start_app('If(app_args, "--late-approve 105")')),
        event("Read the pending requests",
          gettext("raw", ctrl="pending"),
          assign("pending", "scg:List(x:Int32)", 'raw.Split(","c).Select(Function(s) CInt(s)).ToList()'),
          assign("decisions", "sdt:DataTable", 'New DataTable("decisions")'),
          effect('decisions.Columns.Add("request_id", GetType(Int32))'),
          effect('decisions.Columns.Add("decision", GetType(String))'),
          effect('decisions.Columns.Add("amount", GetType(Int32))'),
          local=(("raw", "x:String"), ("ok", "x:Boolean")))),
      foreach("each_request", "pending", "request_id",
        activity("Open the request", event("Open the request", click("Click Open request", expr=open_sel))),
        foreach("each_field", "fields", "field",
          activity("Read the request",
            event("Read a field",
              gettext("raw", expr=field_sel),
              f'<InvokeMethod DisplayName="values(field) = raw" {idref("InvokeMethod")} MethodName="set_Item"><InvokeMethod.TargetObject><InArgument x:TypeArguments="scg:Dictionary(x:String, x:Int32)">[values]</InArgument></InvokeMethod.TargetObject><InArgument x:TypeArguments="x:String">[field]</InArgument><InArgument x:TypeArguments="x:Int32">[CInt(raw)]</InArgument></InvokeMethod>',
              local=(("raw", "x:String"),)))),
        activity("Record the request",
          event("Copy the fields", assign("amount", "x:Int32", 'values("amount")'), assign("remaining_budget", "x:Int32", 'values("budget")'))),
        if_("decide", "amount <= approval_limit AndAlso amount <= remaining_budget",
          [activity("Approve the request", event("Click Approve", click("Click Approve", "Approve")), log_decision("approval"))],
          [if_("over the limit", "amount > approval_limit",
             [reject("Reject over limit", "Over the approval limit")],
             [reject("Reject for budget", "Budget exhausted")])]),
        activity("Return to the list", event("Go back to the requests", click("Click Back to requests", "Backtorequests")))),
      activity("Record the final budget",
        event("Read the final budget", gettext("raw", ctrl="budget"), assign("remaining_budget", "x:Int32", "CInt(raw)"),
              trace('"decisions at the end: " & String.Join(" | ", decisions.AsEnumerable().Select(Function(row) String.Join(",", row.ItemArray)))'),
              local=(("raw", "x:String"),))),
    ]
    variables, cancel_before, name = VARS_SPENDING, False, "spending_approval"
else:
    close, when = sys.argv[3], sys.argv[4]
    attach = len(sys.argv) > 5 and sys.argv[5] == 'attach'
    # attach: an earlier Event opened SpendApp, so the card only attaches to it (the common retrofit shape).
    opener = [activity("Open the request window", event("Open SpendApp on request 104", start_app('"--late-approve 104 --open 104"')))] if attach else []
    nodes = opener + [activity("Approve the open request", event("Click Approve", card(click("Click Approve", "Approve"), close)))]
    variables, cancel_before, name = '  <Variable x:TypeArguments="x:Int32" Default="104" Name="request_id" />\n', when == 'before', f"card_{close}_{when}{'_attach' if attach else ''}"

proc = (f'<r:ReparoProcess DisplayName="{name}" {idref("ReparoProcess")} CancelBeforeRecovery="{cancel_before}" FakeDonePath="{ROOT}\\fake-done.json">\n'
        '<r:ReparoProcess.Variables>\n' + variables + '</r:ReparoProcess.Variables>\n' + '\n'.join(nodes) + '\n</r:ReparoProcess>')
# The author's own handler around Reparo Process: it sees only what Reparo couldn't recover.
outer = (f'<TryCatch DisplayName="Author outer Try Catch" {idref("TryCatch")}><TryCatch.Try>\n' + proc + '\n</TryCatch.Try><TryCatch.Catches>'
         f'<Catch x:TypeArguments="s:Exception" {idref("Catch")}><ActivityAction x:TypeArguments="s:Exception"><ActivityAction.Argument><DelegateInArgument x:TypeArguments="s:Exception" Name="ex" /></ActivityAction.Argument>'
         + trace('"author outer Try Catch caught " & ex.GetType().FullName & " (the object the Bridge rethrew: " & Object.ReferenceEquals(ex, ReparoProcess.LastRethrown).ToString() & "): " & ex.Message.Split(vbLf)(0) & " | stack top: " & ex.StackTrace.Split(vbLf)(0).Trim()')
         + '</ActivityAction></Catch></TryCatch.Catches></TryCatch>')
print(open(sys.argv[1]).read().replace('@@BODY@@', outer))
