# Job Wheel

A controller-friendly radial menu for switching jobs. Hold the shortcut, flick the
right stick toward a job, and let the stick spring back. You switch to that job
and the wheel closes. You don't press a confirm button or open a list.

## Usage

1. Hold **Ctrl + ,**. On a controller, map a button to it with reWASD or Steam Input.
   This opens the combat wheel. Hold **LB** to show the crafting and gathering wheel
   instead. It swaps back and forth as you press and let go of LB, even while the wheel is open.
2. Tilt the right stick toward a job. The targeted icon grows and gets a gold ring.
3. Let the stick return to center. The wheel closes and the job's gear set is equipped.

- Letting go of the shortcut without targeting anything closes the wheel with no change.
- Letting go of the shortcut while a job is targeted switches to it, the same as releasing the stick.
- Picking the job you're already on just closes the wheel.
- `/jobwheel` opens settings.

## Layout

There are two wheels, and every job has a fixed spot on its wheel, so you can learn
the directions by feel. Groups run clockwise starting from the top.

**Combat wheel** (tanks centered on top):

| Group | Jobs |
|---|---|
| Tanks | PLD WAR DRK GNB |
| Healers | WHM SCH AST SGE |
| Melee | MNK DRG NIN SAM RPR VPR |
| Physical ranged | BRD MCH DNC |
| Magical ranged | BLM SMN RDM PCT BLU |

**Crafting and gathering wheel** (hold LB; crafters across the top, gatherers along the bottom):

| Group | Jobs |
|---|---|
| Hand | CRP BSM ARM GSM LTW WVR ALC CUL |
| Land | MIN BTN FSH |

- Locked jobs leave their spot empty, and nothing else moves when you unlock one.
- If you have a class but not its job yet (e.g. Gladiator), the class shows in the job's spot.
- The stick targets the nearest icon, so the fewer jobs you have unlocked, the bigger each target is.
- A thin colored arc behind each group marks the role.

## Switching

Switching equips a gear set:

- By default the plugin uses the first gear set for the job.
- You can pick a specific gear set for any job under **Gear set per job** in settings.
- Jobs with no gear set appear dimmed.

If the switch isn't possible, a short error toast appears and nothing else happens.
This covers being in combat, in a duty, casting, crafting, or not having a gear set.

## Settings

- Wheel scale
- Icon size
- Offset from screen center
- Stick deadzone and selection threshold
- The shortcut
- Gear set per job

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\JobWheel.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).

## Notes

- While the wheel is open, Dalamud's gamepad capture (`IGamepadState.EnableGamepadNav`)
  stops controller input from reaching the game, so the right stick doesn't turn the camera.
  It's switched back off when the wheel closes. If the camera still moves, a reWASD
  shift layer on the shortcut button can remap the right stick while it's held.
- If the controller disconnects while the wheel is open, the stick reads as centered.
  If a job was targeted it's confirmed; otherwise letting go of the shortcut cancels.
- Zoning, cutscenes and logging out close the wheel without switching.
