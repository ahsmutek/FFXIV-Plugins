# Retainer Cooldown

A tiny Dalamud HUD plugin that shows your next retainer venture as a native-looking
FFXIV cooldown icon.

## Features

- **Native action-slot look** – renders a real in-game icon (via `ITextureProvider`)
  with the classic radial cooldown darkening sweep on top.
- **Centered timer** – exact remaining time from `VentureComplete`, drawn like the
  game's cooldown numbers.
- **All-complete indicator** – a pulsing green outline when every venture is done
  (no text).
- **Fastest / latest** – choose whether the icon tracks the venture that completes
  soonest or last.
- **Draggable & persistent** – unlock to drag the icon anywhere; its position and all
  settings are saved.
- **Click to open settings** – clicking the icon (or `/retcd`) opens the config window.
- **Icon picker** – browse and click game icons directly in settings, or enter an ID.
- **Size presets** – snap the icon to fixed sizes (32–128px) to match your hotbars.
- **Hover tooltip** – styled list of every retainer's venture state.
- No IPC/plugin dependencies; reads only
  `FFXIVClientStructs.FFXIV.Client.Game.RetainerManager`.

## Usage

- `/retcd` opens settings.
- Uncheck **Lock HUD** to drag the icon; re-check to pin it.
- Click the icon to open settings.

## Build

Requires a current Dalamud/XIVLauncher development environment and the .NET SDK
(tested with .NET 10).

```sh
dotnet build -c Debug
```

Then load `bin\Debug\RetainerCooldown.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).

## Radial progress limitation

`RetainerManager.Retainer` exposes `VentureId` and `VentureComplete`, but not the
venture start time/duration. The radial sweep therefore uses a configurable assumed
duration (default 60 minutes / Quick Exploration). The displayed remaining time is
still based directly on the game's completion timestamp, so it is always exact.
