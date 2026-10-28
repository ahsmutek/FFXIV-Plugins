# PlaceFinder

Finds small named places like The Slow Wash on the map, no web search needed. Works with a controller.

## Usage

`/place` opens the finder. `/place slow wash` opens it straight to the search results. For one-flick access, add `/place` to a Flick Wheel shortcut wheel.

| Tab | Shows |
|---|---|
| **Here** | The named places on your current zone's map. |
| **Browse** | By region (region → zone → place), or A–Z: every place name under collapsible letters. |
| **Search** | Every place whose name or zone contains what you type. |
| **Settings** | Close after opening the map (on by default), and which expansions show in Browse and Search. |

| Button | Does |
|---|---|
| D-pad up/down | Move (hold to scroll) |
| D-pad left/right, L1/R1 | Switch tab |
| **A** | Go into a region or zone, or open or close a letter. On a place, open the map with a flag on it. |
| **B** | Go back or collapse the letter; at the top, close the finder |
| **Y** | Jump to Search |
| **X** | In Browse, switch between By region and A–Z |

The map next to the list shows where the highlighted place is. Widen the window if it's hidden.

The game doesn't see these buttons while the finder is open. The sticks still move you and the camera. The mouse works too.

## How it works

Each zone's map labels are `MapMarker` rows tied to a `PlaceName`. Markers that only point to a neighbouring zone are skipped. The marker's pixel position is converted to world coordinates, then Dalamud flags it and opens the map. A few names, mostly ships and open sea, have no map label and don't appear.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\PlaceFinder.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
