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

- Give a group a **HUD layout** (Layout 1–4, or Don't change) and/or a **macro**
  (any individual or shared macro, or None). Type in the box at the top of the macro
  list to search your macros by name.
- Tick **Override** on a job to give it its own. Each of its two choices can stay on
  **Use group**, or be set to Don't change / None to skip what the group would do.
- A class uses its job's profile: Gladiator uses Paladin's, Conjurer uses White Mage's.
- When the job changes, the HUD layout switches (if it isn't already the one in use),
  then the macro runs. It waits until zoning, a cutscene or another running macro is over.
- **Also apply after logging in** applies your current job's profile once after login.
- `/jobprofiles apply` (or **Apply now**) applies your current job's profile again.
- If a picked macro's slot has been cleared, a toast says so and nothing runs.

The macro can do anything a macro can, e.g. `/hudlayout`, `/gs`, `/echo`, or plugin
commands such as `/cbarding job`.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\JobProfiles.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
