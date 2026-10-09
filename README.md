# FFXIV Plugins

My [Dalamud](https://github.com/goatcorp/Dalamud) plugins for Final Fantasy XIV. Each plugin
has its own folder; `shared/` holds code compiled into more than one (the shortcut picker
used by Flick Wheel and Job Profiles).

Made for my own use and shared as-is, without support or guarantees. Not published to any
plugin repository.

Third-party tools are against the FINAL FANTASY XIV Terms of Service; use at your own risk.
These read game data that can change with any patch, so they may break without notice.

Not affiliated with or endorsed by Square Enix. FINAL FANTASY XIV © SQUARE ENIX CO., LTD.

## Plugins

### [Retainer Cooldown](RetainerCooldown/)
Your next retainer venture as a native-looking cooldown icon on the HUD, with exact
remaining time and a separate icon once every venture is done. `/retcd`

### [Chocobo Barding](ChocoboBarding/)
Puts barding on your chocobo with one command, so a job macro can switch it with your gear
set: a full set (`/cbarding "Sophic"`), one piece per slot (`/cbarding "Suzaku" "Windswept" "Ice"`),
or the one picked for your job (`/cbarding job`). Only equips barding you own, and works
with the Companion window closed.

### [Flick Wheel](FlickWheel/)
A controller radial menu (formerly Job Wheel). Hold `Ctrl + ,`, flick the right stick
toward an entry, let go. Wheels for combat jobs, crafting/gathering jobs, and your own
shortcuts: windows, actions, items, emotes, macros and chat commands. `/flick`

### [Job Profiles](JobProfiles/)
Switches HUD layout and runs macros or commands whenever your job changes, set per role
with per-job overrides. Also gives pad buttons, combos and keys different actions by job
and duty type. `/jobprofiles`

### [Minimap Zoom](MinimapZoom/)
Lets the minimap zoom out past the game's 1.25 limit, down to 0.25, and keeps your zoom
across job changes, zoning and logins. `/minimapzoom`

### [Keep in Mind](KeepInMind/)
A checklist of ingredients to gather or buy by hand. Right-click an item in the
Crafting or Gathering Log and choose **Keep in mind**; the list groups and colours items by
where you get them. `/keepinmind`

### [ControllerKeeper](ControllerKeeper/)
Stops pad freezes when unrelated hardware (a USB stick, a virtual drive) comes and goes,
and keeps the game on the controller you picked. `/controllerkeeper`

## Building

From a plugin's folder:

```sh
dotnet build -c Debug
```

Then add the DLL from `bin\Debug\` as a dev plugin (`/xlsettings` → Experimental → Dev
Plugin Locations).

## License

Copyright © 2026 anna-wro. Licensed under the
[GNU Affero General Public License v3.0 or later](LICENSE), the same license as Dalamud.
Anything you distribute that's based on these must also be released under the AGPL with
its source. Provided as-is, without warranty.

Icons are from [Tabler Icons](https://github.com/tabler/tabler-icons) (MIT, see
[shared/Icons/LICENSE-tabler-icons.txt](shared/Icons/LICENSE-tabler-icons.txt)).
