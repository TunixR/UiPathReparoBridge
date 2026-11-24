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

## VM gotchas
- `uip rpa init` and `uip rpa run` straight from SSH fail: "Helm requires a signed-in user". Run them through the
  console helper (`vm/go.sh <Project> console`). `init` wasn't retried from the console. Copy the smoke project instead.
- After a successful run, `uip rpa run` never returns under the console helper (it timed out at 400 s). Read
  `proto46\trace.log` directly.
- Stop the backend's Docker stack before starting the VM: together they froze the host on 2026-11-24.

## Next (not done yet)
1. A desktop spending-approval app (WinForms) on the VM.
2. The Bridge blocks (Process/Activity/Event/If/ForEach, NativeActivity), with the Variables read and written
   through DataContext by name, and RunState as an execution property.
3. A forced UI failure (Click with a short timeout on a button that's late to appear) -> the Process's FaultCallback
   -> simulated Reparo `done` -> Continuation at Event + loop pass in the same run. Plus a cold start at a named
   Event and pass.
4. Handler order: Try Catch / Retry Scope inside an Event, a GEH, a Try Catch around the Process.
5. Retrofit diff: plain UiPath spending approval vs the Reparo-block version.
6. .NET 10 project: pack only (Robot 25.10 can't run net10).
