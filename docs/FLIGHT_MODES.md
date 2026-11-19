# Flight modes: 2D and VR with one click

A flight mode is a way to fly with its own DCS settings and programs. Two come by default:

| Mode | DCS starts with | Before DCS starts | After DCS exits |
| --- | --- | --- | --- |
| `2d` · 2D | `--force_disable_VR` | `VR.enable = false`, TrackIR started | TrackIR closed (only if this launch started it) |
| `vr` · VR | `--force_enable_VR` | `VR.enable = true`, `VR.pixel_density = 1`, SSAO off, SSLR off, `preloadRadius = 60000`, Oculus Debug Tool commands (ASW auto, pixel density 1.5) | Oculus Debug Tool after-flight commands (pixel density back to 0) |

The applied DCS Control profile (Engine Optimizations, CPU Boost, Free VRAM…) is applied first, exactly as Launch DCS does. A setting that profile owns in options.lua is never changed by a mode; settings not present in options.lua are skipped and listed.

## Where the settings live

`flight-modes.json` in DCS Control's state folder (`%LOCALAPPDATA%\DCS Control`). `DcsVr.Cli modes` writes the defaults the first time and prints the path. Values in `dcsSettings` are Lua literals: `"0"`, `"false"`, `"\"High\""`.

## ReShade switch

`reShadeOn` (the switch on the Volar page) applies to 2D and VR; null leaves ReShade as it is. ReShade's DLL is `reShade.dllPath`, or found by itself: `dxgi.dll` or `d3d11.dll` next to DCS.exe with `ReShade.ini` beside it or "ReShade" in its version information. Off renames it to `<name>.off`; on renames it back. Another program's dxgi.dll is never touched.

## VREM

[VREM (ReShade VR Enhancer Mod)](https://www.digitalcombatsimulator.com/en/files/3305420/) is a ReShade add-on that works inside DCS's DirectX 11 rendering, so it also shows in VR without ReShade's OpenXR support. Each mode has `vrem` (default: off in 2D, on in VR; null leaves it as it is). The app finds `*VREM*.addon64` in DCS's `bin` and `bin-mt` folders and renames it to `.off` or back. VREM needs ReShade with full add-on support and ReShade on; the launch notes say so when ReShade is off.

## Command line

```
DcsVr.Cli modes
DcsVr.Cli fly 2d
DcsVr.Cli fly vr
```

`fly` waits for DCS to exit, then closes the programs it started and runs the after-flight commands.

## Sources for the defaults

Community settings for Quest 3 over Link: [DCS forum, Quest 3 setup](https://forum.dcs.world/topic/368664-most-current-best-path-for-configuring-and-optimizing-meta-quest-3-and-dcs), [OTT settings for Quest 3](https://forum.dcs.world/topic/362712-ott-settings-for-quest3). Oculus Debug Tool settings: [Meta documentation](https://developers.meta.com/horizon/documentation/native/pc/dg-debug-tool/). Launch groups idea: [Blue Runner](https://forum.dcs.world/topic/390118-blue-runner-a-dcs-pre-flight-control-center/). The Oculus Debug Tool command strings and these values are starting points still to be checked on the target PC.

## In the app

The app opens on a simple Spanish page (`ui/inicio.html`):

- **Volar**: choose 2D or VR, the ReShade switch (both modes), and VOLAR. The launch uses Optimizations only (DCS runs with Quest Link or the monitor as it is) plus the chosen optimizations.
- **Ajustes**: programs that open with DCS per mode, ReShade's DLL, the optimizations (Engine Optimizations, CPU Boost, Free VRAM), DCS.exe and options.lua, and under Avanzado the options.lua values and Oculus Debug Tool commands. Changes save by themselves.

The full English interface (`ui/index.html`) is still in the package for the offline verification (smoke.js) but the app window no longer shows it.

## Restablecer todo

Ajustes › 06 Restablecer todo (two clicks, DCS closed) puts DCS back as it was before the app:

- DCS Control's own files and owned options.lua settings are restored (the classic "Stock DCS" restore).
- options.lua values changed by a flight mode go back to the value recorded before the first change (`flight-modes/options-originals.json`). Values changed before that record existed are listed so they can be checked in DCS.
- ReShade and VREM are renamed back on, as installed.
- The modes become neutral: no options.lua values, no ReShade or VREM switching, Oculus Debug Tool off, and the optimizations off. VOLAR then only starts DCS with the mode's launch argument and the chosen programs.
