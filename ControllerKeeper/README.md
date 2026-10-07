# ControllerKeeper

Keeps your controller working in FFXIV.

## What it fixes

- **Pad freezes on unrelated hardware changes.** The game rescans controllers, briefly
  ignoring the pad, whenever Windows reports any device change (a USB stick, a virtual
  drive, a headset). ControllerKeeper only lets it rescan when the set of connected
  controllers changed.
- **The game switching to the wrong controller.** With several game devices (XInput and HID
  views of one pad, a remapper's virtual pad…), the game can land on another one after a
  rescan or loading screen. ControllerKeeper switches it back to the one you picked.

## Usage

`/controllerkeeper` opens settings:

- **Skip controller rescans when no controller changed**, with a count of skipped rescans.
- **Keep the game on my chosen controller**: pick yours from the list. It's found again
  even in another USB port. **Forget my choice** stops this.
- **Reconnect controllers** (or `/controllerkeeper reconnect`) forces a rescan, like
  replugging. If the pad shows no input in `joy.cpl` either, replug it.

## How it works

The game rescans when its window gets `WM_DEVICECHANGE`. ControllerKeeper subclasses the
window (no game code patched) and passes the message on only when the controllers Windows
lists, by device path, differ from last time. Skipped messages are rechecked 1.5 s later.
Twice a second it checks the game's active controller and calls `SetActiveDevice` if it
isn't yours.

Inspired by [DeviceChangeFix](https://github.com/Jessidhia/DeviceChangeFix); written from
scratch, since that project has no licence.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\ControllerKeeper.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
