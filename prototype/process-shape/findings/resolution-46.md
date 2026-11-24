## Resolution

**The Bridge brings its own blocks (option b).** A UiPath Process for Reparo is built like this:
- **Reparo Process** holds the Process's Variables.
  - **Reparo Activity**
    - **Reparo Event**: its body holds the UiPath activities.
  - **Reparo If** and **Reparo For Each** sit between Activities, Events or each other.

Every UiPath activity sits inside a Reparo Event, as in the Playwright Bridge. Nothing runs between Events except the Bridge's own blocks. The prototype runs this on the VM with the spending-approval example rebuilt as a WinForms app. It is the Playwright example's "full" case: 13 Events, a For Each over the pending requests, and two nested Ifs.

### Decisions (with the owner)

- **Shape:** the Bridge's own blocks, as above. The Bridge validates the shape: a UiPath activity placed outside a Reparo Event is a validation error in Studio ("Put UiPath activities inside a Reparo Event").
- **Control flow:** Reparo If (exclusive gateway) and Reparo For Each, which matches the parity checklist. While, Do While, Flowchart, State Machine, Switch and Parallel aren't Bridge blocks. Inside an Event, any UiPath control flow runs whole as part of that one Event.
- **Variables:** UiPath variables declared on Reparo Process are the Process's Variables. Only they pass between Events, and the Bridge reads and writes them by name. Temporaries declared inside an Event's body stay there. Reparo For Each names its list and its item as Process Variables, so a Continuation can restore the item.
- **Event id:** `<workflow file>#<IdRef>`, for example `Main.xaml#ReparoEvent_5`. A name the author gives is the fallback.
- **Files:** one XAML file per Process for now. An invoked workflow runs whole, inside one Event. Processes that span files go to the fog.

### Verified on the VM

All runs used Studio/Robot 25.10.19, a Windows project and .NET 8.

- **The package:** `Reparo.UiPathBridge.Activities`, built for `net6.0-windows` on CoreWF `UiPath.Workflow`, installs and runs in a .NET 8 project. A .NET 10 project packs against it (`dotNetVersion: "net10.0"`), but it isn't run: .NET 10 needs Robot 26.10.
- **IdRef at runtime:** the compiled root activity's `IdRefDictionary` maps each activity object to its IdRef. The Bridge finds the root by walking up the activity tree.
- **The XAML ships:** a published Windows package holds `content/*.xaml` beside the DLLs. This corrects [Extension points in a UiPath run](https://github.com/TunixR/ReparoHome/issues/40), which said "DLLs only".
- **The TrackingParticipant:** one that an activity registers through `AddDefaultExtensionProvider` stays registered under UiPath's executor. It sees every UiPath activity's state changes and every fault's path.
- **Continuation in the same run:** works. A UI failure in Event 5 at loop pass [2] reaches Reparo Process's FaultCallback, after passing up through each Bridge block with no handler before it. The Bridge writes the Recovery request: Event id, loop pass, Variables and UI log. A fake Reparo answers "continue at Event 5, pass [2]". The Bridge cancels the faulted branch and enters again at that Event and pass, with the Variables restored. Reparo If takes the arm that holds the target without evaluating its condition again. The run ends with the same final budget as a run with no failure.
- **Continuation from a cold start:** works. Input arguments name the Event, loop pass and Variables, and a new run starts there.
- **An author's handlers win:**
  - A Try Catch inside an Event catches the UI failure, and the Bridge never sees it.
  - A Retry Scope inside an Event retries until Approve is back, and the Bridge never sees the failure.
  - A Try Catch around Reparo Process never sees a UI failure inside an Event, because the Process handles it first.
  - One side effect: the UI log lists an Event as done even when an author's Catch swallowed its failure.
- **Retrofit:** the same Process in plain UiPath (Sequence, If, ForEach) and in Reparo blocks have byte-identical UiPath activities and Targets: 22 activities and 13 Targets. Only the containers change:
  - The root Sequence becomes Reparo Process.
  - 7 grouping Sequences become Reparo Activities.
  - Each of the 13 step Sequences is wrapped in a Reparo Event.
  - 2 Ifs and 1 ForEach become Reparo If and Reparo For Each. The ForEach's item becomes a Process Variable.

  77 of about 170 lines change. The author also has to pick the Event boundaries. Here each step was already a Sequence.

### Not verified

**The Global Exception Handler's order:** the GEH never ran, not even in a plain workflow with no Bridge blocks.
- `uip rpa run` doesn't build it.
- `uip rpa pack` drops `exceptionHandlerWorkflow` from the package.
- Patching the key into the package didn't make the Robot run it either.
- Studio has a governance gate, `CanCustomizeGlobalHandler`.

Settling it needs a project made in Studio or an Orchestrator job. That's input for [Which UiPath failures reach Reparo, and which are UI failures](https://github.com/TunixR/ReparoHome/issues/47).

### Assets

- The prototype: [`prototype/process-shape`](https://github.com/TunixR/UiPathReparoBridge/tree/prototype/process-shape/prototype/process-shape), a throwaway branch with no PR.
- The full notes: [`findings/NOTES.md`](https://github.com/TunixR/UiPathReparoBridge/blob/prototype/process-shape/prototype/process-shape/findings/NOTES.md), with the logs of each scenario beside them.
- The blocks: [`Bridge/Blocks.cs`](https://github.com/TunixR/UiPathReparoBridge/blob/prototype/process-shape/prototype/process-shape/Bridge/Blocks.cs).
- The generator for both XAMLs: [`Process/gen.py`](https://github.com/TunixR/UiPathReparoBridge/blob/prototype/process-shape/prototype/process-shape/Process/gen.py).
