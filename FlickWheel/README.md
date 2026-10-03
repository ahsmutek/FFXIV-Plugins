# Flick Wheel

_Formerly **Job Wheel**. Your Job Wheel settings are carried over the first time Flick Wheel loads._

A controller-friendly radial menu for switching jobs and opening anything else you
use often. Hold the hotkey, flick the right stick toward an entry, and let go of
the hotkey to use it. You don't press a confirm button or open a list.

It has two parts, each of which can be turned off in settings:

- **Jobs** – a combat wheel and a crafting & gathering wheel with every job in a fixed spot.
- **Shortcuts** – a wheel of your own shortcuts (game windows, Teleport, items, macros, plugin commands) in groups you name and color.

## Usage

1. Hold **Ctrl + ,**. On a controller, map a button to it with reWASD or Steam Input.
   The wheel opens on whichever wheel you used last (combat, crafting and gathering,
   or shortcuts). Tap **RB** (or LB, LT or RT, set in settings) while it's open to move
   on to the next wheel.
2. Tilt the right stick toward an entry. Its whole slice of the wheel fills with its
   group color and its name shows in the middle of the wheel.
3. Let go of the hotkey. The wheel closes and the entry is used: a job's gear set is
   equipped, a window opens, an item is used, and so on.
   The stick can spring back to center first; the target stays until you flick
   toward another entry.
   Prefer the original flick-and-release? Set **Pick when** to **The stick
   returns to center** in settings, and the entry is used as soon as the stick
   springs back.

- Pressing **B** closes the wheel with no change, even with an entry targeted.
- Letting go of the hotkey without targeting anything closes the wheel with no change.
- Picking the job you're already on just closes the wheel.
- `/flick` opens settings (`/jobwheel` still works). `/flick chat` hides or shows the chat log.

## Job wheels

The Jobs part has two wheels, and every job has a fixed spot on its wheel, so you can
learn the directions by feel. Groups run clockwise starting from the top. If you only
want one of the two wheels, turn the other off under **Wheels to use** on the General tab.

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
| Logs | Fishing Log, Gathering Log, Crafting Log (and optionally the Fish Guide) |

- Locked jobs leave their spot empty, and nothing else moves when you unlock one.
- The **Logs** entries open those windows instead of switching job, once the game has unlocked them. Tick which ones you want under **Logs** on the **Job layout** tab: Crafting, Gathering and Fishing Log are on by default, the Fish Guide is off.
- The **Job layout** tab in settings lets you:
  - move groups around the wheel with the arrows, and choose which group sits centered at the top with **Pin to top**. With tanks pinned on the combat wheel, healers stay at the bottom unless you put them right next to tanks;
  - reorder jobs within a group (drag an icon sideways);
  - pick how strong the colors are (**Muted, Vivid, Soft, Dark**; each button shows all 18
    colors in that style);
  - click a group's swatch to pick one of the 18 palette colors, or a custom one; **Default
    colors** puts them all back;
  - tick **Use job colors** to color groups by role like the game (tanks blue, healers green,
    all DPS red), instead of per group.
- If you have a class but not its job yet (e.g. Gladiator), the class shows in the job's spot.
- The stick targets the slice it points into. Pointing at an empty slice (a locked job) targets the nearest unlocked job instead.
- The ring is a band colored by role (blue tanks, green healers, red, orange and magenta DPS, grey limited jobs, violet crafters, teal gatherers by default), with the job glyphs on top. The targeted job's slice fills with its role color and your current job has a white dot. The east and west sides split the space left between tanks and healers evenly, so slots on the two sides can be slightly wider or narrower than tank and healer slots.

## Switching

Switching equips a gear set:

- By default the plugin uses the first gear set for the job.
- You can pick a specific gear set for any job on the **Gear sets** tab in settings.
- Jobs with no gear set appear dimmed.

If the switch isn't possible, a short error toast appears and nothing else happens.
This covers being in combat, in a duty, casting, crafting, or not having a gear set.

## Shortcuts wheel

Tap RB until the Shortcuts wheel comes up, or give it its own hotkey in settings.
It starts with three groups, all of which you can change:

| Group | Shortcuts |
|---|---|
| Travel | Teleport, Return, Aether Compass |
| Windows | Duty Finder, Timers, Journal |
| Chocobo | Gysahl Greens, `/cbarding job` (Chocobo Barding plugin) |

In the **Shortcuts** settings tab:

- **+** adds a shortcut to a group. The picker has five tabs:
  - **Menus** – any main-menu window: Character, Armoury Chest, Inventory, Currency,
    Achievements, Blue Magic Spellbook, Master's Bestiary, Hunting Log, Duty Finder…
  - **Actions** – general actions (Teleport, Return, Sprint, Mount Roulette, Repair…), the Aether Compass,
    and **Hide or show chat log**. The hidden chat log stays hidden through cutscenes and zoning,
    comes back while you're typing, and is shown again when the plugin unloads. `/flick chat` does
    the same from a macro or the chat line.
  - **Items** – usable items in your bags. The wheel shows how many you have left, and fades the icon at 0.
  - **Macros** – your individual and shared macros, with their own name and icon. Handy for
    things only a macro can do, like `/hudlayout 2`. The shortcut remembers the macro's slot, so
    editing the macro changes what it does; if you clear the slot, the shortcut shows dimmed.
  - **Commands** – any chat command, with your own label and icon ID: a game command like
    `/gs change 3` or `/hudlayout 2`, or a plugin's, like `/cbarding job`. A plugin's command
    only shows on the wheel while that plugin is loaded.
- Drag a shortcut sideways to reorder it. Right-click it to change its label or icon,
  move it to another group, or remove it.
- Each group has a name, a color, arrows to move it around the wheel, and **Pin to top**.
  **Add group** makes a new one; deleting a group that has shortcuts needs Ctrl held.
- **Colors**: click a group's swatch to pick one of 18 colors (red through rose, plus grey),
  or **Custom** for any color. The **Muted / Vivid / Soft / Dark** buttons at the top show
  all 18 colors in that style; click one to switch the whole wheel to it. **Auto colors**
  gives every group a different color.
- Menus you haven't unlocked yet leave their spot empty.

## Settings

- Wheel scale
- Icon size
- Position: tick **Show wheel preview** (pick which wheel it shows next to it) and drag the wheel with the mouse, type an offset from screen center, or press **Center** to reset it
- Stick selection threshold, and whether letting go of the hotkey or the stick returning to center picks
- The hotkey, and which pad button (RB, LB, LT or RT) moves to the next wheel
- Optionally, a hotkey per wheel that opens straight to it. The shared hotkey keeps opening the wheel you last used with it
- Which wheels are turned on: **Jobs** (the combat wheel, the crafting & gathering wheel, or both) and **Shortcuts**
- Group order and which group is on top, job order within each group, each group's color, the color style, and job colors
- Gear set per job
- Shortcut groups, their colors and their shortcuts

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\FlickWheel.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).

## Notes

- Every wheel is the same size, set by **Wheel scale** and **Icon size** alone. A wheel with
  more than 24 entries draws its icons smaller to fit, instead of growing.
- While the wheel is open, Dalamud's gamepad capture (`IGamepadState.EnableGamepadNav`)
  stops controller input from reaching the game, so the right stick doesn't turn the camera.
  It's switched back off when the wheel closes. If the camera still moves, a reWASD
  shift layer on the hotkey button can remap the right stick while it's held.
- If the controller disconnects while the wheel is open, the stick reads as centered.
  If an entry was targeted it's used; otherwise letting go of the hotkey cancels.
- Zoning, cutscenes and logging out close the wheel without using anything.
