# RESEARCH for ReparoHome#47: writes Main.xaml, one Switch case per scenario (geh47\scenario.txt picks one).
import sys
HEAD = open(sys.argv[1]).read()

def mark(text): return f'<g:Mark DisplayName="Mark" Text="{text}" />'
def click(name="Click missing"):
    return (f'<ui:Click DisplayName="{name}" ClickType="CLICK_SINGLE" MouseButton="BTN_LEFT" KeyModifiers="None">'
            '<ui:Click.Target><ui:Target Selector="[&quot;&lt;wnd app=\'nosuchapp.exe\' title=\'No such window\' /&gt;&quot;]" TimeoutMS="1500" WaitForReady="INTERACTIVE" /></ui:Click.Target></ui:Click>')
def seq(name, *body): return f'<Sequence DisplayName="{name}">' + "".join(body) + '</Sequence>'
def trycatch(try_body):
    return ('<TryCatch DisplayName="Try Catch"><TryCatch.Try>' + try_body + '</TryCatch.Try><TryCatch.Catches>'
            '<Catch x:TypeArguments="s:Exception"><ActivityAction x:TypeArguments="s:Exception">'
            '<ActivityAction.Argument><DelegateInArgument x:TypeArguments="s:Exception" Name="exception" /></ActivityAction.Argument>'
            + mark('[&quot;CATCH of Try Catch gets &quot; &amp; Reparo.GehOrder.Trace.Id(exception)]') +
            '</ActivityAction></Catch></TryCatch.Catches></TryCatch>')
def scope(name, body, handle=True):
    return f'<g:FaultScope DisplayName="{name}" Handle="{handle}">' + body + '</g:FaultScope>'
def after(what): return mark(f'after {what}')

CASES = {
    # 1. plain UiPath: Click in a Sequence in Try Catch's Try (what Studio makes)
    "tc": seq("scenario tc", trycatch(seq("Try body", click(), mark("after the Click, in Try body"))), after("Try Catch")),
    # 1b. the Click straight in Try, no Sequence between
    "tcdirect": seq("scenario tcdirect", trycatch(click()), after("Try Catch")),
    # 2. a container with a FaultCallback that handles the fault; the Click in a Sequence in it (an Event's body)
    "fs": seq("scenario fs", scope("Fault scope", seq("Scope body", click(), mark("after the Click, in Scope body"))), after("Fault scope")),
    # 2b. the Click straight in the container
    "fsdirect": seq("scenario fsdirect", scope("Fault scope", click()), after("Fault scope")),
    # 2c. the container's FaultCallback does not handle: does the GEH run again further up?
    "fsrethrow": seq("scenario fsrethrow", seq("Outer", scope("Fault scope", seq("Scope body", click()), handle=False), after("Fault scope, in Outer")), after("Outer")),
    # 2d. a handling container above a non-handling one, so the run survives to show what's above
    "fsnested": seq("scenario fsnested", scope("Outer scope", seq("Between", scope("Inner scope", seq("Scope body", click()), handle=False), after("Inner scope"))), after("Outer scope")),
    # 3. no handler: how often does the GEH run for one fault?
    "nested": seq("scenario nested", seq("L1", seq("L2", seq("L3", click()), after("L3")), after("L2")), after("L1")),
}

body = ('<Sequence DisplayName="Main Sequence"><Sequence.Variables><Variable x:TypeArguments="x:String" Name="scenario" /></Sequence.Variables>'
        '<Assign DisplayName="Read scenario"><Assign.To><OutArgument x:TypeArguments="x:String">[scenario]</OutArgument></Assign.To>'
        '<Assign.Value><InArgument x:TypeArguments="x:String">[System.IO.File.ReadAllText("C:\\Users\\reparo\\UiPath\\reparo\\geh47\\scenario.txt").Trim()]</InArgument></Assign.Value></Assign>'
        + mark('[&quot;=== Main starts scenario &quot; &amp; scenario]') +
        '<Switch x:TypeArguments="x:String" DisplayName="Switch scenario" Expression="[scenario]">'
        + "".join(c.replace('<Sequence DisplayName="scenario', f'<Sequence x:Key="{k}" DisplayName="scenario', 1) for k, c in CASES.items())
        + '</Switch>' + mark('=== Main ends') + '</Sequence>')
print(HEAD.replace("<!--BODY-->", body))
