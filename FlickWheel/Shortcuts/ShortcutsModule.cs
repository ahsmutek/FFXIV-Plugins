using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using IconAtlasClient;
using Shortcuts;

namespace FlickWheel;

internal sealed class ShortcutsModule : IWheelModule
{
    public const string WheelId = "shortcuts";

    private readonly Configuration config;
    private readonly ShortcutCatalog catalog;

    public ShortcutsModule(Configuration config, Action save, ShortcutCatalog catalog, IconCache icons, WheelOverlay overlay,
                           IconPicker iconPicker)
    {
        this.config = config;
        this.catalog = catalog;
        if (config.Shortcuts == null)
        {
            config.Shortcuts = Starter();
            save();
        }

        settings = new ShortcutsSettings(config, this, catalog, icons, overlay, new ShortcutPicker(catalog, icons), iconPicker);
    }

    public IReadOnlyList<IWheel> Wheels => AllWheels().Select(Wrap).ToList();

    private readonly ShortcutsSettings settings;
    private readonly Dictionary<string, ShortcutsWheel> wrappers = new();

    public IEnumerable<ShortcutWheel> AllWheels() => config.MoreShortcutWheels.Prepend(config.Shortcuts!);

    public string IdOf(ShortcutWheel wheel) => wheel == config.Shortcuts ? WheelId : $"{WheelId}-{wheel.Uid}";

    public static string TitleOf(ShortcutWheel wheel) => wheel.Name.Length > 0 ? wheel.Name : "Shortcuts";

    public string NewWheel()
    {
        var wheel = new ShortcutWheel
        {
            Name = $"Wheel {config.MoreShortcutWheels.Count + 2}",
            Groups = [new ShortcutGroup { Name = "Group 1", Hue = ShortcutsSettings.FirstHue }],
        };
        config.MoreShortcutWheels.Add(wheel);
        return IdOf(wheel);
    }

    private ShortcutsWheel Wrap(ShortcutWheel wheel)
    {
        var id = IdOf(wheel);
        if (!wrappers.TryGetValue(id, out var wrapper) || wrapper.Data != wheel)
            wrappers[id] = wrapper = new ShortcutsWheel(this, wheel, id);
        return wrapper;
    }

    public void OnSettingsOpen() { }

    public static Vector4 ColorOf(ShortcutGroup group, int style) =>
        group.Hue >= 0 ? Palette.Color(group.Hue, style) : Palette.FromAbgr(group.Color);

    private static ShortcutWheel Starter() => new()
    {
        Groups =
        [
            new ShortcutGroup
            {
                Name = "Travel",
                Hue = 8,
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
                Hue = 11,
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
                Hue = 3,
                Entries =
                [
                    new ShortcutEntry { Kind = ShortcutKind.Item, RowId = 4868 }, // Gysahl Greens
                    new ShortcutEntry { Kind = ShortcutKind.Command, Command = "/cbarding job", Label = "Barding for my job", IconId = 49 },
                ],
            },
        ],
    };

    private sealed class ShortcutsWheel(ShortcutsModule module, ShortcutWheel data, string id) : IWheel
    {
        public ShortcutWheel Data => data;
        public string Id => id;
        public string Title => TitleOf(data);
        public bool DrawSettings() => module.settings.Draw(data);

        public WheelRing Layout()
        {
            var wheel = data;
            var slots = wheel.Groups
                             .SelectMany((group, index) => group.Entries.Select(entry =>
                                 new RingSlot(entry.Uid, index, ColorOf(group, wheel.Style))))
                             .ToArray();
            var pinned = wheel.Groups.Count > 0 ? Math.Clamp(wheel.PinnedGroup, 0, wheel.Groups.Count - 1) : 0;
            return new WheelRing(slots, (pinned, 0f));
        }

        public List<WheelEntry> Entries(WheelRing ring)
        {
            var byUid = data.Groups.SelectMany(g => g.Entries).ToDictionary(e => e.Uid);
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
