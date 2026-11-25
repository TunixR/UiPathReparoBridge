# PROTOTYPE generator for ReparoHome#66. Writes Main.xaml (ids go to stderr):
#   python3 gen.py header.xaml holds  <default|always> <Event|Process>          > CardHolds/Main.xaml
#   python3 gen.py header.xaml inside <default|always|...> <Event|Process> [CloseOverride] [TrackHoldMs] > CardInside/Main.xaml
# holds: one UiPath "Use Application/Browser" card that opens SpendApp and holds three Reparo Activities
#   (Card > Sequence > Reparo Activity > Reparo Event > Click), then one Reparo Activity after the card.
# inside: #48's shape, the card inside the Event that fails.
# Click Approve fails: SpendApp removes Approve for 6 s the first time request 104 opens.
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
def card(body, close):
    """UiPath's Modern "Use Application/Browser" on SpendApp; it opens the app when it isn't open."""
    g = str(uuid.uuid4())
    close_attr = '' if close == 'default' else f' CloseMode="{close.capitalize()}"'
    return f'''<uix:NApplicationCard AttachMode="ByInstance" DisplayName="Use SpendApp" HealingAgentBehavior="Job" OpenMode="IfNotOpen"{close_attr} {idref("NApplicationCard")} ScopeGuid="{g}" Version="V2">
  <uix:NApplicationCard.Body>
    <ActivityAction x:TypeArguments="x:Object">
      <ActivityAction.Argument><DelegateInArgument x:TypeArguments="x:Object" Name="WSSessionData" /></ActivityAction.Argument>
      {body}
    </ActivityAction>
  </uix:NApplicationCard.Body>
  <uix:NApplicationCard.TargetApp>
    <uix:TargetApp FilePath="{EXE}" Selector="{esc(APP)}" Version="V3">
      <uix:TargetApp.Arguments><InArgument x:TypeArguments="x:String">--late-approve 104 --open 104</InArgument></uix:TargetApp.Arguments>
      <uix:TargetApp.WorkingDirectory><InArgument x:TypeArguments="x:String" /></uix:TargetApp.WorkingDirectory>
    </uix:TargetApp>
  </uix:NApplicationCard.TargetApp>
</uix:NApplicationCard>'''

def event_(name, *body):
    e = event(name, *body)
    sys.stderr.write(f"{name}: Main.xaml#ReparoEvent_{ids['ReparoEvent']}\n")
    return e

WEB = "<html app='msedge.exe' title='Spend web' />"
URL = "http://localhost:8066/spend.html"
def browser_card(body, close, g):
    """UiPath's Modern "Use Browser" on Edge over Chromium API (no extension); it opens the page when it isn't open."""
    close_attr = '' if close == 'default' else f' CloseMode="{close.capitalize()}"'
    return f'''<uix:NApplicationCard AttachMode="ByInstance" DisplayName="Use Spend web" HealingAgentBehavior="Job" InteractionMode="DebuggerApi" OpenMode="IfNotOpen"{close_attr} {idref("NApplicationCard")} ScopeGuid="{g}" Version="V2">
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
def nclick(name, g):
    return f'''<uix:NClick ActivateBefore="True" ClickType="Single" DisplayName="{esc(name)}" {idref("NClick")} KeyModifiers="None" MouseButton="Left" ScopeIdentifier="{g}" Timeout="3" Version="V5">
  <uix:NClick.Target>
    <uix:TargetAnchorable ElementType="Button" FullSelectorArgument="{esc("<webctrl id='approve' tag='BUTTON' />")}" Guid="{uuid.uuid4()}" ScopeSelectorArgument="{esc(WEB)}" SearchSteps="Selector" Version="V6" />
  </uix:NClick.Target>
</uix:NClick>'''

shape, close, recover_at = sys.argv[2], sys.argv[3], sys.argv[4]
override = sys.argv[5] if len(sys.argv) > 5 and sys.argv[5] != '-' else None
hold = sys.argv[6] if len(sys.argv) > 6 else '0'
if shape == 'holds':
    inner = seq([
        activity("Read the request", event_("Read the title", gettext("title", ctrl="title"))),
        activity("Approve the request",
            event_("Read the amount", gettext("amount_txt", ctrl="amount")),
            event_("Click Approve", click("Click Approve", "Approve"))),
        activity("Check the status", event_("Read the status", gettext("status", ctrl="status"))),
    ])
    nodes = [card(inner, close),
             activity("After the card", event_("Note the end", trace('"after the card: title=" & title & " amount=" & amount_txt & " status=" & status')))]
elif shape == 'browser-holds':
    g = str(uuid.uuid4())
    nodes = [browser_card(seq([activity("Approve the request", event_("Click Approve", nclick("Click Approve", g)))]), close, g),
             activity("After the card", event_("Note the end", trace('"after the card"')))]
elif shape == 'browser-inside':
    g = str(uuid.uuid4())
    nodes = [activity("Approve the request", event_("Click Approve", browser_card(seq([nclick("Click Approve", g)]), close, g))),
             activity("After the card", event_("Note the end", trace('"after the card"')))]
else:
    nodes = [activity("Approve the request", event_("Click Approve", card(seq([click("Click Approve", "Approve")]), close))),
             activity("After the card", event_("Note the end", trace('"after the card"')))]
variables = ''.join(f'  <Variable x:TypeArguments="x:String" Name="{n}" />\n' for n in ("title", "amount_txt", "status"))
ov = f' CardCloseOverride="{override}"' if override else ''
name = f"{shape}_{close}_{recover_at.lower()}" + (f"_override_{override.lower()}" if override else '') + (f"_hold{hold}" if hold != '0' else '')
proc = (f'<r:ReparoProcess DisplayName="{name}" {idref("ReparoProcess")} RecoverAt="{recover_at}"{ov} TrackHoldMs="{hold}" FakeDonePath="{ROOT}\\fake-done.json">\n'
        '<r:ReparoProcess.Variables>\n' + variables + '</r:ReparoProcess.Variables>\n' + '\n'.join(nodes) + '\n</r:ReparoProcess>')
# The author's own handler around Reparo Process: it sees only what Reparo couldn't recover.
outer = (f'<TryCatch DisplayName="Author outer Try Catch" {idref("TryCatch")}><TryCatch.Try>\n' + proc + '\n</TryCatch.Try><TryCatch.Catches>'
         f'<Catch x:TypeArguments="s:Exception" {idref("Catch")}><ActivityAction x:TypeArguments="s:Exception"><ActivityAction.Argument><DelegateInArgument x:TypeArguments="s:Exception" Name="ex" /></ActivityAction.Argument>'
         + trace('"author outer Try Catch caught " & ex.GetType().FullName & " (the object the Bridge rethrew: " & Object.ReferenceEquals(ex, ReparoProcess.LastRethrown).ToString() & "): " & ex.Message.Split(vbLf)(0) & " | stack top: " & ex.StackTrace.Split(vbLf)(0).Trim()')
         + '</ActivityAction></Catch></TryCatch.Catches></TryCatch>')
print(open(sys.argv[1]).read().replace('@@BODY@@', outer))
