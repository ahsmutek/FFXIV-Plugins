# Flick Wheel

A controller radial menu for switching jobs and opening anything you use often. Hold the
hotkey, flick the right stick toward an entry, and let go of the hotkey.

## Usage

1. Hold **Ctrl + ,** (map a pad button to it with reWASD or Steam Input). The last wheel
   you used opens; tap **RB** (or LB, LT, RT) to move to the next one.
2. Tilt the right stick toward an entry. It stays targeted when the stick springs back,
   until you flick toward another.
3. Let go of the hotkey to use it. Or set **Pick when** to **The stick returns to center**
   to use it as soon as the stick springs back.

**B**, or letting go with nothing targeted, closes the wheel without doing anything.

`/flick` opens settings; `/flick chat` hides or shows the chat log.

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

Picking a job equips its first gear set, or the one you choose on the **Gear sets** tab.
Jobs without a gear set are dimmed. If the switch isn't possible (combat, a duty, casting,
crafting), an error toast appears.

On the **Job layout** tab you can move groups and pin one to the top, drag jobs to
reorder them (or right-click one to move it), choose which logs to show, and set colors: a style (**Muted, Vivid, Soft,
Dark**), one of 18 palette colors or a custom one per group, or **Use job colors** to
color by role like the game.

## Shortcuts wheel

Starts with three groups you can change:

| Group | Shortcuts |
|---|---|
| Travel | Teleport, Return, Aether Compass |
| Windows | Duty Finder, Timers, Journal |
| Chocobo | Gysahl Greens, `/cbarding job` (Chocobo Barding plugin) |

On the **Shortcuts** tab, **+** adds a shortcut to a group:

- **Menus** – any main-menu window.
- **Actions** – general actions (Teleport, Sprint, Mount Roulette…), the Aether Compass,
  and **Hide or show chat log** (stays hidden through cutscenes and zoning, comes back
  while you type).
- **Items** – usable items in your bags; the wheel shows how many are left.
- **Emotes** – your favourite emotes; search to add any other unlocked one.
- **Macros** – your individual and shared macros, by slot.
- **Commands** – any chat command with your own label and icon, e.g. `/gs change 3`. A
  plugin's command only shows while that plugin is loaded.

Drag a shortcut to reorder it (or **Move left/right** in its right-click menu); right-click it to relabel, change its icon (chosen in [Icon Atlas](../IconAtlas/) when
it's loaded, otherwise by ID), move or remove it. Groups can be renamed, recolored, moved, pinned to the top and deleted (Ctrl if not
empty). Entries you can't use yet leave their spot empty.

### More shortcut wheels

**Add wheel** makes another wheel (Emotes, Raid…) with its own groups and an optional
hotkey on the General tab. Added wheels start outside the RB rotation, so only their own
hotkey opens them. Combined with Job Profiles, a button like L3 can open a different wheel
per job or duty: give the wheel a hotkey such as F13 and make L3 act as F13.

## Settings

The General tab also has wheel scale, icon size, position (drag the preview or type an
offset), the stick threshold, the hotkeys, and which wheels are turned on.

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
