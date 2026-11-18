# Flight modes: 2D and VR with one click

A flight mode is a way to fly with its own DCS settings and programs. Two come by default:

| Mode | DCS starts with | Before DCS starts | After DCS exits |
| --- | --- | --- | --- |
| `2d` · Volar en 2D | `--force_disable_VR` | `VR.enable = false`, DLSS 5 DLL per the mode's switch, TrackIR started | TrackIR closed (only if this launch started it) |
| `vr` · Volar en VR | `--force_enable_VR` | `VR.enable = true`, `VR.pixel_density = 1`, SSAO off, SSLR off, `preloadRadius = 60000`, Oculus Debug Tool commands (ASW auto, pixel density 1.5) | Oculus Debug Tool after-flight commands (pixel density back to 0) |

The applied DCS Control profile (Engine Optimizations, CPU Boost, Free VRAM…) is applied first, exactly as Launch DCS does. A setting that profile owns in options.lua is never changed by a mode; settings not present in options.lua are skipped and listed.

## Where the settings live

`flight-modes.json` in DCS Control's state folder (`%LOCALAPPDATA%\DCS Control`). `DcsVr.Cli modes` writes the defaults the first time and prints the path. Values in `dcsSettings` are Lua literals: `"0"`, `"false"`, `"\"High\""`.

## DLSS 5 switch

`dlss5.targetPath` is the DLL DCS loads (for example `...\DCSWorld\bin-mt\nvngx_dlss.dll`), `dlss5.replacementPath` the DLL to use instead. A mode with `dlss5: true` copies the replacement over the target; the original is backed up once under `flight-modes\dll-originals` and goes back in a mode with `dlss5: false`.

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

The **Volar** page opens first: one big button per mode, what the last launch did, and each mode's settings (DLSS 5 switch, programs, DCS settings, Oculus Debug Tool), the DLSS 5 DLL paths, and a reset to the recommended values. The rail shows only Volar, Overview, Engine, CPU Boost, DCS & headset, Help and Stock DCS; **Mostrar ajustes avanzados** brings back the Pimax, DLSS 5 and Framegen pages. With a Quest 3, choose **Optimizations only** (the page offers it): DCS then uses Quest Link as it is and DCS Control adds only the optimizations.
