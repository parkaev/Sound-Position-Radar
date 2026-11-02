**English** | [Русский](README.md)

# Sound Position Radar

A visual indicator of sound direction for games: shows **which side** a loud sound came
from — a gunshot, footsteps, or a bullet snap. Helps you read the match's sound picture
faster, including players who find it hard to orient by sound alone.

The program listens to **system audio** (the entire device output, or a specific process)
and highlights the direction of an event at the edges of the screen.

> **This is not a cheat.** The program doesn't read game memory, doesn't inject into
> processes, and doesn't emulate input — it only uses public Windows audio APIs (the same
> level as OBS "Application Audio Capture"). It shows exactly what you can hear by ear,
> but visually.

## Features

- **Capture audio from a specific process** (WASAPI process loopback) or the entire device;
- **DayZ profile** (calibrated on gunshot recordings: ~2° direction error) and a generic
  constant-power profile;
- Detection of "standing-out" sounds: adaptive background across 5 frequency bands, a
  separate boost for high-frequency bullet snaps;
- Two overlay presets: **Arcs** (sectors at the screen edges) and **Edge Glow**
  (gradient glow along the screen edges);
- Color indication: **by frequency** (bass — dark red, high frequencies — green: gunshots
  and vehicles are red, snaps and birds are green — the sound's character is visible at a
  glance) or a **fixed** color;
- Fine-grained overlay tuning: opacity, size, reaction width, fade time, hidden zones
  (dimmed to ~35% brightness instead of full hiding) — everything with step 1 and manual
  value entry; with two or more monitors — a monitor picker for the overlay;
- Global hotkey to toggle the overlay (works over the game);
- **Crosshair** (the "Crosshair" tab, like Crosshair X / CS2): cross / dot / circle /
  diagonal X shapes, length, thickness, gap, center dot, black outline (0 = off), color
  palette / #RRGGBB and opacity. Drawn in the same overlay as the radar but enabled
  independently — you can keep only the crosshair. Its own global hotkey. Static — no
  reaction to gunshots;
- Themes: dark, light, follow Windows (switched on the fly);
- Event log (JSONL) and raw WAV recording for diagnostics.

## Installation

Download `Sound Radar.exe` from the
[Releases](https://github.com/parkaev/Sound-Position-Radar/releases) page and run it —
no installation required (Windows 10 19041+ / Windows 11). GitHub replaces spaces in file
names with dots (`Sound.Radar.exe`); the `Sound.Radar.zip` archive contains the exe with
the regular name "Sound Radar.exe".

## Quick Start

1. Choose the source: the game's process (processes currently playing sound are listed at
   the top) or the entire device.
2. Choose the game profile (DayZ / Generic) and press **Start**.
3. Enable the overlay with the "Overlay: ON" button (or the global hotkey) — the
   indication appears at the screen edges (the game must be in windowed or "borderless"
   mode).
4. Adjust the threshold / min. volume (hover tooltips) and colors to your liking.
5. The crosshair is on the "Crosshair" tab: enable it with the toggle and set the shape,
   size, and color. You can turn the radar off and keep only the crosshair (or vice versa).

## Limitations

- Stereo audio can't tell "front/rear" — 180° indication. If the game supports 5.1/7.1,
  enable "360°: capture 8 channels" and the rear will be detected.
- The overlay is not visible in exclusive fullscreen — use borderless.
- The overlay is shown on the selected monitor (the "Monitor" list appears with two or
  more screens); positioning is approximate when monitors have different scale factors.
- The EULA of some games may prohibit third-party overlays — check your game's rules.

## Building from Source

Requires .NET SDK 10:

```bash
dotnet build SoundRadar.slnx
dotnet test  tests/SoundRadar.Core.Tests/SoundRadar.Core.Tests.csproj
dotnet publish src/SoundRadar.App/SoundRadar.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## License

[MIT](LICENSE) © parkaev

## Technologies

.NET 10 / C# / WPF, NAudio 3 (WASAPI), custom DSP (FFT-1024, adaptive background, onset
detector, panorama calibration). Tests — xUnit, offline replay on recordings of real
gunshots.
