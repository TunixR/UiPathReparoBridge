# PROTOTYPE notes: Continuation inside a Use Application card (ReparoHome#67)

Throwaway branch `prototype/card-continuation`, never merged. It builds on `prototype/app-card` (ReparoHome#66), whose
Recovery runs in Reparo Event's FaultCallback. The question: can Bridge blocks sit inside a Use Application/Browser
card (`NApplicationCard`) and still continue a run, with Modern UI activities?

## Answer
Yes. Every UI action here is a Modern Click (`NClick`) inside a Reparo Event, inside a Reparo Activity, inside a card.
It takes its app from the card (`ScopeIdentifier`), with a literal selector or one built from a Variable.

A Continuation completes upward from the failed Event to the innermost Bridge block that holds the target Event at
the target's loop passes, and that block enters it. A card on the way up just completes. So the card runs again from
its top only when that block is outside the card:
- **Normal flow, not a limitation:** the target is before the card, or in an earlier pass of a Reparo For Each
  around the card. The card would start again there anyway.
- **The known limitation:** a back jump to an earlier Reparo Activity in the same card, or from an inner card to an
  outer card's Activity. The card runs again when it normally wouldn't. If it opened its app, it closes the app and
  opens it again, and what Reparo fixed is lost. If it only attaches to an app an earlier Event opened, nothing is
  closed (same PID).
- No rerun: a retry, an earlier Event in the same Reparo Activity, a Reparo For Each inside the card, a forward jump
  inside the card, or a target after the card.

Neither fallback (a card inside each Event, Input elements passed between Events) is needed.

## Decided with the owner (2026-11-25)
- **Keep the shape.** The rerun on a back jump is a known limitation. The guide says to open the app in an earlier
  Event, so the card only attaches. It doesn't need a Studio warning.
- **A For Each around a card isn't a limitation.** The card opens and closes on every pass anyway.
- **Resize window: a guide note only.** `WindowResize` acts again each time a card starts. A Continuation that reruns
  the card undoes a window change Reparo made; a retry or a jump inside the card keeps it. The guide suggests None
  on cards that hold Bridge blocks.

## Prototype bug found and fixed: loop passes in `HoldsTarget`
Since #66 moved the Recovery into Reparo Event, `HoldsTarget` matched the Event id only. A jump to an earlier pass
of the same loop re-entered the nearest Reparo Activity at the current pass (`*-before-fix.log`: "Continuation reached
`ReparoEvent_1 #0`", then "Event ... #1 starts"). Fixed in `0.0.1-proto.67.2`: a block at loop depth d holds the
target only if it holds the Event and the target's passes for its d enclosing loops are the ones running now. A
Reparo For Each at depth d can then switch its own pass.

## Verified on reparo-win (Studio/Robot 25.10.19, UIAutomation 25.10.43, package 0.0.1-proto.67.2)
`Process/gen.py header.xaml <shape> <resize>` generates Main.xaml; `vm/run.sh <shape> <resize> <scenario>` runs it,
`vm/batch.sh` runs the list. Shapes:
- **holds:** card (opens SpendApp) › [Open the request › Click Open request 104 `_1`, Approve the request › Click
  Approve `_2`, Go back › Click Back `_3`], then After the card › Note the end `_4`.
- **attach:** Open SpendApp › Start SpendApp `_1` (starts it unless it runs), then the same card, which only attaches.
- **foreach:** Reparo For Each reqs (102, 104) › card (opens SpendApp on the request) › Approve the request ›
  [Click Approve `_1`, Click Back `_2`].
- **incard:** card › Reparo For Each reqs › Handle the request › [Click Open request (selector from `req`) `_1`,
  Click Approve `_2`, Click Back `_3`].
- **nested:** card (SpendApp) › [Open the request › Click Open request 102 `_1`, Use Browser card (Edge) › Approve on
  the web › Click Approve (web) `_2`, Go back › Click Back `_3`].

SpendApp removes Approve for 6 s the first time request 104 opens in an app instance. The web page lacks it for 6 s
after each load. The fake Reparo answers from `scenarios/<name>.json`, and `ui_command: unmax` un-maximises and moves
SpendApp mid-Recovery. Logs: `findings/<shape>-<resize>-<scenario>.log`. The trace shows SpendApp's PID and window
state at each Event.

| Run | Continuation | App | Result |
| --- | --- | --- | --- |
| holds, retry | `_2` in place | same PID | completes |
| holds, to-e1 | back to an earlier Activity in the card | card closes and reopens (new PID) | lands on `_1`; fails again on the fresh app, `continuation_failed` |
| attach, to-e2 | back to an earlier Activity in the card | same PID, never closed | lands, completes |
| attach, to-e1 | target before the card | same PID | lands, completes |
| attach, Maximize, unmax-to-e2 | back to an earlier Activity | maximised again when the card starts | completes; Reparo's window change undone |
| attach, Maximize, unmax-retry | retry | stays as Reparo left it | completes |
| foreach, retry | `_1 #1` in place | same PID | completes |
| foreach, to-e1-pass0 | `_1 #0` (earlier pass, loop around the card) | card closes; For Each enters pass 0; new app | lands on `#0`; pass 1 fails again on the fresh app |
| incard, retry | `_2 #1` in place | same PID | completes |
| incard, to-e1-pass0 | `_1 #0` (loop inside the card) | same PID | lands on `#0`, completes |
| nested, retry | `_2` in place | both stay open | completes |
| nested, to-e1 | back to the outer card's Activity | both cards close and reopen | lands on `_1`; the page reloads and fails again |

## Gotchas
- **No Studio sample of a Modern desktop Click existed on the VM.** The XAML follows #66's Use Browser `NClick`:
  `TargetAnchorable` with `FullSelectorArgument="<wnd ctrlname='Approve' />"`, `ScopeSelectorArgument` = the app's
  window, `ScopeIdentifier` = the card's `ScopeGuid`. `FullSelectorArgument` takes a VB expression too.
- **The fake Reparo doesn't put the screen where the target expects.** In `incard-None-to-e1-pass0`, "Click Open
  request" `#0` ran while the detail of 104 was showing. The list was hidden, but the Click still succeeded without
  any effect: it found the hidden WinForms button. A real Recovery must leave the screen the target Event expects.
- **Use Browser needs `vm/serve.ps1` running** (`http://localhost:8066/`), as in #66.
