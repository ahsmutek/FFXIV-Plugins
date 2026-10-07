# Job Profiles

Switches your HUD layout and does things for you whenever your job changes, however you
change it: a gear set, `/gs`, the Flick Wheel, or another plugin. It can also give your
controller buttons and keys different actions depending on your job and the duty you're in.

## Jobs tab

`/jobprofiles` opens settings. On the **Jobs** tab, the list on the left has **Everyone**,
the eight roles, and under each role any job you've customised. Pick one to set it up on
the right.

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

A role's page has:

- **When you switch to one of its jobs**: a **HUD layout** (Layout 1–4, or Don't change)
  and a list of things to do. **Add** opens the same picker as Flick Wheel's shortcuts:
  game menus, actions, chocobo commands, items, emotes, macros, and **Commands**: any
  chat command, game or plugin (`/gs change 3`, `/hudlayout 2`, `/cbarding job`…). They
  run in order; a `/wait 2` command pauses before the next one. A command can be edited
  in place, and **×** removes a line.
- **Buttons**: what your buttons do for this role (see below).

**Everyone** has only Buttons: behaviour for every job. A job follows its role until you
**+ Customise a job…**; its page then has the same sections, each starting as **Same as**
its role, plus **Stop customising** to make it follow the role again. Classes follow their
job: Gladiator uses Paladin's settings.

- When your job changes, the HUD layout switches (if it isn't already the one in use),
  then the list runs, each line once a macro started before it has finished. It waits
  until zoning, a cutscene or a running macro is over. Changing job again drops the
  previous job's lines that haven't run yet.
- **HUD layout names** (at the top, folded) lets you name layouts 1–4, e.g. "Healer" or
  "Crafting"; the dropdowns then show "2 · Healer". The names are only used here; the game
  itself numbers its layouts.
- **Also when I log in** does your current job's settings once after logging in.
  **Apply for my current job now** (or `/jobprofiles apply`) does them again.
- Lines that can't run right now are skipped: an emptied macro slot, an item you're out
  of, a plugin command whose plugin isn't loaded. They show dimmed in settings.

### Buttons on a page

**+ Button behaviour** adds a line: which **button**, **where** (Anywhere, Outside duties,
In any duty, or one kind of duty — Dungeons, Trials, Raids, Ultimate Raids, Deep Dungeons,
PvP… from the game's own list), and **what**:

- **Do something**: a list, like the one above.
- **Hold a key**: the key (recorded by pressing it) is held down for as long as the button
  is. Use it to open a Flick Wheel wheel through its own hotkey: hold L3, flick, let go.
  Give those wheels hotkeys without a modifier (F13–F24), so letting go never confuses a
  Ctrl or Shift you're holding yourself.

When several pages set the same button, the most specific wins: a job beats its role, a
role beats Everyone, and a kind of duty (In Raids) beats In any duty or Outside duties,
which beat Anywhere. So "Everyone, Anywhere: Emotes wheel" plus "Healers, In Raids: Raid
wheel" gives healers the raid wheel in raids and everyone the emotes wheel elsewhere.

## Buttons tab

Your buttons and keys themselves.

- **Add button or key**, then hold everything it takes and let go: a controller button
  (R3, L3…), a combo (LT + R3, LB + Start…), a key with or without Ctrl, Shift or Alt, or
  any mix of keys and buttons (Ctrl + ' + K; Ctrl + ' from a paddle + A). Click it later
  to record a different one; Esc cancels.
- Give it a **name** (e.g. "Emotes wheel") to find it easily on the pages.
- **Here and now** shows what it would do where you're standing and which page that comes
  from. **Set up in N places** unfolds into everything it does: who, where and what.
- The biggest combo held wins: with both R3 and LT + R3 set up, pressing LT + R3 does only
  that one, and Ctrl + ' + A beats Ctrl + '. A part you hold first still does its own
  thing when pressed on its own. Modifiers must match exactly: a plain ' doesn't fire while
  Ctrl is held.
- **Paddles** never reach the game as buttons of their own. Map each one to a key the
  game doesn't use (F13–F24) in reWASD or Steam Input, and record that key.
- **Hide from the game** (on by default, controller buttons only): while a button does
  something where you are, the game doesn't see it, so you don't have to unbind it in the
  game's settings. Where nothing is set up for it, the game gets it as usual. In a combo,
  LB, LT, RB and RT still reach the game, since the cross hotbar needs them. Keys you set up
  here are always hidden from the game while you use them.
- Nothing happens while you're typing in chat, or while a button is being recorded.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\JobProfiles.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
