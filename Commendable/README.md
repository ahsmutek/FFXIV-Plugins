# Commendable

Remembers who helped you during a duty, so the player commendation at the end is an easy choice. A memory aid
only: no scores or rankings, and it never commends for you.

## Usage

- Play a duty as usual. When it's complete, the summary opens beside the game without taking the controller or
  keyboard. It lists what each player chose to do for you in particular; party-wide heals, mitigation and buffs
  are left out.
  - **Relationships:** who selected you as Dance Partner or Kardion target, or bonded with you through Synastry or
    Fey Union, and for how long. In blue, whom you selected.
  - **Personal assistance:** raises (accepted, or offered and not taken), healer limit breaks that revived you,
    protection such as Cover, The Blackest Night or Aquaveil, astrologian cards (*Received 8 cards — 5 offensive,
    3 defensive/healing*), Esuna that removed a debuff, The Warden's Paean, and Rescue.
  - **Limit breaks:** who used which one, whoever it hit.
  - **Routine support:** single-target heals. Only heals from non-healers show by default, in green; settings can
    show everyone's or none.
- Click a player for their event log: each entry with its time and the exact action used. Players who left are
  dimmed and marked *left*; players with nothing recorded are hidden.
- **Bookmark** someone you appreciated for any reason: from their right-click menu (party list, chat, nameplate),
  the bookmark button in the summary, or `/commendable mark` on your target. Right-click a player in the summary
  (or press Y) to add a short note.
- `/commendable` shows the summary or brings it back from the game; run it while you're using it to hide it. Until
  the duty is complete it only shows or hides it, and the controller and keyboard stay with the game even if you
  click it. It closes by itself when you commend someone (or close the game's commendation window) and when you
  leave the duty; `/commendable last` shows that duty again, `/commendable config` the settings.
- Sort by party order, bookmarks first, most recent or name. Settings turn each kind of help on or off, and add
  compact rows, waiting for the game's commendation window before opening, and chat lines when someone raises or
  selects you (only you see them).

Works in dungeons, trials, raids and alliance raids, queued directly or through a roulette. Commending is still
done in the game's own window.

## Saved duties

The last 3 duties (change it in settings) are kept on this PC in
`%APPDATA%\XIVLauncher\pluginConfigs\Commendable\Journal`, so `/commendable replay` can show one again (`replay 2`
for the one before). They hold what the plugin saw: party names, worlds and jobs, actions between other players and
you, every player's limit breaks (strangers too, in field content), and party members' statuses. A plugin reload or
reconnect mid-duty carries on with the same file.

From this folder, `dotnet run --project Replay -- [n] [--log] [--raw]` prints the same summary without the game;
`--log` adds each player's event log, `--raw` the events behind the tracked lines.

## How it works

- Actions come from the game's action results, through a hook that only reads them, and are matched by ID, so
  any client language works. Credit for a raise goes to the source of the Raise status you came back with, so with
  two raisers it's the one you accepted. Cards are told apart by the status they put on you.
- Relationships come from the partner status on a party member and who it's from; refreshes and heal ticks don't
  count again. Pets count as their owner, so a fairy's Fey Union and the demi-Phoenix's Rekindle go to the summoner.
- Everything is saved as observed, then turned into the summary by code that doesn't need the game, the same code
  replays use.
- Nothing is sent anywhere: no network access, no actions, no messages to other players, no clicks in game windows.
  The commendation window is only watched for opening and closing, and read for its position.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\Commendable.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
