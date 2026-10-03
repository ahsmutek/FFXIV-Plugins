using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Shortcuts;

namespace FlickWheel;

// A wheel of your own shortcuts: game windows, Teleport and other general actions,
// items (with how many are left) and plugin commands, in groups you name and color.
internal sealed class ShortcutsModule : IWheelModule
{
    public const string WheelId = "shortcuts";

    private readonly Configuration config;
    private readonly ShortcutCatalog catalog;

    public ShortcutsModule(Configuration config, Action save, ShortcutCatalog catalog, IconCache icons, WheelOverlay overlay)
    {
        this.config = config;
        this.catalog = catalog;
        if (config.Shortcuts == null)
        {
            config.Shortcuts = Starter();
            save();
        }

        var settings = new ShortcutsSettings(config, catalog, icons, overlay, new ShortcutPicker(catalog, icons));
        Wheels = [new ShortcutsWheel(this)];
        SettingsTabs = [new("Shortcuts", settings.Draw)];
    }

    public string Id => "shortcuts";
    public string Name => "Shortcuts";
    public IReadOnlyList<IWheel> Wheels { get; }
    public IReadOnlyList<SettingsTab> SettingsTabs { get; }

    private ShortcutWheel Wheel => config.Shortcuts!;

    public void OnSettingsOpen() { }

    // A palette hue in the wheel's style, or the group's custom color.
    public static Vector4 ColorOf(ShortcutGroup group, int style) =>
        group.Hue >= 0 ? Palette.Color(group.Hue, style) : Palette.FromAbgr(group.Color);

    // What a new install starts with; everything can be changed or removed.
    private static ShortcutWheel Starter() => new()
    {
        Groups =
        [
            new ShortcutGroup
            {
                Name = "Travel",
                Hue = 8, // Teal
                Entries =
                [
                    new ShortcutEntry { Kind = ShortcutKind.GeneralAction, RowId = 7 }, // Teleport
                    new ShortcutEntry { Kind = ShortcutKind.GeneralAction, RowId = 8 }, // Return
                    new ShortcutEntry { Kind = ShortcutKind.Action, RowId = 26988 },    // Aether Compass
                ],
            },
            new ShortcutGroup
            {
                Name = "Windows",
                Hue = 11, // Blue
                Entries =
                [
                    new ShortcutEntry { Kind = ShortcutKind.MainCommand, RowId = 33 }, // Duty Finder
                    new ShortcutEntry { Kind = ShortcutKind.MainCommand, RowId = 5 },  // Timers
                    new ShortcutEntry { Kind = ShortcutKind.MainCommand, RowId = 4 },  // Journal
                ],
            },
            new ShortcutGroup
            {
                Name = "Chocobo",
                Hue = 3, // Amber
                Entries =
                [
                    new ShortcutEntry { Kind = ShortcutKind.Item, RowId = 4868 }, // Gysahl Greens
                    // Only shows while the Chocobo Barding plugin is loaded. Icon: the Companion menu.
                    new ShortcutEntry { Kind = ShortcutKind.Command, Command = "/cbarding job", Label = "Barding for my job", IconId = 49 },
                ],
            },
        ],
    };

    private sealed class ShortcutsWheel(ShortcutsModule module) : IWheel
    {
        public string Id => WheelId;
        public string Title => "Shortcuts wheel";

        public WheelRing Layout()
        {
            var wheel = module.Wheel;
            var slots = wheel.Groups
                             .SelectMany((group, index) => group.Entries.Select(entry =>
                                 new RingSlot(entry.Uid, index, ColorOf(group, wheel.Style))))
                             .ToArray();
            var pinned = wheel.Groups.Count > 0 ? Math.Clamp(wheel.PinnedGroup, 0, wheel.Groups.Count - 1) : 0;
            return new WheelRing(slots, (pinned, 0f));
        }

        // Entries that can't be used right now (a locked menu, a plugin that isn't loaded)
        // leave their slot empty. Items you're out of stay, faded.
        public List<WheelEntry> Entries(WheelRing ring)
        {
            var byUid = module.Wheel.Groups.SelectMany(g => g.Entries).ToDictionary(e => e.Uid);
            var entries = new List<WheelEntry>();
            for (var slot = 0; slot < ring.Slots.Length; slot++)
            {
                if (!byUid.TryGetValue(ring.Slots[slot].Key, out var entry) || !module.catalog.Available(entry) ||
                    module.catalog.Describe(entry) is not { } info)
                    continue;

                var count = module.catalog.Count(entry);
                entries.Add(new WheelEntry(slot, info.Name, info.IconId, () => module.catalog.Run(entry),
                                           KeyOutIcon: info.KeyOutIcon, Faded: count == 0, Count: count));
            }

            return entries;
        }
    }
}
