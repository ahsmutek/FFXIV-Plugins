# Job Profiles

Switches your HUD layout and runs things whenever your job changes, however you change it.
Also gives controller buttons and keys different actions by job and duty.

## Jobs tab

`/jobprofiles` opens settings. The list on the left has **Everyone**, **Combat jobs**, the
eight roles, and any jobs you've customised under their role.

| Role | Jobs |
|---|---|
| Tanks | PLD WAR DRK GNB |
| Healers | WHM SCH AST SGE |
| Melee DPS | MNK DRG NIN SAM RPR VPR |
| Physical Ranged DPS | BRD MCH DNC |
| Magical Ranged DPS | BLM SMN RDM PCT |
| Limited Jobs | BLU BST |
| Disciples of the Hand | CRP BSM ARM GSM LTW WVR ALC CUL |
| Disciples of the Land | MIN BTN FSH |

A role's page sets:

- **When you switch to one of its jobs**: a **HUD layout** and a list of steps, picked
  like Flick Wheel's shortcuts (menus, actions, items, emotes, macros) or typed as chat
  commands. Steps run in order; `/wait 2` pauses.
- **Buttons**: what your buttons do for this role (below).

**Combat jobs** is a page like a role's for the six combat roles (Tanks to Limited Jobs). Those
roles follow it unless they set their own: their HUD layout and steps have a **Same as Combat
jobs** option, and their button lines win over its lines.

**Everyone** has only Buttons. **+ Customise a job…** gives a job its own page, starting as
a copy of its role's. Classes use their job's settings.

- Steps wait out zoning, cutscenes and running macros. Changing job again drops the
  previous job's steps that haven't run yet.
- Steps that can't run (an empty macro slot, an item you're out of, an unloaded plugin's
  command) are skipped and shown dimmed.
- **HUD layout names** lets you name layouts 1–4 for the dropdowns.
- **Also when I log in** applies your job's settings after login; **Apply for my current
  job now** (or `/jobprofiles apply`) applies them again.

### Buttons on a page

**+ Button behaviour** adds a line: the **button**, **where** (Anywhere, Outside duties, In
any duty, or one duty type), and **what**:

- **Do something**: a list of steps.
- **Hold a key**: holds a key for as long as the button is held, e.g. to open a Flick Wheel
  wheel by its hotkey. Use unmodified keys (F13–F24) so they never clash with a Ctrl or
  Shift you're holding.

The most specific line wins: job over role over Combat jobs over Everyone, and one duty type
over any duty or outside duties, over Anywhere.

## Buttons tab

- **Add button or key**, then hold everything it takes and let go: a pad button, a combo
  (LT + R3), a key with modifiers, or any mix (Ctrl + ' + A). Esc cancels.
- The biggest combo held wins: LT + R3 doesn't also fire R3. Modifiers must match exactly.
- **Here and now** shows what a button does where you are, and from which page.
- Paddles don't reach the game as buttons; map them to unused keys (F13–F24) in reWASD or
  Steam Input and record those.
- **Hide from the game** (pad buttons, on by default): while a button does something here,
  the game doesn't see it, so you needn't unbind it. Shoulder buttons in a combo still reach
  the game for the cross hotbar. Bound keys are always hidden while in use.
- Nothing fires while you type in chat.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\JobProfiles.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
