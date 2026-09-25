# Chocobo Barding

A tiny Dalamud plugin that puts a full barding set on your chocobo companion with one
command, so a job macro can switch barding along with your gearset.

## Features

- **One command, full set** – `/cbarding "Sophic Barding"` equips head, body and legs.
  The `Barding` suffix is optional and names are case-insensitive (`/cbarding sophic`).
- **Works with the Companion window closed** – ready for macros.
- **Owned barding only** – barding you don't own is refused, and suggestions for a
  mistyped name only list barding you own.
- **Safe to repeat** – slots already wearing the piece are skipped (re-selecting a worn
  piece would take it off).
- Uses the game's normal path: calls the Buddy agent's `ReceiveEvent` with the same
  arguments the Companion → Appearance tab sends. No hooks, signatures, memory writes
  or custom packets.

## Usage

```text
/cbarding "Sophic Barding"
```

In a job macro:

```text
/gearset change "Summoner"
/cbarding "Sophic Barding"
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
`20, slot, BuddyEquip row`, where slot is `0` Head, `1` Body, `2` Legs. `/cbarding`
looks the name up in the `BuddyEquip` sheet, checks ownership with Dalamud's
`IUnlockState`, reads the worn pieces from `CompanionInfo.BuddyEquipRowIds`, and sends
that event for each slot that needs changing.

## Debug logging

`/cbdebug` logs Buddy agent events to `/xllog`, which is how the event above was found.

- `/cbdebug on` / `/cbdebug off` / `/cbdebug status`
- `/cbdebug unlocks` – log every barding you own
