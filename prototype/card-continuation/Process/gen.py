# PROTOTYPE generator for ReparoHome#67 (Continuation inside a Use Application card). Writes Main.xaml; Event ids go
# to stderr. Every UI action is a Modern Click (NClick) that takes its app from the card around it (ScopeIdentifier).
#   python3 gen.py header.xaml <shape> <resize> > Card67/Main.xaml       resize: None | Maximize | ...
# Shapes:
#   holds    card(opens SpendApp) > [Activity Open request 104, Activity Approve, Activity Back], Activity After the card
#   attach   Activity Start SpendApp (an Event that starts it unless it runs), then the same card, which only attaches
#   foreach  Reparo For Each reqs > card(opens SpendApp on the request) > Activity Approve > [Click Approve, Click Back]
#   incard   card(opens SpendApp) > Reparo For Each reqs > Activity Handle > [Click Open request <req>, Approve, Back]
#   nested   card(opens SpendApp) > [Activity Open request 102, Use Browser card(Edge) > Activity Approve on the web]
# SpendApp removes Approve for 6 s the first time request 104 opens in an app instance; the web page lacks it for 6 s
# after each load.
import sys, uuid
ids = {}
def idref(kind):
    ids[kind] = ids.get(kind, 0) + 1
    return f'sap2010:WorkflowViewState.IdRef="{kind}_{ids[kind]}"'
def esc(s): return s.replace('&','&amp;').replace('<','&lt;').replace('>','&gt;').replace('"','&quot;')
ROOT = 'C:\\Users\\reparo\\UiPath\\reparo\\proto48'
EXE = ROOT + '\\app\\SpendApp.exe'
APP = "<wnd app='spendapp.exe' title='Spending requests' />"
WEB = "<html app='msedge.exe' title='Spend web' />"
URL = "http://localhost:8066/spend.html"

def assign(to, typ, vb):
    return f'''<Assign {idref("Assign")}><Assign.To><OutArgument x:TypeArguments="{typ}">[{to}]</OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments="{typ}">{esc('['+vb+']')}</InArgument></Assign.Value></Assign>'''
def trace(vb):
    return f'<InvokeMethod DisplayName="Trace" {idref("InvokeMethod")} MethodName="W" TargetType="r:Trace"><InArgument x:TypeArguments="x:String">{esc("["+vb+"]")}</InArgument></InvokeMethod>'
def seq(inner):
    return f'<Sequence {idref("Sequence")}>\n' + '\n'.join(inner) + '\n</Sequence>'
def event(name, *body):
    e = f'<r:ReparoEvent DisplayName="{esc(name)}" {idref("ReparoEvent")}>\n' + seq(body) + '\n</r:ReparoEvent>'
    sys.stderr.write(f"{name}: Main.xaml#ReparoEvent_{ids['ReparoEvent']}\n")
    return e
def activity(name, *events):
    return f'<r:ReparoActivity DisplayName="{esc(name)}" {idref("ReparoActivity")}>\n' + '\n'.join(events) + '\n</r:ReparoActivity>'
def foreach(name, over, item, *nodes):
    return f'<r:ReparoForEach DisplayName="{esc(name)}" Over="{over}" Item="{item}" {idref("ReparoForEach")}>\n' + '\n'.join(nodes) + '\n</r:ReparoForEach>'

def nclick(name, g, ctrl=None, ctrl_vb=None, web=False):
    """A Modern Click scoped to the card with ScopeGuid g. ctrl_vb: a VB expression for the control name."""
    if web:
        full, scope, kind = esc("<webctrl id='approve' tag='BUTTON' />"), esc(WEB), "Button"
    else:
        full = esc(f"<wnd ctrlname='{ctrl}' />") if ctrl else esc('["<wnd ctrlname=\'" & ' + ctrl_vb + ' & "\' />"]')
        scope, kind = esc(APP), "Button"
    return f'''<uix:NClick ActivateBefore="True" ClickType="Single" DisplayName="{esc(name)}" {idref("NClick")} KeyModifiers="None" MouseButton="Left" ScopeIdentifier="{g}" Timeout="3" Version="V5">
  <uix:NClick.Target>
    <uix:TargetAnchorable ElementType="{kind}" FullSelectorArgument="{full}" Guid="{uuid.uuid4()}" ScopeSelectorArgument="{scope}" SearchSteps="Selector" Version="V6" />
  </uix:NClick.Target>
</uix:NClick>'''

def card(body, g, args_vb, resize, name="Use SpendApp"):
    """Modern "Use Application/Browser" on SpendApp: opens it when it isn't open, closes it if it opened it."""
    rs = '' if resize == 'None' else f' WindowResize="{resize}"'
    return f'''<uix:NApplicationCard AttachMode="ByInstance" DisplayName="{name}" HealingAgentBehavior="Job" OpenMode="IfNotOpen"{rs} {idref("NApplicationCard")} ScopeGuid="{g}" Version="V2">
  <uix:NApplicationCard.Body>
    <ActivityAction x:TypeArguments="x:Object">
      <ActivityAction.Argument><DelegateInArgument x:TypeArguments="x:Object" Name="WSSessionData" /></ActivityAction.Argument>
      {body}
    </ActivityAction>
  </uix:NApplicationCard.Body>
  <uix:NApplicationCard.TargetApp>
    <uix:TargetApp FilePath="{EXE}" Selector="{esc(APP)}" Version="V3">
      <uix:TargetApp.Arguments><InArgument x:TypeArguments="x:String">{esc("[" + args_vb + "]")}</InArgument></uix:TargetApp.Arguments>
      <uix:TargetApp.WorkingDirectory><InArgument x:TypeArguments="x:String" /></uix:TargetApp.WorkingDirectory>
    </uix:TargetApp>
  </uix:NApplicationCard.TargetApp>
</uix:NApplicationCard>'''

def browser_card(body, g):
    return f'''<uix:NApplicationCard AttachMode="ByInstance" DisplayName="Use Spend web" HealingAgentBehavior="Job" InteractionMode="DebuggerApi" OpenMode="IfNotOpen" {idref("NApplicationCard")} ScopeGuid="{g}" Version="V2">
  <uix:NApplicationCard.Body>
    <ActivityAction x:TypeArguments="x:Object">
      <ActivityAction.Argument><DelegateInArgument x:TypeArguments="x:Object" Name="WSSessionData" /></ActivityAction.Argument>
      {body}
    </ActivityAction>
  </uix:NApplicationCard.Body>
  <uix:NApplicationCard.TargetApp>
    <uix:TargetApp Area="0, 0, 0, 0" BrowserType="Edge" Selector="{esc(WEB)}" Url="{URL}" Version="V2" />
  </uix:NApplicationCard.TargetApp>
</uix:NApplicationCard>'''

def start_if_not_running():
    vb = f'If(System.Diagnostics.Process.GetProcessesByName("SpendApp").Length = 0, System.Diagnostics.Process.Start("{EXE}", "--late-approve 104"), Nothing) IsNot Nothing'
    return assign("ok", "x:Boolean", vb) + f'<Delay Duration="00:00:03" {idref("Delay")} />'

shape, resize = sys.argv[2], sys.argv[3]
g = str(uuid.uuid4())
end = lambda: activity("After the card", event("Note the end", trace('"after the card"')))
three = lambda: [
    activity("Open the request", event("Click Open request 104", nclick("Click Open request 104", g, "Openrequest104"))),
    activity("Approve the request", event("Click Approve", nclick("Click Approve", g, "Approve"))),
    activity("Go back", event("Click Back", nclick("Click Back", g, "Backtorequests"))),
]
if shape == 'holds':
    nodes = [card(seq(three()), g, '"--late-approve 104"', resize), end()]
elif shape == 'attach':
    first = activity("Open SpendApp", event("Start SpendApp", start_if_not_running()))
    nodes = [first, card(seq(three()), g, '"--late-approve 104"', resize), end()]
elif shape == 'foreach':
    nodes = [foreach("Each request", "reqs", "req",
                card(seq([activity("Approve the request",
                    event("Click Approve", nclick("Click Approve", g, "Approve")),
                    event("Click Back", nclick("Click Back", g, "Backtorequests")))]),
                    g, '"--late-approve 104 --open " & req', resize)),
             end()]
elif shape == 'incard':
    nodes = [card(seq([foreach("Each request", "reqs", "req",
                activity("Handle the request",
                    event("Click Open request", nclick("Click Open request", g, ctrl_vb='"Openrequest" & req')),
                    event("Click Approve", nclick("Click Approve", g, "Approve")),
                    event("Click Back", nclick("Click Back", g, "Backtorequests"))))]),
                g, '"--late-approve 104"', resize),
             end()]
elif shape == 'nested':
    gw = str(uuid.uuid4())
    nodes = [card(seq([
                activity("Open the request", event("Click Open request 102", nclick("Click Open request 102", g, "Openrequest102"))),
                browser_card(seq([activity("Approve on the web", event("Click Approve (web)", nclick("Click Approve (web)", gw, web=True)))]), gw),
                activity("Go back", event("Click Back", nclick("Click Back", g, "Backtorequests"))),
             ]), g, '"--late-approve 0"', resize),
             end()]
else:
    sys.exit("unknown shape " + shape)
variables = ('  <Variable x:TypeArguments="x:Boolean" Name="ok" />\n'
             '  <Variable x:TypeArguments="x:String" Name="req" />\n'
             '  <Variable x:TypeArguments="scg:List(x:String)" Name="reqs" Default="[New List(Of String) From {&quot;102&quot;, &quot;104&quot;}]" />\n')
name = f"{shape}_{resize.lower()}"
proc = (f'<r:ReparoProcess DisplayName="{name}" {idref("ReparoProcess")} RecoverAt="Event" FakeDonePath="{ROOT}\\fake-done.json">\n'
        '<r:ReparoProcess.Variables>\n' + variables + '</r:ReparoProcess.Variables>\n' + '\n'.join(nodes) + '\n</r:ReparoProcess>')
outer = (f'<TryCatch DisplayName="Author outer Try Catch" {idref("TryCatch")}><TryCatch.Try>\n' + proc + '\n</TryCatch.Try><TryCatch.Catches>'
         f'<Catch x:TypeArguments="s:Exception" {idref("Catch")}><ActivityAction x:TypeArguments="s:Exception"><ActivityAction.Argument><DelegateInArgument x:TypeArguments="s:Exception" Name="ex" /></ActivityAction.Argument>'
         + trace('"author outer Try Catch caught " & ex.GetType().FullName & ": " & ex.Message.Split(vbLf)(0)')
         + '</ActivityAction></Catch></TryCatch.Catches></TryCatch>')
print(open(sys.argv[1]).read().replace('@@BODY@@', outer))
