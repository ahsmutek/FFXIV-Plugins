# Job Profiles

Switches your HUD layout and/or runs a macro whenever your job changes, however you
change it: a gear set, `/gs`, the Flick Wheel, or another plugin.

## Usage

`/jobprofiles` opens settings. There's one row per role group, with its jobs underneath:

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

- Give a group a **HUD layout** (Layout 1–4, or Don't change) and any number of
  **macros** (individual or shared). **Add macro** adds one, its dropdown picks which
  (type in the box at the top to search by name), and **×** removes it.
- Tick **Override** on a job to give it its own. Its HUD layout can stay on **Use group**
  or be set to Don't change. Untick **Use group's macros** to give it its own list,
  which starts as a copy of the group's; an empty list runs no macros.
- A class uses its job's profile: Gladiator uses Paladin's, Conjurer uses White Mage's.
- When the job changes, the HUD layout switches (if it isn't already the one in use),
  then the macros run in order, each once the one before has finished. It waits until
  zoning, a cutscene or another running macro is over. Changing job again drops the
  previous job's macros that haven't run yet.
- **Also apply after logging in** applies your current job's profile once after login.
- `/jobprofiles apply` (or **Apply now**) applies your current job's profile again.
- If a picked macro's slot has been cleared, a toast says so and that macro is skipped.

A macro can do anything a macro can, e.g. `/hudlayout`, `/gs`, `/echo`, or plugin
commands such as `/cbarding job`.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\JobProfiles.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
