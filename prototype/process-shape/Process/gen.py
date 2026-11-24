# PROTOTYPE generator for the spending-approval Process XAML.
# Run: python3 gen.py header.xaml [plain|trycatch|retryscope] [reparo|native] > SpendingApproval/Main.xaml
# "native" writes the same Process in plain UiPath (Sequence, If, ForEach), the workflow a retrofit starts from.
import itertools, sys
MODE = sys.argv[2] if len(sys.argv) > 2 else "plain"   # plain | trycatch | retryscope
NATIVE = len(sys.argv) > 3 and sys.argv[3] == "native"
ids = {}
def idref(kind):
    ids[kind] = ids.get(kind, 0) + 1
    return f'sap2010:WorkflowViewState.IdRef="{kind}_{ids[kind]}"'
def esc(s): return s.replace('&','&amp;').replace('<','&lt;').replace('>','&gt;').replace('"','&quot;')
APP = "<wnd app='spendapp.exe' title='Spending requests' />"
def sel(ctrl): return esc(APP + f"<wnd ctrlname='{ctrl}' />")
def sel_expr(vb): return esc('[' + vb + ']')

def click(name, ctrl=None, expr=None, timeout=2000):
    s = sel(ctrl) if ctrl else sel_expr(expr)
    return f'''<ui:Click DisplayName="{esc(name)}" {idref("Click")} ClickType="CLICK_SINGLE" MouseButton="BTN_LEFT" KeyModifiers="None">
  <ui:Click.Target><ui:Target Selector="{s}" TimeoutMS="{timeout}" WaitForReady="INTERACTIVE" /></ui:Click.Target>
</ui:Click>'''
def gettext(ctrl, var):
    return f'''<ui:GetValue DisplayName="Get Text {ctrl}" {idref("GetValue")}>
  <ui:GetValue.Value><OutArgument x:TypeArguments="x:String">[{var}]</OutArgument></ui:GetValue.Value>
  <ui:GetValue.Target><ui:Target Selector="{sel(ctrl)}" TimeoutMS="5000" WaitForReady="INTERACTIVE" /></ui:GetValue.Target>
</ui:GetValue>'''
def assign(to, typ, vb):
    return f'''<Assign {idref("Assign")}><Assign.To><OutArgument x:TypeArguments="{typ}">[{to}]</OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments="{typ}">{esc('['+vb+']')}</InArgument></Assign.Value></Assign>'''
def seq(inner, local_vars=()):
    v = ''.join(f'<Variable x:TypeArguments="x:String" Name="{n}" />' for n in local_vars)
    vs = f'<Sequence.Variables>{v}</Sequence.Variables>' if v else ''
    return f'<Sequence {idref("Sequence")}>{vs}\n' + '\n'.join(inner) + '\n</Sequence>'
def event(name, *body, local=()):
    if NATIVE: return seq(body, local).replace('<Sequence ', f'<Sequence DisplayName="{esc(name)}" ', 1)
    return f'<r:ReparoEvent DisplayName="{esc(name)}" {idref("ReparoEvent")}>\n' + seq(body, local) + '\n</r:ReparoEvent>'
def activity(name, *events):
    if NATIVE: return f'<Sequence DisplayName="{esc(name)}" {idref("Sequence")}>\n' + '\n'.join(events) + '\n</Sequence>'
    return f'<r:ReparoActivity DisplayName="{esc(name)}" {idref("ReparoActivity")}>\n' + '\n'.join(events) + '\n</r:ReparoActivity>'
def if_(name, cond, then, els):
    if NATIVE:
        return (f'<If DisplayName="{esc(name)}" Condition="{esc("["+cond+"]")}" {idref("If")}>\n<If.Then>\n'
                + seq(then) + '\n</If.Then>\n<If.Else>\n' + seq(els) + '\n</If.Else>\n</If>')
    return (f'<r:ReparoIf DisplayName="{esc(name)}" Condition="{esc("["+cond+"]")}" {idref("ReparoIf")}>\n'
            + '\n'.join(then) + '\n<r:ReparoIf.Else>\n' + '\n'.join(els) + '\n</r:ReparoIf.Else>\n</r:ReparoIf>')
def reject(activity_name, reason):
    return activity(activity_name,
        event("Open the Reason dropdown", click("Click Reason", "Reason")),
        event(f"Pick {reason}", click(f"Click {reason}", reason.replace(' ', ''))),
        event("Click Reject", click("Click Reject", "Reject")))

def approve_body():
    c = click("Click Approve", "Approve")
    if MODE == "trycatch":
        return (f'<TryCatch DisplayName="Author Try Catch" {idref("TryCatch")}><TryCatch.Try>' + c + '</TryCatch.Try><TryCatch.Catches>'
                f'<Catch x:TypeArguments="ui:SelectorNotFoundException" {idref("Catch")}><ActivityAction x:TypeArguments="ui:SelectorNotFoundException"><ActivityAction.Argument><DelegateInArgument x:TypeArguments="ui:SelectorNotFoundException" Name="ex" /></ActivityAction.Argument>'
                f'<InvokeMethod DisplayName="Trace catch" {idref("InvokeMethod")} MethodName="W" TargetType="r:Trace"><InArgument x:TypeArguments="x:String">[&quot;author Try Catch caught &quot; &amp; ex.GetType().Name]</InArgument></InvokeMethod>'
                '</ActivityAction></Catch></TryCatch.Catches></TryCatch>')
    if MODE == "retryscope":
        return (f'<ui:RetryScope DisplayName="Author Retry Scope" NumberOfRetries="5" RetryInterval="00:00:01" {idref("RetryScope")}><ui:RetryScope.ActivityBody><ActivityAction>'
                + c + '</ActivityAction></ui:RetryScope.ActivityBody></ui:RetryScope>')
    return c

body = [
  activity("Open the spending requests",
    event("Open the requests window",
      f'<InvokeMethod DisplayName="Start SpendApp" {idref("InvokeMethod")} MethodName="Start" TargetType="sd:Process"><InArgument x:TypeArguments="x:String">C:\\Users\\reparo\\UiPath\\reparo\\proto46\\app\\SpendApp.exe</InArgument><InArgument x:TypeArguments="x:String">[If(app_args, "--late-approve 105")]</InArgument></InvokeMethod>',
      f'<Delay Duration="00:00:03" {idref("Delay")} />'),
    event("Read the pending requests",
      gettext("pending", "raw"),
      assign("pending", "scg:List(x:Int32)", 'raw.Split(","c).Select(Function(s) CInt(s)).ToList()'), local=("raw",))),
  (f'<ui:ForEach x:TypeArguments="x:Int32" DisplayName="each_request" Values="[pending]" {idref("ForEach")}>\n<ui:ForEach.Body>\n'
   f'<ActivityAction x:TypeArguments="x:Int32">\n<ActivityAction.Argument><DelegateInArgument x:TypeArguments="x:Int32" Name="request_id" /></ActivityAction.Argument>\n<Sequence {idref("Sequence")}>'
   if NATIVE else f'<r:ReparoForEach DisplayName="each_request" Over="pending" Item="request_id" {idref("ReparoForEach")}>'),
  activity("Open the request",
    event("Open the request", click("Click Open request", expr='"' + APP.replace('"','""') + "<wnd ctrlname='Openrequest\" + request_id.ToString + \"' />\"")),
    event("Read the request",
      gettext("amount", "raw"), assign("amount", "x:Int32", "CInt(raw)"),
      gettext("budget", "raw2"), assign("remaining_budget", "x:Int32", "CInt(raw2)"), local=("raw", "raw2"))),
  if_("decide", "amount <= approval_limit AndAlso amount <= remaining_budget",
    [activity("Approve the request", event("Click Approve", approve_body()))],
    [if_("over the limit", "amount > approval_limit",
       [reject("Reject over limit", "Over the approval limit")],
       [reject("Reject for budget", "Budget exhausted")])]),
  activity("Return to the list", event("Go back to the requests", click("Click Back to requests", "Backtorequests"))),
  '</Sequence>\n</ActivityAction>\n</ui:ForEach.Body>\n</ui:ForEach>' if NATIVE else '</r:ReparoForEach>',
  activity("Record the final budget",
    event("Read the final budget", gettext("budget", "raw"), assign("remaining_budget", "x:Int32", "CInt(raw)"), local=("raw",))),
]

header = open(sys.argv[1]).read()
proc = f'''<r:ReparoProcess DisplayName="spending_approval" {idref("ReparoProcess")} ContinueFrom="[continue_from]" ContinuePasses="[continue_passes]" ContinueVariables="[continue_variables]" FakeDonePath="C:\\Users\\reparo\\UiPath\\reparo\\proto46\\fake-done.json">
<r:ReparoProcess.Variables>
  <Variable x:TypeArguments="x:Int32" Default="500" Name="approval_limit" />
  <Variable x:TypeArguments="scg:List(x:Int32)" Name="pending" />
  <Variable x:TypeArguments="x:Int32" Name="request_id" />
  <Variable x:TypeArguments="x:Int32" Name="amount" />
  <Variable x:TypeArguments="x:Int32" Name="remaining_budget" />
</r:ReparoProcess.Variables>
''' + '\n'.join(body) + '\n</r:ReparoProcess>'
if NATIVE:
    proc = (proc.replace('<r:ReparoProcess.Variables>', '<Sequence.Variables>').replace('</r:ReparoProcess.Variables>', '</Sequence.Variables>')
                .replace('</r:ReparoProcess>', '</Sequence>').replace('<Variable x:TypeArguments="x:Int32" Name="request_id" />\n', ''))
    proc = '<Sequence DisplayName="spending_approval" ' + idref("Sequence") + '>' + proc[proc.index('\n'):]
print(header.replace('@@BODY@@', proc))
