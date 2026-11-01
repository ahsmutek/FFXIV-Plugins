# CatProof

Locks the keyboard in FFXIV so cats walking across it can't type in chat or press hotkeys while you play on a controller.

## Usage

- `/catproof` locks or unlocks the keyboard. `/catproof on` and `/catproof off` set it either way, and
  `/catproof config` opens settings. All of them work from a macro or a Flick Wheel shortcut; a `/catproof`
  shortcut without its own icon shows the icon chosen here, orange while locked.
- While locked, a small box shows a different cat message each time, with an **Unlock** button. Settings can
  switch it to just an icon (a paw, cat, fish, box and more) that stays on screen, dimmed while unlocked;
  click it to lock or unlock. Drag either anywhere; its place is kept, and
  settings can reset it.
- Every game session starts unlocked, and disabling or unloading the plugin unlocks at once.

While locked, presses from physical keyboards don't reach the game, Direct Chat, a text field that already
has focus, Dalamud or any plugin window. The controller, the mouse and keys sent by software (Steam Input,
DS4Windows, Job Profiles key bindings) work as usual. A key held while you lock is still let go, so nothing gets stuck.

## Limitations

- **Windows shortcuts** (the Win key, Alt+Tab, Ctrl+Alt+Del, Ctrl+Shift+Esc) reach Windows before the game.
  Blocking them would take a system-wide keyboard hook, which CatProof avoids.
- **Plugins that read the keyboard state directly** still see presses. A cat can open Flick Wheel with its
  hotkey, but can't pick anything without the stick.
- **Mapping software with its own virtual keyboard driver** (reWASD, Razer or Logitech remapping, Interception
  tools) looks like a physical keyboard and gets blocked. Software that sends keys the usual way works.
- A key still held when you unlock reaches the game from Windows' next key repeat.

## How it works

CatProof hooks `PeekMessageW` inside the game process. A key press marked by Windows as coming from hardware
(`GetCurrentInputMessageSource`) is changed to an empty message as the game takes it from the queue, before it
becomes text and before the game, Dalamud or any plugin sees it. Key releases always pass. Raw Input keyboard
presses are dropped the same way if the game reads the keyboard that way.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\CatProof.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
