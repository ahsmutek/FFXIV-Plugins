using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FlickWheel;

public enum JobGroup
{
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    Caster,
    Hand,
    Land,
    Limited,
    Logs,
}

public enum WheelKind
{
    Combat,
    Crafting,
}

internal static class JobLayout
{
    // Groups in clockwise order, each with its jobs in their default clockwise order.
    // Tanks sit centered north and healers centered south. Melee and the limited
    // jobs fill the east side; physical ranged and casters fill the west.
    private static readonly (JobGroup Group, string[] Jobs)[] CombatGroups =
    [
        (JobGroup.Tank, ["PLD", "WAR", "DRK", "GNB"]),
        (JobGroup.Melee, ["MNK", "DRG", "NIN", "SAM", "RPR", "VPR"]),
        (JobGroup.Limited, ["BLU", "BST"]),
        (JobGroup.Healer, ["WHM", "SCH", "AST", "SGE"]),
        (JobGroup.PhysicalRanged, ["BRD", "MCH", "DNC"]),
        (JobGroup.Caster, ["BLM", "SMN", "RDM", "PCT"]),
    ];

    // Crafters across the top, then clockwise the gatherers, then the gathering and
    // crafting logs on the west side (each next to the jobs it belongs to).
    private static readonly (JobGroup Group, string[] Jobs)[] CraftingGroups =
    [
        (JobGroup.Hand, ["CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL"]),
        (JobGroup.Land, ["MIN", "BTN", "FSH"]),
        (JobGroup.Logs, ["GATHERLOG", "CRAFTLOG"]),
    ];


    // Wheel entries that open a game window (a main command) instead of switching job.
    public static readonly Dictionary<string, uint> Commands = new()
    {
        ["CRAFTLOG"] = 9, // Crafting Log
        ["GATHERLOG"] = 7, // Gathering Log
    };

    // Palette hue per group (see Palette.Hues) until you pick another: close to the game's
    // role colors, with the three DPS kinds told apart.
    private static readonly Dictionary<JobGroup, int> DefaultHues = new()
    {
        [JobGroup.Tank] = 11,           // Blue
        [JobGroup.Healer] = 6,          // Green
        [JobGroup.Melee] = 0,           // Red
        [JobGroup.PhysicalRanged] = 2,  // Orange
        [JobGroup.Caster] = 15,         // Magenta
        [JobGroup.Limited] = 17,        // Grey
        [JobGroup.Hand] = 13,           // Violet
        [JobGroup.Land] = 8,            // Teal
        [JobGroup.Logs] = 0,            // Red
    };

    // "Use job colors": the game's own role colors, every DPS red.
    private static readonly Dictionary<JobGroup, int> JobColorHues = new()
    {
        [JobGroup.Tank] = 11,           // Blue
        [JobGroup.Healer] = 6,          // Green
        [JobGroup.Melee] = 0,           // Red
        [JobGroup.PhysicalRanged] = 0,
        [JobGroup.Caster] = 0,
        [JobGroup.Limited] = 17,        // Grey
        [JobGroup.Hand] = 13,           // Violet
        [JobGroup.Land] = 3,            // Amber
        [JobGroup.Logs] = 0,            // Red
    };

    // Every slot the wheel has, locked jobs included.
    public static int SlotCount(WheelKind kind) =>
        (kind == WheelKind.Crafting ? CraftingGroups : CombatGroups).Sum(g => g.Jobs.Length);

    public static IEnumerable<JobGroup> GroupsOf(WheelKind kind) =>
        (kind == WheelKind.Crafting ? CraftingGroups : CombatGroups).Select(g => g.Group);

    // The group pinned to the top of the wheel: the player's pick, else tanks / crafters.
    public static JobGroup NorthOf(WheelKind kind, Configuration config) =>
        config.NorthGroup.TryGetValue(kind, out var group) && GroupsOf(kind).Contains(group)
            ? group
            : GroupsOf(kind).First();

    // The north group is centered at the top. With tanks on top of the combat wheel,
    // healers are also pinned to the bottom, opposite them.
    private static (int, float)[] AnchorsOf(WheelKind kind, Configuration config)
    {
        var north = NorthOf(kind, config);
        return kind == WheelKind.Combat && north == JobGroup.Tank
            ? [((int)JobGroup.Tank, 0f), ((int)JobGroup.Healer, MathF.PI)]
            : [((int)north, 0f)];
    }

    // The wheel's groups in the player's saved clockwise order, sanitized like OrderOf.
    public static List<JobGroup> GroupOrderOf(WheelKind kind, Configuration config)
    {
        var defaults = GroupsOf(kind).ToList();
        var order = new List<JobGroup>();
        if (config.GroupOrder.TryGetValue(kind, out var saved))
            order.AddRange(saved.Where(defaults.Contains).Distinct());
        order.AddRange(defaults.Where(group => !order.Contains(group)));
        return order;
    }

    public static WheelRing Build(WheelKind kind, Configuration config)
    {
        var groups = GroupOrderOf(kind, config);
        var slots = groups.SelectMany(g => OrderOf(g, config).Select(job => new RingSlot(job, (int)g, ColorOf(g, config))))
                          .ToArray();
        return new WheelRing(slots, AnchorsOf(kind, config));
    }

    // The group's jobs in the player's saved order. Unknown entries are dropped and
    // jobs missing from the saved order (e.g. added in an update) go on the end.
    public static List<string> OrderOf(JobGroup group, Configuration config)
    {
        var defaults = Defaults(group);
        var order = new List<string>();
        if (config.JobOrder.TryGetValue(group, out var saved))
            order.AddRange(saved.Where(defaults.Contains).Distinct());
        order.AddRange(defaults.Where(job => !order.Contains(job)));
        return order;
    }

    public static string[] Defaults(JobGroup group) =>
        CombatGroups.Concat(CraftingGroups).First(g => g.Group == group).Jobs;

    // The group's palette hue: the job color when that option is on, else the one picked
    // for the group, else its default. -1 means a custom color (RoleColors).
    public static int HueOf(JobGroup group, Configuration config) =>
        config.UseJobColors ? JobColorHues[group]
        : config.RoleHues.TryGetValue(group, out var hue) ? hue
        : config.RoleColors.ContainsKey(group) ? -1
        : DefaultHues[group];

    public static Vector4 ColorOf(JobGroup group, Configuration config) =>
        HueOf(group, config) is var hue and >= 0
            ? Palette.Color(hue, config.RoleStyle)
            : Palette.FromAbgr(config.RoleColors[group]);
}
