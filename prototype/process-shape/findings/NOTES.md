# PROTOTYPE notes: How a Process is built in UiPath (ReparoHome#46)

Throwaway branch `prototype/process-shape`, never merged.

## Decided with the owner (2026-11-24)
- Shape 1, own blocks: Reparo Process > Reparo Activity > Reparo Event, plus Reparo If and Reparo For Each
  between Events. UiPath activities only inside Events. Variables are UiPath variables declared on Reparo Process.
- Every UiPath activity sits inside an Event (no plain steps between Events), as in the Playwright Bridge.
- Event id = workflow file + IdRef, with an author-given name as the fallback if IdRef can't be read at runtime.
- One XAML file per Process for now. An invoked workflow runs whole, inside one Event. Spanning files goes to the fog.

## Verified on reparo-win (Studio/Robot 25.10.19, Windows project, net8.0)
- The `net6.0-windows` package (`Reparo.UiPathBridge.Activities`, CoreWF `UiPath.Workflow` 6.0.0-20240401-07,
  PrivateAssets) installs from Studio's local feed (`%LOCALAPPDATA%\Programs\UiPath\Studio\Packages`) and runs
  on .NET 8.0.33.
- UiPath's executor KEEPS a TrackingParticipant registered by an activity through
  `metadata.AddDefaultExtensionProvider(() => new ConcreteTracker())`: start/close records for every activity,
  plus WorkflowInstance/Scheduled records. `context.GetExtension<ConcreteTracker>()` returns it.
- IdRef IS readable at runtime. Walk up the internal `Activity.Parent` (reflection) to the compiled root
  (`<Project>.Main`, in `.local\install\<Project>_Main.dll`). Its `IdRefDictionary`
  (`Dictionary<object,string>`) maps each activity object to its IdRef (`ReparoProbe_1`, `Sequence_2`, ...).
  `ConstructorIdRefDictionary` holds the root's body. Nothing is attached to the activity itself (0 props).
- WF's runtime `Activity.Id` is positional (1.3, 1.4, 1.5), so it's not usable as an Event id, as expected.
- A published Windows package (smoke nupkg) ships `content/*.xaml` AND `lib/net8.0-windows7.0/*.dll`. The DLL
  contains `IdRefDictionary`. This contradicts #40 ("DLLs only").

## Results on spending approval (SpendApp, the Playwright example's "full" case: budget 1000, five requests)
The Process: 13 Events in 7 Activities, one Reparo For Each over `pending`, two nested Reparo Ifs (`Process/gen.py`).
- **A, in-run Continuation:** works. SpendApp `--late-approve 105` removes Approve for 6 s, so Click Approve #2 raises
  `SelectorNotFoundException`. The fault walks up through each Bridge block to Reparo Process with no handler before
  it (`TRACK fault ... handler=` lines). Its FaultCallback calls `HandleFault`, writes `recovery-1.json` (task_name,
  errored_act with model_event_id `Main.xaml#ReparoEvent_5`, loop_iteration [2], variables, ui_log), takes the fake
  Reparo's answer, cancels the child, and re-enters at Event 5, pass [2], with the Variables restored. Reparo If
  'decide' takes the arm that holds the target without evaluating its condition. Final budget 50, as without the
  failure. `scenario-a-continuation.log`, `scenario-a-recovery-1.json`.
- **B, cold start:** works. `continue_from=Main.xaml#ReparoEvent_9`, `continue_passes=3` and the Variables as JSON start
  a new run at "Open the Reason dropdown" in "Reject for budget", pass 3, and finish. `scenario-b-cold-start.log`.
- **C3, an author Try Catch inside an Event:** it wins. It catches the `SelectorNotFoundException`, the Bridge never
  sees it, and no Recovery request is written. The UI log still lists the Event as run. `scenario-c3-trycatch.log`.
- **C4, an author Retry Scope inside an Event:** it wins. Its own TryCatch catches the first two failures, the third
  try clicks Approve once it's back, and no Recovery request is written. Final budget 50. `scenario-c4-retryscope.log`.
- **A Try Catch around Reparo Process:** not run. By construction it never sees a UI failure inside an Event: the
  Process's FaultCallback handles it first, so the author's outer handlers see only what Reparo couldn't recover.
- **C1/C2, the Global Exception Handler: UNVERIFIED.** It never ran, even in GehProbe, which has no Reparo blocks:
  - `uip rpa run` builds only the files Main.xaml reaches, so the handler file isn't even built.
  - `uip rpa pack` drops `exceptionHandlerWorkflow` from the packaged project.json, whether it's set at the top
    level, under `designOptions`, or as `ExceptionHandlerWorkflow` in `project.uiproj`.
  - Patching the key into the package's project.json and running it with `UiRobot execute --file` still doesn't run
    the handler (the exit code changes from 0 to 1).
  - Studio's contracts carry `CanCustomizeGlobalHandler` / `DefaultOnlyGlobalHandler` (a governance or licence gate),
    and the `uip` login here is "Refresh Failed". Settling this needs a Studio-made project or an Orchestrator job:
    #47's work.
- **E, retrofit:** `Process/Native/Main.xaml` is the same Process in plain UiPath (Sequence, If, ForEach) and runs to
  the same decisions and a final budget of 50. Going to Reparo blocks leaves all 22 UiPath activities and their 13
  Targets byte-identical. It changes only the containers: root Sequence -> Reparo Process (Variables move with it,
  plus 3 continue_* arguments), 7 Sequences -> Reparo Activity, 13 step Sequences each wrapped in a Reparo Event,
  2 If -> Reparo If, 1 ForEach<Int32> -> Reparo For Each, whose item moves from a delegate argument to a Process
  Variable so a Continuation can restore it. 77 of about 170 lines change. `retrofit-diff.txt`.
- **F, .NET 10:** a project with `dotNetVersion: "net10.0"` (Helm also takes `Net10`, and refuses `10.0`, `10` and
  `8.0`) packs to `lib/net10.0-windows7.0` against the `net6.0-windows` Bridge package. Not run: Robot 25.10 can't run
  .NET 10. `net10-pack.txt`.

## VM gotchas
- `uip rpa init` and `uip rpa run` straight from SSH fail: "Helm requires a signed-in user". Run them through the
  console helper (`vm/go.sh <Project> console`). `init` wasn't retried from the console. Copy the smoke project instead.
- After a successful run or pack, `uip` never returns under the console helper. `run.ps1` and `packrun.ps1` wait for
  the output and kill it.
- `UiRobot execute --file` runs only packages, and reuses an installed package of the same name and version: copies
  of one project all packed as SmokeNotepad 1.0.0 and ran the old smoke project. The package name comes from
  `project.uiproj`'s `Name`, not project.json (`vm/rename.ps1`).
- Classic selectors find hidden WinForms controls, so SpendApp removes Approve instead of hiding it.
- Stop the backend's Docker stack before starting the VM: together they froze the host on 2026-11-24.
