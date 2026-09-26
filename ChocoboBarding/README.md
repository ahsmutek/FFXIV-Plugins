# Chocobo Barding

A tiny Dalamud plugin that puts barding on your chocobo companion with one command,
so a job macro can switch barding along with your gearset.

## Features

- **Full set or mix & match** – `/cbarding "Sophic"` equips head, body and legs;
  `/cbarding "Suzaku" "Windswept" "Ice"` picks a different piece per slot.
  Names are shown without the `Barding` suffix; typing it is optional, and names are
  case-insensitive (`/cbarding sophic`).
- **Keep or remove a slot** – `-` leaves a slot as it is, `none` takes that piece off.
- **Barding per job** – pick a barding for each job (and crafter/gatherer) in the **Jobs**
  tab, then `/cbarding job` puts on the one for the job you're on. Put it on a hotbar
  and press it only when you want your chocobo to match; switching jobs alone changes
  nothing. Base classes use their job's pick (Arcanist → Summoner/Scholar).
- **Works with the Companion window closed** – ready for macros.
- **Barding window** – `/cbarding` with no name (or the plugin's Settings / Open button)
  opens it. The **Barding** tab has:
  - **Mix & match** – a column per slot with a large preview icon and a dropdown of
    your barding (plus Keep current / None); it builds the macro line with a Copy button.
  - **Full sets** – every barding you own with its head/body/legs icons, macro line and
    a Copy button, with a filter box.

  The **Jobs** tab has a row per job with a Full set dropdown and Head/Body/Legs
  dropdowns. Your current job is highlighted.
- **Owned barding only** – barding you don't own is refused, and suggestions for a
  mistyped name only list barding you own. Nothing changes unless every slot is valid.
- **Human pace** – pieces change one at a time: each change waits until the game shows
  the previous one, then pauses briefly. A new command replaces changes not yet made.
- **Safe to repeat** – slots already wearing the piece are skipped (re-selecting a worn
  piece would take it off).
- Uses the game's normal path: calls the Buddy agent's `ReceiveEvent` with the same
  arguments the Companion → Appearance tab sends. No hooks, signatures, memory writes
  or custom packets.

## Usage

```text
/cbarding "Sophic"                          full set
/cbarding "Suzaku" "Windswept" "Ice"        head, body, legs
/cbarding - - "Ice"                         legs only
/cbarding "Suzaku" none none                Suzaku head, body and legs removed
/cbarding none                              remove all barding
/cbarding job                               barding picked for your current job
```

Give either one name (full set) or three (head, body, legs). Use quotes around names
when giving three.

Run `/cbarding` on its own to open the barding window and copy a macro line.

In a job macro:

```text
/gearset change "Summoner"
/cbarding "Sophic"
```

## Build

Requires a current Dalamud/XIVLauncher development environment and the .NET SDK
(tested with .NET 10).

```sh
dotnet build -c Debug
```

Then load `bin\Debug\ChocoboBarding.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).

## How it works

Clicking a piece in Companion → Appearance sends the Buddy agent event
`20, slot, BuddyEquip row`, where slot is `0` Head, `1` Body, `2` Legs. Selecting the
piece a slot already wears takes it off. `/cbarding` looks names up in the `BuddyEquip`
sheet, checks ownership with Dalamud's `IUnlockState`, reads the worn pieces from
`CompanionInfo.BuddyEquipRowIds`, and sends that event once per slot that needs
changing, waiting for each change to show before the next.

## Debug logging (Debug builds only)

`/cbdebug` logs Buddy agent events to `/xllog`, which is how the event above was found.
It isn't compiled into Release builds.

- `/cbdebug on` / `/cbdebug off` / `/cbdebug status`
- `/cbdebug unlocks` – log every barding you own
