# Minimap Zoom

Lets the minimap zoom out past the game's limit, e.g. to see more around you when gathering.

## Usage

- The game stops zooming out at 1.25. The minimap's **−** button now keeps going, 0.25 a
  click, down to **Furthest zoom** (0.25 by default). **+** zooms back in as usual.
- Your zoom is restored after job changes, zoning and logins.
- `/minimapzoom` opens settings; `/minimapzoom reset` returns to 1.25, as does disabling
  the plugin.

Markers move with the map. Zooming out doesn't always show more gathering nodes, since that
depends on what the game has loaded.

## How it works

ClientStructs doesn't map the minimap's zoom, so the plugin reads it at a fixed offset in
`_NaviMap` and only writes after checking it against the map image's scale. If a patch
moves it, the plugin leaves the minimap alone.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\MinimapZoom.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
