# Hunt Map

Shows where any hunt mark can spawn, B to SS rank, on the game's own map, and lets you tick spots off with a controller.

## Usage

`/huntmap` opens it. `/huntmap gatling` opens it straight to the search results. For one-flick access, add `/huntmap` to a Flick Wheel shortcut wheel.

| Tab | Shows |
|---|---|
| **Bills** | The targets of the Elite Mark Bills you hold, with their kills. |
| **Here** | The marks in your current zone. |
| **Browse** | By region (region → zone → mark), or A–Z: every mark under collapsible letters. |
| **Search** | Every mark whose name or zone contains what you type. A rank on its own, like `s`, also matches. |
| **Settings** | Close after opening the map (off by default), and which ranks and expansions are shown. |

Opening a mark lists its spawn spots, nearest first when you're in its zone. The map next to the list shows the spots: red for unchecked, grey for checked. Widen the window if it's hidden. If a bill's target is in your zone and isn't done yet, the window opens straight on its spots.

**Hunt** (or Start on the pad) makes the mark your target and closes the window. The target's spots, and those of any unfinished bill target, show on the game's map and minimap as red dots, green once checked. Flagging a spot also makes its mark the target; **Stop hunting** clears it. While you're hunting, the window opens on the target's spots, in the tab you started from. Starting or ending a hunt unchecks all of that mark's spots. Spots check themselves off when you get within 20 yalms of one (turn this off in Settings).

| Button | In a list | In a mark's spots |
|---|---|---|
| D-pad up/down | Move (hold to scroll) | Move |
| D-pad left/right, L1/R1 | Switch tab | Switch tab |
| **A** | Go into a region, zone or mark, or open or close a letter | Open the map with a flag on the spot |
| **B** | Go back or collapse the letter; at the top, close | Back to the list |
| **X** | In Browse, switch between By region and A–Z | Mark the spot checked, or unchecked |
| **Y** | Jump to Search | Flag the nearest spot you haven't checked |
| **Start/Menu** (≡ in the footer) | Stop hunting | Hunt this mark, or stop hunting it |

The game doesn't see these buttons while the window has focus; click outside it to give the controller back to the game, and click the window to take it again. The sticks still move you and the camera. The mouse works too.

Checks are kept per mark. A bill target's checks start over when you take a new bill.

## How it works

Spawn points come from [ffxiv-huntmaps-maker](https://github.com/RKI027/ffxiv-huntmaps-maker)'s `marks.json`, which is bundled with the plugin, and are matched to the game's maps by zone name. The bills you hold are read from the game. Endwalker and Dawntrail B ranks have 5–6 spots each, which may be area centres rather than exact points.

Spots are added after the map's and minimap's own markers, so aetherytes and the rest stay. They're put back whenever the game rebuilds them. The map's marker list holds 132 entries shared with the game, so in a busy zone a long list of spots can be cut short.

The spawn data is © 2019 Arkhelyi, MIT licence (`LICENSE-ffxiv-huntmaps-maker.txt`).

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\HuntMap.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
