# FFXIV Plugins

A collection of my [Dalamud](https://github.com/goatcorp/Dalamud) plugins for
Final Fantasy XIV. Each plugin lives in its own folder.

## Plugins

### [Retainer Cooldown](RetainerCooldown/)
A tiny HUD icon showing your next retainer venture as a native-looking FFXIV
cooldown, with an exact remaining-time readout, a radial cooldown sweep, and a
green "all done" indicator. Draggable, configurable, and dependency-free (reads
only `RetainerManager`). Command: `/retcd`.

## Building

Each plugin is a standard Dalamud project. From a plugin's folder:

```sh
dotnet build -c Debug
```

Then load the built DLL from `bin\Debug\` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations). See each plugin's own
README for details.
