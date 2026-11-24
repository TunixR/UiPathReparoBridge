# Findings: the vision Modality in a UiPath run

Prototype for [The vision Modality in a UiPath run](https://github.com/TunixR/ReparoHome/issues/49). Every run is on the `reparo-win` VM: Windows Server 2025, Studio/Robot 25.10.19, one 1920x1080 monitor, a Spanish keyboard layout (`0x40A0C0A`), the console session auto-logged on as `reparo` (medium integrity).

Instead of Reparo or FakeReparo, a probe script sends wire-shaped requests: a `screenshot` (`observe`) and `action` messages with `action_type` and `action_inputs`, as Reparo's agent writes them. Each reply is the Bridge's `{success, action_type, error?}`. TargetApp logs what each control actually got. Boxes come from TargetApp's own control rectangles, so a hit is exact, not read off the screenshot by eye. The one exception is `dismiss`, which was read by eye.

## Decisions (with the owner)

| Question | Decision |
| --- | --- |
| Which screenshot | The whole monitor that holds the foreground window when the Recovery starts. It stays fixed for the Recovery. A minimised or missing foreground window falls back to the primary monitor. |
| Coordinates | Physical pixels. The screenshot is the monitor at its native resolution, and a box's pixels are that monitor's pixels plus its origin. The Bridge pins per-monitor-v2 DPI awareness on its own thread for each call, whatever the process declares. |
| Typing | Layered, with a setting. `type_method: keys` (the default) sends real scan codes for every character the target window's layout can make, Shift and AltGr included, and `VK_PACKET` (Unicode) for the rest: dead-key accents, other scripts, emoji. `unicode` sends everything as `VK_PACKET`. `paste` puts the text on the clipboard, sends Ctrl+V and restores the clipboard, for Citrix or IME fields. |
| Elevation | Detect and fail the action. Before input, the Bridge compares the target window's integrity level with the Robot's and replies `success: false` with the reason. The guide says to run the Robot elevated to automate admin apps. |

## Input method per action

All Win32 `SendInput`. No UiPath activity takes a raw point or sends a lone key down or up.

| `action_type` (aliases) | Input |
| --- | --- |
| `left_single` (`click`, `select`), `left_double`, `right_single` | absolute move with `MOUSEEVENTF_VIRTUALDESK`, then button down and up (twice, back to back, for double) |
| `hover` | absolute move |
| `drag` | move to the start, left down, 20 interpolated moves, left up |
| `scroll` | move to `start_box`, then `ScrollDelta` (360 = 3 notches) in chunks of 120, `WHEEL` for up/down and `HWHEEL` for left/right |
| `type` | per `type_method`, above; `\n` is Enter and `\t` is Tab; keys held by `keydown` are released while typing and pressed again afterwards |
| `hotkey` | virtual keys with the scan code from the layout, `EXTENDEDKEY` where needed (right Ctrl/Alt, Win, arrows, Home/End, PgUp/PgDn, Insert/Delete), pressed in order and released in reverse |
| `keydown` (`press`), `keyup` (`release`) | one key event; the Bridge tracks held keys and releases them when the Recovery ends |
| `wait` | sleep |

A box is `[x, y]` or `[x1, y1, x2, y2]` (the Bridge takes the centre). Values all ≤ 1.0 on an axis are fractions of the screenshot.

## Verified

- **Inside the UiPath Executor** (`findings/uipath`): the activity runs in `UiPath.Executor` on .NET 8, started with `uip rpa run`. Its process is per-monitor DPI aware: the Executor's manifest declares PerMonitorV2, and UiRobot.exe and Studio declare none. Click, the 8x8 `tiny` target, double-click, scroll, typing with scan codes and VK_PACKET (`desde UiPath ñ@€é 漢 😀`), paste and Ctrl+A all arrive exactly. TargetApp logs `Cursor.Position` when its handler runs, so the `tiny` line shows the next action's point. The tiny control's own handler fired; with pauses between steps (`basics`) it logs +0,+0.
- **Every action at 100 %** (`findings/basics`, a DPI-unaware console exe):
  - Clicks via `[x,y]`, a 4-value box and fractions all land +0,+0 from the control's centre, the 8x8 target included. Double-click, right-click and hover arrive as such.
  - `type` with `keys`: `Hola ñandú @ 5€ ¿é? 漢字 😀⏎` arrives exactly, as 19 scan-code characters and 6 VK_PACKET ones. ñ is the layout's own key (scan 0x27), @ and € go through AltGr, ¿ through Shift, ú and é (dead keys on this layout) go through VK_PACKET, as do CJK and the emoji's surrogate pair.
  - `unicode` and `paste` arrive exactly; paste restores the clipboard's text.
  - A Shift held by `keydown` doesn't turn typed `abc` into `ABC`.
  - Hotkeys `ctrl a`, `ctrl+end` and `shift home` work, and `keydown ctrl` is released at the end.
  - Scroll moves the list 9 rows (3 notches); left/right scroll sends 3 `WM_MOUSEHWHEEL`; drag sends 20 moves between down and up.
  - Rejections reply `success: false` with a reason: a point outside the screenshot, an unknown key, a bad scroll direction, an unknown `action_type`.
- **Windows Reparo opens or closes** (`basics`): a dialog opened by a click, outside the main window, shows in the next screenshot, and its OK can be clicked. After the app's window closes, the screenshot still works and shows the desktop. There is no Active page to follow: every window on the monitor is in the screenshot, and keyboard input goes to whatever window is in the foreground.
- **Live 150 % scaling** (`findings/scaled-150`): the DPI-unaware process sees a 1280x720 screen, and the pinned thread sees 1920x1080 at 144 dpi. The screenshot is 1920x1080 physical. Clicks land +0,+0, and tiny, double, scroll, drag and type work.
- **An elevated target** (`findings/elevated`): the check reads integrity 0x3000 against the Robot's 0x2000 and fails the action: "the target window '…' runs elevated …; Windows would drop the input". With the check off, `SendInput` returns success and the app gets nothing: the drop is silent, as research said.
- **A locked console** (`findings/locked`, `locked-console.png`): `OpenInputDesktop` fails with error 5, so both `observe` and `act` raise SessionLost, which the Bridge treats as an Infrastructure failure. The same check catches a disconnected session (`WTSConnectState`) and an input desktop other than `Default` (UAC, Ctrl+Alt+Del).
- **A real unexpected window** (`findings/dismiss`): the Shutdown Event Tracker that appears after an unplanned shut-off; Cancel was read off the screenshot and clicked.

## Not verified

- **Several monitors:** the VM has one head. The code takes the foreground window's monitor, with its origin, and the virtual-desk normalisation covers negative origins, but nothing ran there.
- **Citrix, VMware Horizon, RDP-in-RDP:** no client on the VM. That's why `paste` exists; whether scan codes or VK_PACKET reach a Citrix session is open.
- **An unattended RDP session at a fixed Robot Settings resolution:** the console session was used throughout.
- **.NET 10:** as in [How a Process is built in UiPath](https://github.com/TunixR/ReparoHome/issues/46), it needs Robot 26.10.
- **IME (Japanese, Chinese) input:** VK_PACKET bypasses the IME, so the characters arrive but no composition runs. `paste` is the workaround.

## Recommendations for the spec (not claimed by the owner)

- `ScrollDelta` 360 (3 notches), 20 drag steps, JPEG quality 85.
- Draw the pointer into the screenshot, as the Reference client does: the agent can see where it is.
- Release held keys when the Recovery ends, whatever its result.
- Fractions should be required to be 0..1; today a negative value slips through as a fraction.
- The screenshot shows every window on the monitor, other apps included. The secret-Event gap (screenshots aren't masked) applies here too.
- An app closing is not an Infrastructure failure: the screen remains. SessionLost (locked, disconnected, secure desktop) is.
- `paste` restores text only; other clipboard formats are lost.
- Keep `VisionSession` behind a Modality interface (observe, act, release), so a UIA accessibility-tree Modality can sit beside it.
- Before the first action, the Bridge should check the screen hasn't changed since the screenshot (monitor moved, resolution or scaling changed). The prototype does this and fails the action with "the screen changed since the screenshot".

## Session rules for the guide

- The Robot's session must be unlocked, connected and on the `Default` desktop for the whole Recovery: no screen lock, no disconnect, no UAC prompt.
- Run the Robot elevated to automate an elevated app; otherwise Reparo's actions on it fail.
- Unattended: RDP with a fixed Robot Settings resolution, the minimised-RDP registry fix on connecting machines, no PiP Virtual Desktop.
- In Citrix or for IME text, set `type_method` to `paste`.
- Reparo sees and can click anything on the monitor of the app's window, other apps and notifications included.
