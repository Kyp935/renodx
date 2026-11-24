# DBH FSR/DLSS Launcher

A small Windows Forms program (C# 5, builds with the compiler that ships with Windows) for the Detroit: Become Human
RenoDX add-ons in `src/games/detroitbecomehuman`. It installs the add-on files, writes the mod settings to
`ReShade.ini`, patches the Epic exe's 70% resolution-scale minimum (backup kept as `DetroitBecomeHuman_orig.exe`),
and starts the game.

Build: run `build.cmd`, then put `renodx-detroitbecomehuman.addon64`, `renodx-detroitbecomehuman-effects.addon64`,
`amd_fidelityfx_loader_vk.dll` and `amd_fidelityfx_upscaler_vk.dll` in `dist\mod\`.

`patch-detroit-exe.ps1` is the same exe patch without the launcher.

`src/utils/settings.hpp` on this branch has one change: the "Depth of Field" settings section starts collapsed.
It is not part of the pull request to decoded-brain/renodx.
