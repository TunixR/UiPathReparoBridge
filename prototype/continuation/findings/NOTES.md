# PROTOTYPE notes: Continuation in UiPath (ReparoHome#48)

Throwaway branch `prototype/continuation`, never merged. It builds on `prototype/process-shape` (ReparoHome#46):
the Bridge's own blocks, Reparo Process › Activity › Event plus Reparo If and Reparo For Each.

## Decided with the owner (2026-11-24)
- A rejected Continuation or a failed Recovery **rethrows the original UI exception**, the same object, so the
  author's handlers outside Reparo Process see it as if Reparo weren't there.
- **In-run Continuation only.** No cold start, so `continue_from`, `continue_passes` and `continue_variables` are gone.
- **Variable types: as many as possible.** See "Variables" below.
- `uip rpa debug start-from-here`: ruled out. It's a debugger command, production runs never use the debugger,
  and the Bridge's blocks continue without it.

## Verified on reparo-win (Studio/Robot 25.10.19, Windows project, net8.0; package 0.0.1-proto.48)
Process: `Process/gen.py spending`, the spending approval from #46, with:
- a nested Reparo For Each `each_field` (over `fields`) inside `each_request`;
- a DataTable `decisions` that gets one row per decision;
- Variables of several types;
- the author's own Try Catch around Reparo Process.

SpendApp `--late-approve 105` makes Click Approve fail at pass [2]. A fake Reparo answers from
`scenarios/<name>.json`. Logs are in `findings/<name>.log`.

| Scenario | Answer | Result |
| --- | --- | --- |
| retry | the failed Event, [2] | continues; final decisions and budget 50 as without the failure |
| go-back | `ui_command: back`, an Event of an earlier Activity (Open the request), [2] | continues there, same result |
| nested | Read a field, [2, 1], plus a DataTable row added by Reparo | enters the inner loop's pass 1; the row is kept, with its column types |
| reject-unknown-event | `Other.xaml#ReparoEvent_3` (an invoked workflow's id) | rejected: not an Event of Main.xaml |
| reject-pass-count | Read a field with [2] | rejected: 1 pass given, 2 loops enclose it |
| reject-pass-range | Click Approve, [7] | rejected: `pending` has 5 items |
| reject-variable-type | `amount: "lots"` | rejected: not an Int32 |
| recovery-failed | `success: false` | no Continuation |
| process-finished | `process_finished: true` | Reparo Process ends without running more Events; outcome `completed` |
| card-*-after/before, card-always-attach | (CardProbe) a second UI failure after the Continuation | not handed to Reparo; propagates; outcome `continuation_failed` |

In every rejection and in recovery-failed, the author's outer Try Catch caught `UiPath.Core.SelectorNotFoundException`
with `ReferenceEquals(ex, rethrown) = True`. The stack top is the original Click's.

- **The Recovery waits in an async child activity.** `ReparoRecoveryWait` is an `AsyncCodeActivity`, an implementation
  child of Reparo Process. The FaultCallback runs on the workflow thread (1). It calls `HandleFault`, writes the
  payload and schedules the wait. The fake Reparo runs on a pool thread, and `EndExecute` comes back on thread 1.
  Nothing blocks the workflow thread. UNVERIFIED: an Orchestrator Stop during the wait reaching `Cancel` (the real
  Bridge sends `abort` there). There was no Orchestrator to test it.
- **The faulted branch ends by itself right after `HandleFault`.** It ends `Closed`, within about 20 ms and before the
  wait starts. `CancelChild` beforehand only changes that to `Canceled`. Either way the Bridge can't hold the branch
  open while Reparo works, and the order (cancel before or after the wait) makes no difference.
- **UiPath's Use Application/Browser (NApplicationCard) has its own fault handler.** It runs before Reparo
  Process's FaultCallback (`TRACK fault ... handler='Use SpendApp'`), and it may close the app:
  - **The card attaches to an app an earlier Event opened, CloseMode default (IfOpenedByAppBrowser):** the app
    stays open through the fault, the wait and the Continuation, and the run completes. `card-default-attach`.
  - **The card opened the app itself (any CloseMode), or CloseMode is Always:** the card closes the app (about
    380 ms) before rethrowing. Reparo then sees a desktop without the app, and the Continuation reopens it fresh.
    `card-default-after`, `card-*-before`, `card-always-*`.
  - So an Event's card must only attach. Open the app in an earlier Event, and don't set CloseMode Always inside an
    Event. This is the same as without Reparo: UiPath closes the app before any Try Catch sees the failure.
- **One Recovery per run.** A UI failure after the Continuation is never handed over; the outcome is `continuation_failed`.
- **Reparo If** enters the arm that holds the target and logs a warning when its condition picks the other arm.

## Variables (`TypesProbe/`, `findings/types-probe.txt`; same Newtonsoft 13 as UiPath)
Value → JSON text → declared type → JSON text, compared, with the Bridge's parse settings
(`FloatParseHandling.Decimal`, `DateParseHandling.None`):
- **Round-trip exactly:**
  - scalars: String, Int32, Int64, Double (NaN too), Decimal, Boolean, Char;
  - dates and times: DateTime (all kinds), DateTimeOffset, TimeSpan;
  - other value types: Guid, Nullable, Enum, Uri, Version, Byte[];
  - arrays, List, HashSet, Queue, and Dictionary with String or Int32 keys;
  - KeyValuePair, Tuple, ValueTuple, JObject, JArray, POCO classes, NetworkCredential.
- **Default parse settings lose data:** Decimal goes through Double, and DateTimeOffset loses its offset. Use the
  settings above.
- **DataTable:** plain Newtonsoft infers column types from the JSON (Int32 becomes Int64, Decimal becomes Double, and
  an empty table loses its columns). The Bridge's path keeps them: it builds the table from the live value's columns
  and fills the rows from the JSON. Verified in the run (`restored decisions = DataTable[3 rows; request_id:Int32, ...]`).
- **Lossy without an error, so the check can't trust "no exception":**
  - SecureString becomes `{"Length":n}` and comes back empty;
  - classes with private state lose that state;
  - Exception loses its type;
  - a Variable declared Object comes back as Int64 or JArray.
- **Fail outright:** DataRow, MailMessage, Image, Process. Live handles (UiElement, Browser) are the same kind.
- **Prototype rule:** at every Event start the Bridge checks each Variable's round trip.
  - **Carried:** what survives goes in the payload, and Reparo's answer overrides it key by key.
  - **Not carried:** what doesn't survive (here `contact: SecureString`) is never sent. A Continuation gives it back
    its live value from that Event's start. It is the same reference, so an in-place change made by the failed
    Event isn't undone.
  - A name Reparo returns that was never sent is ignored, with a warning.

## VM gotchas (this session)
- SSH on :2222 kept resetting (passt), so everything went through the qemu guest agent: `vm/gexec.sh` runs as
  SYSTEM, and `vm/put.sh` and `vm/get.sh` copy files. Console runs use `console.ps1` (`schtasks /RU reparo /IT`)
  through `vm/go48.sh`.
- dotnet as SYSTEM: use reparo's NuGet cache as the only source (`vm/build-env.ps1`). The SDK (10.0.404) has no
  offline net8 targeting pack, so the TypesProbe targets net6.0-windows with `RollForward=Major`.
- `x:String[]` isn't a XAML type for a Variable; use `s:String[]` (`clr-namespace:System`).
- Classic selectors find SpendApp's hidden list buttons, so a Continuation at "Open the request" while on the
  detail page clicks through silently (`continuation-wrong-screen.log`). It's a SpendApp artefact, the same gotcha as #46.
- The spending `continuation-failed` retry (1 s) came back just in time and completed. The CardProbe runs show the
  second-failure path instead.
