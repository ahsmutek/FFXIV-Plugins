# Minimap Zoom

Lets the minimap zoom out further than the game allows, so you can see more of the
area around you, e.g. when gathering.

## Usage

- The game stops zooming out at 1.25. With this plugin, the minimap's **−** button
  keeps working past that, 0.25 a click, down to **Furthest zoom** (0.25 by default).
- **+** zooms back in as usual. Once you're back at 1.25 or closer, the game is in
  charge again and the plugin does nothing.
- The game resets the minimap when you change job, zone or log in. The plugin puts
  your zoom back each time.
- `/minimapzoom` opens settings, which show the current zoom. `/minimapzoom reset`
  (or **Back to 1.25**) returns to the game's furthest zoom.
- Turning the plugin off or unloading it also returns the minimap to 1.25.

The map image still covers the whole circle when zoomed out. The game places the
markers using the same zoom value, so they move with the map. How many gathering
nodes show depends on what the game has loaded around you, so zooming out doesn't
always reveal more of them.

## How it works

ClientStructs doesn't map the minimap's zoom, so the plugin reads it at a fixed offset
in the `_NaviMap` addon. Before writing, it checks that the value matches the map
image's scale, which the game keeps equal to the zoom. If a game update moves the value,
the check fails and the plugin leaves the minimap alone; settings then show the zoom
as unknown.

Debug builds also have `/minimapzoomdebug`: memory and UI-node snapshots and diffs,
event logging and button unlocking, used to find the zoom value.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\MinimapZoom.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
