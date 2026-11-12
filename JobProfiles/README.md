# Job Profiles

Switches your HUD layout, mount and minion whenever your job changes, however you change it.
Also gives controller buttons and keys different actions by job and duty.

## Jobs tab

`/jobprofiles` opens settings. The list on the left has **All jobs**, **Combat jobs** with its six roles under it, **Disciples of
the Hand** and **Disciples of the Land**. Jobs you've customised sit under their role, which can
fold them away. **+ Customise a job** gives a job its own page. Classes use their job's settings.

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

A page has a card per setting; a card's title folds it, and **Buttons** starts folded.

- **HUD layout**: switched to when you change to one of its jobs. The pencil next to it names
  layouts 1–4 for the dropdowns.
- **Mounts**: one is drawn at random when you switch job, and Mount Roulette and Flying Mount
  Roulette ride it until your next job change, from any hotbar, the Mount Guide or a
  `/gaction "Mount Roulette"` macro. The mount you're riding is never swapped. With none chosen,
  the roulette works as usual.
- **Minions**: a random one is summoned when you switch job, never the one already out. Nothing
  happens where minions aren't allowed.
- **Buttons**: what your buttons do for this page (below).

A job follows its role, and a combat role follows **Combat jobs**. Their pages say what they
inherit from and what they override, and each card has **Inherit** and **Customize**:
Customize starts from what it inherited, or opens the picker when that's nothing; mounts or minions
saved with none chosen go back to Inherit. A card with nothing to show doesn't open. Button lines
don't inherit as a whole; the most specific line for a button wins (below).

The pencil chip on Mounts or Minions opens your mounts or minions as a grid like the game's
guides: D-pad to move, A to tick, X to search. The ones already chosen come first; **Hide
chosen** leaves only the rest. **Type** (a minion's race or behaviour, a mount's seats) narrows
the grid, **Tick all shown** and **Untick all shown** act on what's shown, and **Copy from…** adds
another role's or job's list. Nothing changes until **Save** (or Y); **Cancel** or B leaves the
list as it was.

Right-clicking a mount in the Mount Guide or a minion in the Minion Guide offers **Job Profiles**:
tick the roles and jobs that use it, then **Save** (or Y). A faint box follows the page above;
changing it gives that page its own list, starting from what it inherited. Your current job is
highlighted.

At the top of a page, the copy button copies another page's HUD layout, mounts, minions and, if
ticked, button lines into this one, as they are on that page now. On a job, the bin stops
customising it after asking: it follows its role again and loses its own settings and lines.

**All jobs** has only Buttons.

### Buttons on a page

**+ Line** on the Buttons card adds a line: the **button**, **where**, and **what**. **Where** is
Anywhere, or any of Outside duties, In any duty, In combat, In a party and the duty types; one
of them being true is enough. **What** is:

- **Do something**: a list of steps, picked like Flick Wheel's shortcuts (menus, actions,
  items, emotes, macros) or typed as chat commands. Steps run in order; `/wait 2` pauses. They
  wait out zoning, cutscenes and running macros, and changing job drops those that haven't run
  yet. Steps that can't run (an empty macro slot, an item you're out of, an unloaded plugin's
  command) are skipped and shown dimmed.
- **Hold a key**: holds a key for as long as the button is held. Use unmodified keys
  (F13–F24) so they never clash with a Ctrl or Shift you're holding.
- **Open Flick Wheel**: holds one of [Flick Wheel](../FlickWheel/)'s hotkeys open, chosen by
  name, while the button is held: hold, flick, let go. The hotkey doesn't need a key.

The most specific line wins: job over role over Combat jobs over All jobs, and only duty types
over other places, over Anywhere.

## Settings tab

- **Auto-apply on job change** turns the Jobs tab on or off; **Apply on login** also applies your
  job's settings a few seconds after you log in. **Apply for my current job now** (or
  `/jobprofiles apply`) applies them again.
- **Use controller buttons and keys** turns everything on the Buttons tab on or off.

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
