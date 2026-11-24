# DBH FSR/DLSS Launcher

A small Windows Forms program (C# 5, builds with the compiler that ships with Windows) for the Detroit: Become Human
RenoDX add-ons in `src/games/detroitbecomehuman`. It installs the add-on files, writes the mod settings to
`ReShade.ini`, patches the Epic exe's 70% resolution-scale minimum (backup kept as `DetroitBecomeHuman_orig.exe`),
and starts the game.

Build: run `build.cmd`, then put `renodx-detroitbecomehuman.addon64`, `renodx-detroitbecomehuman-effects.addon64`,
`amd_fidelityfx_loader_vk.dll` and `amd_fidelityfx_upscaler_vk.dll` in `dist\mod\`.

`patch-detroit-exe.ps1` is the same exe patch without the launcher.

## Differences from the pull request

This branch is the release branch. Compared with the pull request to decoded-brain/renodx it has one change in
`src/games/detroitbecomehuman/addon.cpp`: the four Depth of Field shaders are not replaced and the Depth of Field
settings are hidden, so the game's own DOF runs untouched. With the replacements active the main menu lost its
background blur. Decoded-brain's Native DOF v2 work is left intact in the pull request.

## Notes

- Tested on an RTX 4060 (DLSS) and an RX 6750 XT (FSR 3.1), Epic Games Store build. The Steam build is untested.
- NVIDIA Smooth Motion works alongside the mod on an RTX 4060 (it needs an RTX 40-series card; its frame counter
  includes the generated frames).
- Optional, RTX only: DLSS 5 Neural Rendering via the community "DLSS5 Tool + DX11 Bridge" method (for example through
  DLSS5 Swapper or RHI) ran on top of the mod on an RTX 4060 (Vulkan, DLSS Ultra Performance, 1148x480 to 3440x1440,
  about 4 ms per frame, HDR). It is third-party software that is not included or supported here, it needs NVIDIA's
  nvngx_dlssnr.dll (older cards need a modified one), and other methods (Feeder, GITC Uplift, ShortFuse DLSS Tool) are untested.
