# Icon Atlas

Browse every game icon by what it's for, drawn straight from your game files.

## Usage

`/iconatlas` opens the atlas. `/iconatlas 61241` opens it on that icon; `/iconatlas dancer` searches the ranges.

The list on the left holds every known icon ID range in collapsible categories (job actions, status effects, items, map markers, plates…); hover a range for its IDs. Search by name, or type an ID to find the range it’s in. Click a range to see its icons, or a category name to see all of its icons at once. Right-click a category or range to pin it to the top of the list. Hover an icon for a larger preview, click it to inspect.

The inspector shows the icon's ID, its file path in the game, and its size, with buttons to copy the ID or path. **HQ** shows an item icon's HQ version. Arrow keys step through icons. With a controller, Y is the right-click
and X the double-click.

Right-click an icon, or press **Favorite** in the inspector, to add it to **Favorites** at the top of the list; favorites are marked with a gold dot and saved between sessions.

**Only IDs with an icon** hides the gaps in a range (on by default). **Size** sets the tile size.

Flick Wheel and Retainer Cooldown open the atlas when you change an icon: pick one and press **Use this icon**, or double-click it, to send it back. Other plugins can do the same over IPC: call `IconAtlas.Pick(token, purpose, currentIcon)` and listen on `IconAtlas.Picked(token, icon)`.

## How it works

Range descriptions come from [fae-industries/ffxiv-icons](https://github.com/fae-industries/ffxiv-icons), which calls itself incomplete, with a few overlapping ranges evened out. Opening a range checks each of its IDs for an icon file in the background; icons load through Dalamud's texture provider, HD where the game has it.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\IconAtlas.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
