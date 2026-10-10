# RenoDX for Titanfall 2

HDR for Titanfall 2 (DirectX 11). Campaign or Northstar only; do not use the ReShade add-on build in official multiplayer.

## How it works

- The game does all of its post processing in one pixel shader (`0xA0BA6139`, `CBufEnginePost`): sharpen, pilot cloak aberration, depth of field, bloom, exposure, a per-channel filmic curve (`x(10x+0.3) / (x(10x+0.5)+1.5)`), view fades, and a 32³ sRGB color correction LUT. It writes linear color to the swapchain through an `R8G8B8A8_UNORM_SRGB` view, and the HUD is drawn on top afterwards.
- The replacement keeps that whole chain. The vanilla graded result (curve + fades + LUT) is the SDR reference for `ToneMapPass(untonemapped, graded)`, so the vanilla look is kept and highlights extend to the chosen peak.
- The swapchain is upgraded (HDR10 by default, scRGB optional). Because the sRGB view is replaced by a linear float buffer, the intermediate stays linear. Game gamma emulation is applied by `RenderIntermediatePass`; HUD gamma emulation is applied in `SwapChainPass`.

## Build

```
cmake --preset clang-x64
cmake --build --preset clang-x64-release --target titanfall2
```

Output: `build/release/renodx-titanfall2.addon64`.

## Install

1. Install ReShade 6.8+ with full add-on support for `Titanfall2.exe` (DirectX 11). If the game crashes on launch, move `dxgi.dll` and `ReShade.ini` into `bin\x64_retail`.
2. Copy `renodx-titanfall2.addon64` next to ReShade's `dxgi.dll`. Remove `renodx-devkit.addon64` if it is there.
3. Use borderless windowed mode with Windows HDR on. Settings are under the RenoDX tab in the ReShade overlay (Home key).

## Manual verification

- With **Tone Mapper: Vanilla** and the **Off** preset, the image should match unmodded SDR.
- Bright outdoor scenes: sun, sky and explosions should exceed SDR white without clipping to flat white; peak follows **Peak Brightness**.
- HUD, menus and subtitles should sit at **UI Brightness** and not be expanded.
- Pilot cloak (scene alpha mask) and depth of field in menus/cutscenes should look like vanilla.
- Death/fade to black and screen flashes (view fade) should behave as in vanilla.
