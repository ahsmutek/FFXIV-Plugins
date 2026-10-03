# FFXIV Plugins

A collection of my [Dalamud](https://github.com/goatcorp/Dalamud) plugins for
Final Fantasy XIV. Each plugin lives in its own folder.

These were all made for my own personal use. They're shared as-is, without support
or guarantees, and aren't published to any plugin repository.

Using third-party tools is against the FINAL FANTASY XIV Terms of Service; use these
at your own risk. They read game data that can change with any patch, so they may
break without notice.

Not affiliated with or endorsed by Square Enix. FINAL FANTASY XIV © SQUARE ENIX CO., LTD.

## Plugins

### [Retainer Cooldown](RetainerCooldown/)
A tiny HUD icon showing your next retainer venture as a native-looking FFXIV
cooldown, with an exact remaining-time readout, a radial cooldown sweep, and a
separate icon when every venture is done. Draggable, configurable, and dependency-free (reads
only `RetainerManager`). Command: `/retcd`.

### [Chocobo Barding](ChocoboBarding/)
Puts barding on your chocobo companion with one command, so a job macro can switch
barding along with your gearset. Equip a full set or pick a different piece per slot,
with a window that builds the macro line for you, or pick a barding per job and put
it on with `/cbarding job`. `/cbarding greens` uses Gysahl Greens from a macro. Works
with the Companion window closed, only equips barding you own, and uses the game's
normal Appearance-tab event (no hooks or memory writes). Command: `/cbarding "Sophic"`
or `/cbarding "Suzaku" "Windswept" "Ice"`.

### [Flick Wheel](FlickWheel/)
A controller radial menu for switching jobs and opening anything else (formerly Job
Wheel). Hold `Ctrl + ,`, flick the right stick toward an entry, and let go to use it.
Tap RB (or another shoulder button) to move between the combat, crafting/gathering
and shortcuts wheels; it reopens on the one you used last. Every unlocked job has a
fixed, role-grouped spot so you can learn the directions by feel. The shortcuts wheel
holds your own groups of game windows, Teleport and other actions, items (with how many
are left) and plugin commands such as `/cbarding job`. Command: `/flick` (settings).

### [Job Profiles](JobProfiles/)
Switches your HUD layout and/or runs a macro whenever your job changes, however you
change it (a gear set, `/gs`, Flick Wheel). Set one per role group (tanks, healers,
melee, crafters…) and override it for single jobs; a class uses its job's profile.
Waits out zoning, cutscenes and running macros, and can also apply on login. Command:
`/jobprofiles` (settings), `/jobprofiles apply`.

## Building

Each plugin is a standard Dalamud project. From a plugin's folder:

```sh
dotnet build -c Debug
```

Then load the built DLL from `bin\Debug\` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations). See each plugin's own
README for details.

## License

Copyright © 2026 anna-wro. Licensed under the
[GNU Affero General Public License v3.0 or later](LICENSE), the same license as
Dalamud. You're free to use, modify and share these plugins, but anything you
distribute that's based on them must also be released under the AGPL with its
source code. They come as-is, without warranty, and I'm not liable for anything that
happens from using them.
