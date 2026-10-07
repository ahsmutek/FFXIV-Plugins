# Retainer Cooldown

Shows your next retainer venture as a native-looking cooldown icon on the HUD.

## Features

- A game icon with the hotbar's radial cooldown sweep and an exact remaining-time readout.
- A second icon of your choice once every venture is done.
- Tracks the soonest or the last venture to finish.
- Hover for every retainer's venture state.
- Pick icons from a browser in settings, or by ID; sizes snap to hotbar sizes (32–128px).
- Optionally hide the timer, or the whole icon while no ventures are running.
- Reads only `RetainerManager`; no plugin dependencies.

## Usage

- `/retcd`, or clicking the icon, opens settings.
- Untick **Lock HUD** to drag the icon.

## Limitation

The game exposes when a venture finishes but not when it started, so the sweep assumes a
duration you choose (1h by default, Quick Exploration). The remaining time is always exact.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\RetainerCooldown.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
