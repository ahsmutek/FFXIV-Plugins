# Hunt Map

Shows where the B-rank targets of your weekly Elite Mark Bills can spawn, on the game's own map, and lets you tick spots off with a controller.

## Usage

Take an Elite Mark Bill from a Hunt Board, then open `/huntmap`. For one-flick access, add `/huntmap` to a Flick Wheel shortcut wheel.

- There's one tab per bill you hold. It opens on the target in your zone, showing its kills and how many spots you've checked.
- The list shows the target's possible spawn spots, nearest first when you're in its zone, with a map preview beside it. Widen the window if the preview is hidden.
- While the bill isn't done, the game's map of that zone shows every spot: red for not yet checked, grey for checked.

| Button | Does |
|---|---|
| D-pad up/down | Move (hold to scroll) |
| D-pad left/right, L1/R1 | Switch target |
| **A** | Open the map with a flag on the spot |
| **X** | Mark the spot checked, or unchecked |
| **Y** | Flag the nearest spot you haven't checked |
| **B** | Close |

The game doesn't see these buttons while the window is open. The sticks still move you and the camera. The mouse works too: click a spot to flag it.

Checks are kept per bill and start over when you take a new one.

## How it works

The plugin reads which Elite bills you hold from the game, and looks up each target's spawn points by name and zone in [ffxiv-huntmaps-maker](https://github.com/RKI027/ffxiv-huntmaps-maker)'s `marks.json`, which is bundled with the plugin. All 77 Elite targets, A Realm Reborn to Dawntrail, are covered. Endwalker and Dawntrail targets have 5–6 spots each, which may be area centres rather than exact points.

The spots are added after the map's own markers, so aetherytes and the rest stay, and they're put back whenever the game rebuilds the map. The marker list holds 132 entries shared with the game, so in a busy zone a long list of spots can be cut short.

The spawn data is © 2019 Arkhelyi, MIT licence (`LICENSE-ffxiv-huntmaps-maker.txt`).

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\HuntMap.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
