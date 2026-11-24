# PROTOTYPE: the vision Modality in a UiPath run

Throwaway code for [The vision Modality in a UiPath run](https://github.com/TunixR/ReparoHome/issues/49). Never merged. The answer lives on that issue; the full notes are in [`findings/NOTES.md`](findings/NOTES.md).

## What's here

- `Bridge/Vision/`: the vision Modality, the code the question is about.
  - `VisionModality.cs`: `VisionSession` (monitor choice, screenshot, `Act` for every wire action type, session and integrity checks) and `VisionSettings`.
  - `Keys.cs`: pyautogui-style key names to virtual keys, plus the hotkey splitter ported from the Playwright Bridge.
  - `Native.cs`: Win32 declarations.
- `Bridge/`: the package `Reparo.UiPathBridge.Activities` (`net6.0-windows`) with one UiPath activity, `ReparoVisionProbe`, that plays a probe script inside the Executor.
- `Bridge/ProbeRunner.cs`: plays a script of wire-shaped requests (`observe`, `act`, ...) and logs each reply. `"@name"` in a box aims at a TargetApp control.
- `Probe/`: the same runner as a console exe with no DPI manifest, for quick runs outside UiPath, plus `scale <percent>`, which changes the display scaling live.
- `TargetApp/`: a WinForms window with one control per action, logging what it receives (`app.log`) and writing its control rectangles (`rects.json`).
- `VisionRun/Main.xaml`: the UiPath process that runs `ReparoVisionProbe`.
- `scripts/`: one probe script per scenario.
- `vm/`: helpers for the `reparo-win` VM. `build.sh` and `go.sh` run on the host; the `.ps1` files run on the VM, in the console session.
- `findings/`: the logs, screenshots and app logs of each scenario, and `NOTES.md`.

## Running it

From the host, with the VM up:

```sh
vm/build.sh Probe TargetApp           # sync and build on the VM
vm/go.sh basics app                   # run scripts/basics.json against TargetApp
vm/go.sh scaled app 150               # the same at 150 % scaling
```

Inside UiPath: run `vm/setup-uipath.ps1` once on the VM (packs the Bridge into a local feed and creates `VisionRun`), then run `vm/uipath.ps1` through `vm/console49.ps1`.
