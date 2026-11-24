# RESEARCH: when UiPath's Global Exception Handler runs (ReparoHome#47)

Branch `research/uipath-geh-order`, based on `prototype/process-shape`, never merged. Studio/Robot 25.10.19 on reparo-win,
Windows project, net8.0, 2026-11-24. Every run was `uip rpa pack`, then `UiRobot execute --file` on the console desktop.

## Why the GEH never ran in #46

The executor reads the handler from **`runtimeOptions.exceptionHandlerWorkflow`** in project.json
(`UiPath.Project.ProjectData.RuntimeOptions.ExceptionHandlerWorkflow`, read by `UiPath.Executor.Tracking.WorkflowTracking`).
The #46 prototype set the key at the top level and under `designOptions`, so the executor never saw it. With the key under
`runtimeOptions`, `uip rpa pack` keeps it, compiles `GlobalHandler.xaml` into the package DLL, and the Robot runs it. No
Studio UI, licence or governance gate was involved (`geh-order/package.txt`).

## How it works (from UiPath.Executor.Core.dll, decompiled with ilspycmd on the VM)

- `ExceptionTracking` is a tracking participant on every `ActivityScheduledRecord`. When a parent schedules a child
  **without a FaultCallback**, the participant gives the child its own fault bookmark (`FaultHandler.Install`, owned by the
  parent). When the parent passes a FaultCallback (`ScheduleActivity(child, onCompleted, onFaulted)`, as Try Catch does with
  its Try), the child already has a fault bookmark, and UiPath leaves it alone.
- WF hands a fault to the nearest fault bookmark up the tree. A UiPath bookmark runs the GEH with that child as
  `errorInfo.ActivityInfo`, then acts on `result`:
  - `Continue` (the template's "rethrow"): the fault is not handled, so it propagates to the next bookmark up.
  - `Ignore`: `HandleFault`, and the child counts as completed, so its parent goes on with the next activity.
  - `Retry`: `HandleFault`, and the same child is scheduled again. `RetryCount` is kept per activity id and reset when
    anything other than Retry is returned.
  - `Abort`: `Abort` on the whole workflow instance. No further fault callback runs.
- Studio's Debug path (`UiPath.Executor.Debugger.dll`, `DebugTrackingParticipant`) installs the same bookmarks. The debugger
  gets each fault first, and the GEH runs if the debugger answers Continue. This is from the code only; Debug was not run.

## Findings

The probe project is `GehOrder/Project` (`gen.py` writes Main.xaml, one Switch case per scenario). `GehOrder/Bridge` is a
package with `Mark` (trace line), `FaultScope` (schedules its Body with a FaultCallback; `Handle` decides whether it calls
`HandleFault`), and `FaultTracker` (logs WF's `FaultPropagationRecord`s). Every Click targets a window that doesn't exist
(`SelectorNotFoundException`, 1.5 s timeout). The GEH logs each call with the exception object's identity (`@hash`), the
activity and the `RetryCount`. It answers from `geh47\geh-mode.txt`; `retry` means retry once per activity, then Continue.
Logs: `geh-order/<scenario>-<mode>.log`.

### 1. Plain UiPath: Click inside Try Catch: VERIFIED

Try Catch > Sequence "Try body" > Click, the shape Studio makes:
- **The GEH runs before the Catch, and it runs even though the Try Catch catches.** It runs once, for the Click. It doesn't
  run for "Try body": Try Catch scheduled that with its own FaultCallback (`tc-continue.log`).
- `Continue`: the Catch gets the same exception object, and the run goes on after the Try Catch (`tc-continue.log`).
- `Ignore`: the Catch never runs. "Try body" goes on after the Click as if the Click had succeeded (`tc-ignore.log`).
- `Retry`: the Click runs again. When it fails again, the GEH runs again (RetryCount=1, Continue), and only then does the
  Catch get the new exception (`tc-retry.log`).
- `Abort`: the Catch never runs, nothing after the Try Catch runs, and the job ends faulted, UiRobot exit 1 (`tc-abort.log`).
- With the Click straight in Try (no Sequence), **the GEH doesn't run at all** and the Catch gets the fault
  (`tcdirect-continue.log`).

### 2. A container with a FaultCallback (a Reparo Process stand-in): VERIFIED

`FaultScope` with Handle=true > Sequence "Scope body" > Click, the shape of a Reparo Event's body:
- **The GEH runs before the FaultCallback**, once, for the Click (`fs-continue.log`).
- `Continue` (rethrow): the FaultCallback gets the same exception object, handles it, and the run goes on (`fs-continue.log`).
- `Ignore`: **the FaultCallback never sees the fault.** "Scope body" goes on after the Click (`fs-ignore.log`).
- `Retry`: the Click runs again. The FaultCallback gets only the failure after the GEH's last Continue, as a new exception
  object (`fs-retry.log`). If the Click had then succeeded, the FaultCallback would have seen nothing.
- `Abort`: **the FaultCallback never runs**, and the job ends faulted, exit 1 (`fs-abort.log`).
- With the Click straight in the container's Body, the GEH doesn't run, and the FaultCallback gets the fault first and
  alone (`fsdirect-continue.log`).
- When the FaultCallback doesn't handle the fault, **the GEH runs again further up**. It runs once for the container itself
  ('Fault scope'), then once per level above it, up to 'Main Sequence', and the job faults (`fsrethrow-continue.log`).
  With a handling container above it, the GEH runs once between the two FaultCallbacks, for the inner container, and then
  the outer FaultCallback handles the fault (`fsnested-continue.log`).

### 3. Once per activity in the call stack, not once per fault: VERIFIED, with one exception

L1 > L2 > L3 > Click with no handler: one fault (one exception object) ran the GEH **7 times**. It ran for Click, L3, L2,
L1, the scenario Sequence, the Switch and Main Sequence; the root Main has no parent, so it gets no call (`nested-continue.log`).
The exception: **a level scheduled with a FaultCallback gets no GEH call** ('Try body' in 1, 'Scope body' in 2, the direct
children in `tcdirect` and `fsdirect`).

Retry compounds across levels. With "retry once per activity", one level's Retry re-runs everything below it, and the
counts below were reset, so the Click ran 128 times (2^7 levels) and the GEH 254 times before the job faulted, after 227 s
(`nested-retry.log`). `Ignore` at the Click stops it at once: the levels above go on normally (`nested-ignore.log`).

## What this means for which failures reach Reparo (input for #47, not decided here)

- An author's GEH runs **before** the Bridge's FaultCallback for every UiPath activity that isn't a direct child of a
  Bridge block that schedules it with a FaultCallback. It can then take the UI failure away from Reparo: `Ignore` and
  `Abort` always do, and `Retry` does whenever the retry succeeds.
- In the #46 prototype only Reparo Process schedules with a FaultCallback (its Reparo Activities). Reparo Activity, Event,
  If and For Each schedule without one. So the GEH runs at the Click, at each Sequence in the Event, at the Event and at
  the blocks between it and the Reparo Activity, all before Reparo Process sees the fault.
- If the GEH answers `Continue` at every level, Reparo still gets the same exception, just later. The GEH's side effects
  (logs, screenshots, closed apps) then happen before the Recovery.
- When Reparo doesn't handle a fault (not a UI failure, or too many Recoveries), the GEH runs again from Reparo Process
  upward, so the author's handler still sees what Reparo couldn't recover.
