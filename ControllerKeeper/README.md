# ControllerKeeper

Keeps your controller working in FFXIV.

## What it fixes

- **Pad freezes on unrelated hardware changes.** The game rescans its controllers, and
  stops reading the pad for a moment, every time Windows reports any device change: a
  USB stick, a virtual drive, a headset. ControllerKeeper only lets the game rescan when
  the set of connected controllers actually changed.
- **The game ending up on the wrong controller.** With several game devices (a pad seen
  through XInput and as a plain HID device, a remapper's virtual pad, a 3D mouse's virtual
  joystick…) the game can switch to another one after a rescan or loading screen, and the
  pad seems dead until it's replugged. ControllerKeeper switches the game back to the
  controller you picked.

## Usage

`/controllerkeeper` opens settings:

- **Skip controller rescans when no controller changed** (on by default). The counter
  below it shows how many rescans were skipped and let through this session.
- **Keep the game on my chosen controller** (on by default), and the list of controllers
  the game sees. Pick yours with the radio button; the one the game is using is marked
  *(in use)*. While your controller is unplugged the game is left alone, and it's switched
  back as soon as it returns, even in another USB port.
- **Forget my choice** stops keeping any controller.
- **Reconnect controllers** (or `/controllerkeeper reconnect`) makes the game rescan its
  controllers, like replugging the pad. It helps when the game has lost track of a pad
  that still works elsewhere; if the pad shows no input in Windows' own test (`joy.cpl`)
  either, the pad itself has stopped and needs replugging.

## How it works

- Rescans: the game rescans when its window gets `WM_DEVICECHANGE`. ControllerKeeper sits
  in front of the window (a standard Windows subclass, no game code patched) and compares
  the controllers Windows lists, by device path, with the last ones it saw. Unchanged: the
  message is dropped. Changed: it's passed on. A dropped message is checked again 1.5 s
  later, and the game gets a fresh one if a controller turned up in the meantime.
  Comparing which controllers are connected, not how many, means a pad swapped for
  another (a virtual pad being recreated) still triggers a rescan.
- Keeping your controller: twice a second it checks which of the game's controllers is
  active and, if it isn't yours, calls the game's own `SetActiveDevice`.

Inspired by [DeviceChangeFix](https://github.com/Jessidhia/DeviceChangeFix) (written from
scratch; that project has no licence, so none of its code is used).

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\ControllerKeeper.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
