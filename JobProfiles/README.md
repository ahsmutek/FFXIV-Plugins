# Job Profiles

Switches your HUD layout and runs macros, menus, items or chat commands whenever your
job changes, however you change it: a gear set, `/gs`, the Flick Wheel, or another
plugin. It can also run different ones from pad buttons and keys depending on your job
and the duty you're in.

## Usage

`/jobprofiles` opens settings. On the **Job change** tab there's one row per role group, with its jobs underneath:

| Group | Jobs |
|---|---|
| Tanks | PLD WAR DRK GNB |
| Healers | WHM SCH AST SGE |
| Melee DPS | MNK DRG NIN SAM RPR VPR |
| Physical Ranged DPS | BRD MCH DNC |
| Magical Ranged DPS | BLM SMN RDM PCT |
| Limited Jobs | BLU BST |
| Disciples of the Hand | CRP BSM ARM GSM LTW WVR ALC CUL |
| Disciples of the Land | MIN BTN FSH |

- Give a group a **HUD layout** (Layout 1–4, or Don't change) and a list of things to
  run. **Add** opens the same picker as Flick Wheel's shortcuts: game windows, general
  actions, chocobo commands, items, macros, and **Commands**: any chat command, game or
  plugin (`/gs change 3`, `/hudlayout 2`, `/cbarding job`…). `/wait 2` pauses the list.
  A command can be edited in place; **×** removes a line.
- Tick **Override** on a job to give it its own. Its HUD layout can stay on **Use group**
  or be set to Don't change. Untick **Use group's list** to give it its own list,
  which starts as a copy of the group's; an empty list runs nothing.
- A class uses its job's profile: Gladiator uses Paladin's, Conjurer uses White Mage's.
- When the job changes, the HUD layout switches (if it isn't already the one in use),
  then the list runs in order, each line once a macro started before it has finished.
  It waits until zoning, a cutscene or another running macro is over. Changing job again
  drops the previous job's lines that haven't run yet.
- **Also apply after logging in** applies your current job's profile once after login.
- `/jobprofiles apply` (or **Apply now**) applies your current job's profile again.
- Lines that can't run right now are skipped: an emptied macro slot, an item you're out
  of, a plugin command whose plugin isn't loaded. They show dimmed in settings.

## Buttons

The **Buttons** tab runs lists like the Job change tab's (macros, menus, items, chat
commands…) from controller buttons and keyboard keys, picking a different list
depending on your job and where you are.

- **Add binding**, then press what should trigger it:
  - a pad button or combo: hold the buttons together and let go (R3, L3, LT + R3,
    Back + X…);
  - a keyboard key, optionally with Ctrl, Shift or Alt.
  Click the combo later to record a new one; Esc cancels.
- **Paddles** never reach the game as buttons of their own. Map each one to a key the
  game doesn't use (F13–F24) in reWASD or Steam Input, and record that key.
- Each binding has **rules**, tried top to bottom; the first that fits runs its list
  (in order, like the Job change tab). A rule has:
  - **who**: anyone, a role group, or one job;
  - **where**: anywhere, outside duties, in any duty, or in one kind of duty (Dungeons,
    Trials, Raids, Ultimate Raids, Deep Dungeons, PvP…, the game's own list).
  Put specific rules first and a catch-all (Anyone, Anywhere) last.
- **Right now** under each binding shows what it would run where you're standing.
- If both R3 and LT + R3 are bound, pressing LT + R3 runs only that one. A button
  you hold for a combo still runs its own binding when pressed on its own first.
- Unbind the pad buttons you use in the game's gamepad settings; the game sees them
  too. Bound keys are hidden from the game while the plugin uses them.
- Nothing runs while you're typing in chat, or while a binding is being recorded.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\JobProfiles.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
