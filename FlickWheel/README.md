# Flick Wheel

_Formerly **Job Wheel**. Your Job Wheel settings are carried over the first time Flick Wheel loads._

A controller-friendly radial menu for switching jobs and opening anything else you
use often. Hold the shortcut, flick the right stick toward an entry, and let go of
the shortcut to use it. You don't press a confirm button or open a list.

It has two parts, each of which can be turned off in settings:

- **Jobs** – a combat wheel and a crafting & gathering wheel with every job in a fixed spot.
- **Shortcuts** – a wheel of your own shortcuts (game windows, Teleport, items, plugin commands) in groups you name and color.

## Usage

1. Hold **Ctrl + ,**. On a controller, map a button to it with reWASD or Steam Input.
   The wheel opens on whichever wheel you used last (combat or crafting and gathering).
   Tap **RB** (or LB, LT or RT, set in settings) while it's open to move on to the next wheel.
2. Tilt the right stick toward a job. Its whole slice of the wheel fills with its role color
   and its name shows in the middle of the wheel.
3. Let go of the shortcut. The wheel closes and the job's gear set is equipped.
   The stick can spring back to center first; the target stays until you flick
   toward another job.
   Prefer the original flick-and-release? Set **Pick when** to **The stick
   returns to center** in settings, and the job switches as soon as the stick
   springs back.

- Pressing **B** closes the wheel with no change, even with a job targeted.
- Letting go of the shortcut without targeting anything closes the wheel with no change.
- Picking the job you're already on just closes the wheel.
- `/flick` opens settings (`/jobwheel` still works).

## Layout

There are two wheels, and every job has a fixed spot on its wheel, so you can learn
the directions by feel. Groups run clockwise starting from the top.

**Combat wheel** (tanks centered north, healers centered south):

| Group | Jobs |
|---|---|
| Tanks (north) | PLD WAR DRK GNB |
| Melee (east) | MNK DRG NIN SAM RPR VPR |
| Limited (east) | BLU BST |
| Healers (south) | WHM SCH AST SGE |
| Physical ranged (west) | BRD MCH DNC |
| Magical ranged (west) | BLM SMN RDM PCT |

**Crafting and gathering wheel** (tap RB or your chosen button to swap to it; crafters across the top, then the gatherers and the logs down the right side, bottom and left):

| Group | Jobs |
|---|---|
| Hand | CRP BSM ARM GSM LTW WVR ALC CUL |
| Land | MIN BTN FSH |
| Logs | Gathering Log, Crafting Log |

- Locked jobs leave their spot empty, and nothing else moves when you unlock one.
- The **Crafting Log** and **Gathering Log** entries open those windows instead of switching job. They appear once the game has unlocked them.
- The **Job layout** tab in settings lets you:
  - move groups around the wheel with the arrows, and choose which group sits centered at the top with **Pin to top**. With tanks pinned on the combat wheel, healers stay at the bottom unless you put them right next to tanks;
  - reorder jobs within a group (drag an icon sideways);
  - pick each group's color, or apply a color preset (Muted, Vivid, Soft, Classic roles, Dark) to the whole wheel.
- If you have a class but not its job yet (e.g. Gladiator), the class shows in the job's spot.
- The stick targets the slice it points into. Pointing at an empty slice (a locked job) targets the nearest unlocked job instead.
- The ring is a band colored by role (blue tanks, green healers, reds for DPS, grey limited jobs, purple crafters, teal gatherers), with the job glyphs on top. The targeted job's slice fills with its role color and your current job has a white dot. The east and west sides split the space left between tanks and healers evenly, so slots on the two sides can be slightly wider or narrower than tank and healer slots.

## Switching

Switching equips a gear set:

- By default the plugin uses the first gear set for the job.
- You can pick a specific gear set for any job under **Gear set per job** in settings.
- Jobs with no gear set appear dimmed.

If the switch isn't possible, a short error toast appears and nothing else happens.
This covers being in combat, in a duty, casting, crafting, or not having a gear set.

## Shortcuts wheel

Tap RB until the Shortcuts wheel comes up, or give it its own shortcut in settings.
It starts with three groups, all of which you can change:

| Group | Shortcuts |
|---|---|
| Travel | Teleport, Return, Aether Compass |
| Windows | Duty Finder, Timers, Journal |
| Chocobo | Gysahl Greens, `/cbarding job` (Chocobo Barding plugin) |

In the **Shortcuts** settings tab:

- **+** adds a shortcut to a group. The picker has four tabs:
  - **Menus** – any main-menu window: Character, Armoury Chest, Inventory, Currency,
    Achievements, Blue Magic Spellbook, Master's Bestiary, Hunting Log, Duty Finder…
  - **Actions** – general actions (Teleport, Return, Sprint, Mount Roulette, Repair…) and the Aether Compass.
  - **Items** – usable items in your bags. The wheel shows how many you have left, and fades the icon at 0.
  - **Commands** – any plugin command, with your own label and icon ID. It only shows on the
    wheel while that plugin is loaded.
- Drag a shortcut sideways to reorder it. Right-click it to change its label or icon,
  move it to another group, or remove it.
- Each group has a name, a color, arrows to move it around the wheel, and **Pin to top**.
  **Add group** makes a new one; deleting a group that has shortcuts needs Ctrl held.
- Menus you haven't unlocked yet leave their spot empty.

## Settings

- Wheel scale
- Icon size
- Position: tick **Show wheel preview** (pick which wheel it shows next to it) and drag the wheel with the mouse, type an offset from screen center, or press **Center** to reset it
- Stick selection threshold, and whether letting go of the shortcut or the stick returning to center picks
- The shortcut, and which pad button (RB, LB, LT or RT) moves to the next wheel
- Optionally, a shortcut per wheel that opens straight to it. The shared shortcut keeps opening the wheel you last used with it
- Which wheels are turned on (**Jobs**, **Shortcuts**)
- Group order and which group is on top, job order within each group, each group's color, and color presets
- Gear set per job
- Shortcut groups, their colors and their shortcuts

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\FlickWheel.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).

## Notes

- While the wheel is open, Dalamud's gamepad capture (`IGamepadState.EnableGamepadNav`)
  stops controller input from reaching the game, so the right stick doesn't turn the camera.
  It's switched back off when the wheel closes. If the camera still moves, a reWASD
  shift layer on the shortcut button can remap the right stick while it's held.
- If the controller disconnects while the wheel is open, the stick reads as centered.
  If a job was targeted it's confirmed; otherwise letting go of the shortcut cancels.
- Zoning, cutscenes and logging out close the wheel without switching.
