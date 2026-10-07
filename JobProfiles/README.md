# Job Profiles

Switches your HUD layout and does things for you whenever your job changes, however you
change it: a gear set, `/gs`, the Flick Wheel, or another plugin. It can also give your
controller buttons and keys different actions depending on your job and the duty you're in.

## Job change tab

`/jobprofiles` opens settings. The **Job change** tab has one row per role, with its jobs
underneath:

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

- For each role, pick a **HUD layout** (Layout 1–4, or Don't change) and what to do
  under **Then do**. **Add** opens the same picker as Flick Wheel's shortcuts: game
  menus, actions, chocobo commands, items, emotes, macros, and **Commands**: any chat
  command, game or plugin (`/gs change 3`, `/hudlayout 2`, `/cbarding job`…). They run
  in order; a `/wait 2` command pauses before the next one. A command can be edited in
  place, and **×** removes a line.
- To set up one job differently from its role, tick **Override** next to it. Its HUD
  layout can stay **Same as group** or be set on its own. Untick **Same as group** under
  Then do to give it its own list, which starts as a copy of the role's; an empty list
  does nothing.
- Classes follow their job: Gladiator uses Paladin's settings, Conjurer White Mage's.
- When your job changes, the HUD layout switches (if it isn't already the one in use),
  then the list runs, each line once a macro started before it has finished. It waits
  until zoning, a cutscene or a running macro is over. Changing job again drops the
  previous job's lines that haven't run yet.
- **Also when I log in** does your current job's settings once after logging in.
  **Apply for my current job now** (or `/jobprofiles apply`) does them again.
- Lines that can't run right now are skipped: an emptied macro slot, an item you're out
  of, a plugin command whose plugin isn't loaded. They show dimmed in settings.

## Buttons tab

Makes a controller button, a button combo or a keyboard key do different things
depending on your job and where you are.

- **Add button or key**, then press it:
  - a controller button or combo: hold the buttons together and let go (R3, L3,
    LT + R3, Back + X…);
  - a keyboard key, optionally with Ctrl, Shift or Alt.
  Click it later to record a different one; Esc cancels.
- Each button has **rules**, checked from the top; the first that fits is used. A rule
  says:
  - **who**: Anyone, a role, or one job;
  - **where**: Anywhere, Outside duties, In any duty, or in one kind of duty (Dungeons,
    Trials, Raids, Ultimate Raids, Deep Dungeons, PvP… from the game's own list);
  - **what**: **Do something** (a list, like on the Job change tab) or **Hold a key**:
    the key (recorded by pressing it) is held down for as long as the button is. Use that
    to open a Flick Wheel wheel through its own hotkey: hold L3, flick, let go. Give
    those wheels hotkeys without a modifier (F13–F24), so letting go never confuses a
    Ctrl or Shift you're holding yourself.
  Put specific rules first and a catch-all (Anyone, Anywhere) last.
- **Here and now** under each button shows what it would do where you're standing, and
  which rule that is.
- With both R3 and LT + R3 set up, pressing LT + R3 does only that one. A button you
  hold for a combo still does its own thing when pressed on its own first.
- **Paddles** never reach the game as buttons of their own. Map each one to a key the
  game doesn't use (F13–F24) in reWASD or Steam Input, and record that key.
- Unbind the controller buttons you use here in the game's controller settings; the game
  sees them too. Keys you set up here are hidden from the game while you use them.
- Nothing happens while you're typing in chat, or while a button is being recorded.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\JobProfiles.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
