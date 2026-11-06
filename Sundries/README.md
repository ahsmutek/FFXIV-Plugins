# Sundries

Small quality-of-life helpers that don't need a plugin of their own. `/sundries` (or Sundries' cog in
`/xlplugins`) lists them, each with its own on/off switch and settings.

## Guide Highlight

Opening the Mount Guide or Minion Guide borders the mount you're riding or the minion you have out, while its
page is shown (colour in settings). The minion still counts while you're mounted and it's hidden.

## Minimap Zoom

The game stops zooming the minimap out at 1.25. The minimap's **−** button now keeps going, 0.25 a click, down
to **Furthest zoom** (0.25 by default); **+** zooms back in as usual. Your zoom is restored after job changes,
zoning and logins. **Back to 1.25** in settings, or turning it off, returns to the game's range.

Markers move with the map. Zooming out doesn't always show more gathering nodes, since that depends on what
the game has loaded.

Minimap Zoom used to be its own plugin. Remove it once Sundries is installed; its settings carry over the
first time Sundries loads.

## How it works

Guide Highlight matches the guides' grid slots by icon against your current mount's or minion's icon, and
draws an ImGui border over the slot. The border is left out where another game window covers it.

ClientStructs doesn't map the minimap's zoom, so Minimap Zoom reads it at a fixed offset in `_NaviMap` and
only writes after checking it against the map image's scale. If a patch moves it, the minimap is left alone.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\Sundries.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
