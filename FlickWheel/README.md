# Flick Wheel

A controller radial menu for switching jobs and opening anything you use often. Hold the
hotkey, flick the right stick toward an entry, and let go of the hotkey.

## Usage

1. Hold a hotkey, **Ctrl + ,** to start with (map a pad button to it with reWASD or Steam
   Input). The last wheel you used with it opens; tap **RB** (or LB, LT, RT) to move to
   its next wheel.
2. Tilt the right stick toward an entry. It stays targeted when the stick springs back,
   until you flick toward another.
3. Let go of the hotkey to use it. Or set **Pick when** to **The stick returns to center**
   to use it as soon as the stick springs back.

**B**, or letting go with nothing targeted, closes the wheel without doing anything.

`/flick` opens settings; `/flick chat` hides or shows the chat log.

## Settings

- **Wheels**: every wheel in a list; pick one to edit it, or **+ New wheel**. Each wheel
  shows which hotkeys open it.
- **Hotkeys**: what each hotkey opens, and the next wheel button.
- **Look & feel**: wheel scale, icon size, position (drag the preview or type an offset),
  the stick threshold and **Pick when**.

## Hotkeys

Each hotkey opens its own list of wheels, any mix of job wheels and your own, and RB
moves through them in that order. You start with **Main** (Ctrl + ,, every wheel); there's
always at least one.

**Edit** a hotkey to rename it, set its key (click the key button and press it, or
**Choose from list**), and add, remove or reorder its wheels. Warnings show a hotkey with
no wheels, or the same key as another.

With [Job Profiles](../JobProfiles/), a button like L3 can **Open Flick Wheel** with a
different hotkey on each job or duty, e.g. Tank on tanks and Healer on healers. Hotkeys
used only that way don't need a key (**Clear** removes one).

## Job wheels

Every job has a fixed spot, so you can learn the directions by feel. Locked jobs leave
their spot empty; a class you have without its job (e.g. Gladiator) takes the job's spot.

**Combat**, tanks at the top and healers at the bottom:

| Group | Jobs |
|---|---|
| Tanks (north) | PLD WAR DRK GNB |
| Melee (east) | MNK DRG NIN SAM RPR VPR |
| Limited (east) | BLU BST |
| Healers (south) | WHM SCH AST SGE |
| Physical ranged (west) | BRD MCH DNC |
| Magical ranged (west) | BLM SMN RDM PCT |

**Crafting and gathering**:

| Group | Jobs |
|---|---|
| Hand | CRP BSM ARM GSM LTW WVR ALC CUL |
| Land | MIN BTN FSH |
| Logs | Crafting, Gathering and Fishing Log; optionally the Fish Guide |

Picking a job equips its first gear set, or the one you choose under **Gear set** in the
job's right-click menu. Jobs without a gear set are dimmed. If the switch isn't possible
(combat, a duty, casting, crafting), an error toast appears.

In a job wheel's editor you can move groups and pin one to the top, drag jobs to reorder
them (or right-click one to move it), choose which logs to show, and set colors for both
job wheels: a style (**Muted, Vivid, Soft, Dark**), one of 18 palette colors or a custom
one per group, or **Use job colors** to color by role like the game.

## Your wheels

**Shortcuts** starts with three groups you can change:

| Group | Shortcuts |
|---|---|
| Travel | Teleport, Return, Aether Compass |
| Windows | Duty Finder, Timers, Journal |
| Chocobo | Gysahl Greens, `/cbarding job` (Chocobo Barding plugin) |

**+ New wheel** adds another (Emotes, Raid…); add it to a hotkey to open it. In a wheel's
editor, **+** adds shortcuts to a group: tick as many as you like, on any tab, then **Add**.

- **Menus** – any main-menu window.
- **Actions** – general actions (Teleport, Sprint, Mount Roulette…), the Aether Compass,
  and **Hide or show chat log** (stays hidden through cutscenes and zoning, comes back
  while you type).
- **Items** – usable items in your bags; the wheel shows how many are left.
- **Emotes** – your favourite emotes; search to add any other unlocked one.
- **Macros** – your individual and shared macros, by slot.
- **Commands** – any chat command with your own label and icon, e.g. `/gs change 3`. A
  plugin's command only shows while that plugin is loaded. Without an icon of your own, a
  plugin can supply one (CatProof shows your chosen cat icon, lit while locked).

Drag a shortcut to reorder it (or **Move left/right** in its right-click menu); right-click it to relabel, change its icon (chosen in [Icon Atlas](../IconAtlas/) when
it's loaded, otherwise by ID), move or remove it. Groups can be renamed, recolored, moved or copied to another wheel, pinned to the top and deleted (Ctrl if not
empty). Wheels can be renamed, and deleted except Shortcuts (Ctrl if not empty). Entries
you can't use yet leave their spot empty.

## Notes

- Every wheel is the same size; one with more than 24 entries draws smaller icons.
- While the wheel is open, controller input is kept from the game so the right stick
  doesn't turn the camera.
- Zoning, cutscenes and logging out close the wheel without using anything.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\FlickWheel.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
