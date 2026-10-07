# Chocobo Barding

Puts barding on your chocobo companion with one command, so a job macro can switch it along
with your gear set.

## Usage

```text
/cbarding "Sophic"                          full set
/cbarding "Suzaku" "Windswept" "Ice"        head, body, legs
/cbarding - - "Ice"                         legs only (- keeps a slot)
/cbarding "Suzaku" none none                Suzaku head, body and legs removed
/cbarding none                              remove all barding
/cbarding job                               barding picked for your current job
/cbarding greens                            use Gysahl Greens (summon your chocobo)
/cbarding                                   open the barding window
```

Give one name (full set) or three, quoted. Names are case-insensitive and the `Barding`
suffix is optional.

In a job macro:

```text
/gearset change "Summoner"
/cbarding "Sophic"
```

Summon your chocobo dressed for your job:

```text
/micon "Gysahl Greens" item
/cbarding greens
/wait 1
/cbarding job
```

## Barding window

- **Barding** tab: build a mix-and-match line slot by slot, or browse your full sets, each
  with a Copy button for the macro line.
- **Jobs** tab: pick barding per job for `/cbarding job` (base classes use their job's).
  **Match my job automatically** applies it when you summon your chocobo or change job
  while it's out, after combat ends.
- **Pets** tab (Arcanist, Summoner or Scholar): `/petglamour` lines for every Carbuncle and
  Eos look, and `/petsize` lines for Summoner's big summons.

## Behaviour

- Only barding you own; nothing changes unless every slot is valid.
- Works with the Companion window closed.
- Pieces change one at a time, each after the game shows the last. A new command replaces
  changes not yet made, and slots already wearing the piece are skipped.

## How it works

Clicking a piece in Companion → Appearance sends the Buddy agent event
`20, slot, BuddyEquip row` (slot `0` head, `1` body, `2` legs); selecting the piece a slot
already wears takes it off. The plugin sends that same event, and uses greens like the
inventory's Use menu. No hooks, signatures, memory writes or custom packets.

Debug builds add `/cbdebug on|off|status|unlocks`, which logs Buddy agent events to `/xllog`.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\ChocoboBarding.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
