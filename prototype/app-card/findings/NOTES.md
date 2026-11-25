# PROTOTYPE notes: Use Application/Browser closes the app before Reparo sees the failure (ReparoHome#66)

Throwaway branch `prototype/app-card`, never merged. It builds on `prototype/continuation` (ReparoHome#48).
The question: UiPath's Use Application/Browser card (`NApplicationCard`) has its own fault handler. It closes the app
before Reparo Process's FaultCallback runs, so Reparo recovers on a desktop without the app. Can the Bridge see the
failure while the app is still open?

## Decided with the owner (2026-11-25)
- **The Recovery runs in Reparo Event's FaultCallback, not Reparo Process's.** In WF the innermost handler runs first.
  When the card holds the Event (card › Reparo Activity › Reparo Event › UI activity), Reparo Event sees the failure
  before the card's handler does, so the app is still open. Reparo Process keeps only what's left: it marks a UI
  failure that reaches it after the Recovery as `continuation_failed`.
- **Continue by completion.** If the Continuation names this Event, the Event runs its body again (retry in place).
  If it names another Event, this Event completes. Each Bridge block above it completes too, until one holds the
  target and enters it. A UiPath scope in between, such as the card, completes normally. No signal exception is used.
- **Rethrow from the Event.** A rejected Continuation or a failed Recovery rethrows the original UI exception, the
  same object, from Reparo Event. The card then closes the app, as it does without Reparo, and the author's
  outer Try Catch sees the original (`ReferenceEquals = True`).
- **A card inside a Reparo Event: guide plus a Studio validation warning.** The warning applies unless the card only
  attaches (OpenMode Never and CloseMode not Always). The guide says to put the card around the Reparo Activity,
  or to open the app in an earlier Event.
- **Known limitation, no more work:** a back jump to an earlier Reparo Activity in the same card leaves the card. The
  card closes the app, if it opened it or CloseMode is Always, and reopens it on the way back in. The state Reparo
  fixed is lost.

## Rejected
- **A TrackingParticipant that holds the workflow thread.** `Track` runs synchronously before the card's handler,
  and a 3 s hold delayed the close by 3 s (`inside-default-Event-retry-hold3000`). But the card closes the app right
  after the hold, and the Recovery can't run inside `Track`.
- **Overriding the card's CloseMode to Never.** The override can't be made at run time: `InvalidOperationException:
  The argument of type NAppCloseMode cannot be used. Make sure that it is declared on an activity`
  (`inside-always-Event-retry-overrideNever`, an earlier build). Reparo Process's `CacheMetadata` can set it before the
  children are prepared, and that works (`inside-default-Event-retry-overrideNever`). But it changes what the author
  wrote: the app stays open after a normal end.
- **Forbidding a card inside an Event.** Too strict: a card that only attaches is safe there.

## Verified on reparo-win (Studio/Robot 25.10.19, package 0.0.1-proto.66.2)
`Process/gen.py header.xaml <shape> <close> <Event|Process> [override] [holdMs]` generates Main.xaml. The shapes:
- **holds:** card(SpendApp) › [Activity "Read the request" (Read the title `_1`), Activity "Approve the request"
  (Read the amount `_2`, Click Approve `_3`), Activity "Check the status" (Read the status `_4`)], then
  Activity "After the card" (Note the end `_5`).
- **inside:** Activity › Event Click Approve `_1` › card › Click, then Note the end `_2`.
- **browser-\***: the same with a Use Browser card (Edge, `DebuggerApi`) on `web/spend.html`.

The author's own Try Catch wraps Reparo Process. SpendApp fails Click Approve on request 104, and the page adds the
button 6 s after load. A fake Reparo answers from `scenarios/<name>.json`. Logs: `findings/<shape>-<close>-<at>-<scenario>.log`;
the first screenshot Reparo would get: `findings/shots/`.

| Run | Answer | App while Reparo works | Result |
| --- | --- | --- | --- |
| holds, default, Event | retry | open | completes |
| holds, Always, Event | retry | open | completes |
| browser-holds, default, Event | retry | open (Edge) | completes |
| holds, default, Event | back, same Activity (`_2`) | open | stays in the card, completes |
| holds, default, Event | back, earlier Activity (`_1`) | open | leaves the card, which closes and reopens the app; second failure, `continuation_failed` |
| holds, default, Event | forward in the card (`_4`) | open | completes |
| holds, default, Event | out of the card (`_5`) | open | card completes normally, then `_5`; completes |
| holds, default, Event | rejected (`Other.xaml#ReparoEvent_3`) | open | original rethrown; card closes the app; outer Try Catch catches the same object |
| holds, default, Event | `process_finished` | open | everything completes |
| holds, default, **Process** (control) | retry | **closed** | app reopened fresh; second failure, `continuation_failed` |
| inside, default, Event (control) | retry | **closed** | same |
| browser-inside, default, Event (control) | retry | **closed** (Edge) | same |
| inside, default, Event, 3 s Track hold | retry | open during the hold, closed after | same |
| inside, default, Event, CloseMode → Never | retry | open | completes; rejected, see above |

The card closes the app in about 370 to 600 ms once its handler runs.

## Gotchas
- **Use Browser needs the UiPath extension.** It was installed by machine policy on the VM:
  `SetupExtensions.exe /edge-chromium-policyglobal /silent`, which adds HKLM Edge policies (ExtensionInstallForcelist,
  the NativeMessaging allowlist, DevTools). Edge must restart to load it, so `vm/run66.ps1` kills msedge.
- **The extension can't open `file://`** (`BrowserFailedToNavigateToUrlException`). `vm/serve.ps1` serves `web/` on
  `http://localhost:8066/`.
- **NuGet restore failed on a locked DLL** after the first build, so the version went to `0.0.1-proto.66.2`.
- `Error: write ECANCELED` at the end of some logs comes from the `uip` CLI after the run ended. It's harmless.
