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


    // Muted versions of the game's role colors, dark enough for the gold glyphs to read on.
    private static readonly Dictionary<JobGroup, Vector4> DefaultColors = new()
    {
        [JobGroup.Tank] = new(.16f, .26f, .52f, .88f),
        [JobGroup.Healer] = new(.17f, .38f, .16f, .88f),
        [JobGroup.Melee] = new(.48f, .14f, .14f, .88f),
        [JobGroup.PhysicalRanged] = new(.50f, .24f, .10f, .88f),
        [JobGroup.Caster] = new(.42f, .13f, .33f, .88f),
        [JobGroup.Hand] = new(.33f, .22f, .45f, .88f),
        [JobGroup.Land] = new(.14f, .36f, .34f, .88f),
        [JobGroup.Limited] = new(.30f, .31f, .36f, .88f),
        [JobGroup.Logs] = new(.48f, .14f, .14f, .88f),
    };

    // Whole-wheel palettes for the settings window. The first one is the default.
    public static readonly (string Name, Dictionary<JobGroup, Vector4> Colors)[] ColorPresets =
    [
        ("Muted", DefaultColors),
        ("Vivid", new()
        {
            [JobGroup.Tank] = new(.20f, .36f, .80f, .92f),
            [JobGroup.Healer] = new(.20f, .56f, .22f, .92f),
            [JobGroup.Melee] = new(.72f, .16f, .16f, .92f),
            [JobGroup.PhysicalRanged] = new(.78f, .38f, .10f, .92f),
            [JobGroup.Caster] = new(.62f, .16f, .52f, .92f),
            [JobGroup.Limited] = new(.42f, .44f, .52f, .92f),
            [JobGroup.Logs] = new(.72f, .16f, .16f, .92f),
            [JobGroup.Hand] = new(.46f, .30f, .68f, .92f),
            [JobGroup.Land] = new(.14f, .54f, .50f, .92f),
        }),
        ("Soft", new()
        {
            [JobGroup.Tank] = new(.36f, .46f, .68f, .82f),
            [JobGroup.Healer] = new(.36f, .56f, .38f, .82f),
            [JobGroup.Melee] = new(.66f, .38f, .38f, .82f),
            [JobGroup.PhysicalRanged] = new(.68f, .50f, .34f, .82f),
            [JobGroup.Caster] = new(.60f, .38f, .56f, .82f),
            [JobGroup.Limited] = new(.50f, .52f, .56f, .82f),
            [JobGroup.Logs] = new(.66f, .38f, .38f, .82f),
            [JobGroup.Hand] = new(.52f, .44f, .64f, .82f),
            [JobGroup.Land] = new(.34f, .58f, .54f, .82f),
        }),
        ("Classic roles", new()
        {
            [JobGroup.Tank] = new(.20f, .32f, .66f, .90f),
            [JobGroup.Healer] = new(.20f, .46f, .20f, .90f),
            [JobGroup.Melee] = new(.56f, .16f, .16f, .90f),
            [JobGroup.PhysicalRanged] = new(.56f, .16f, .16f, .90f),
            [JobGroup.Caster] = new(.56f, .16f, .16f, .90f),
            [JobGroup.Limited] = new(.56f, .16f, .16f, .90f),
            [JobGroup.Logs] = new(.56f, .16f, .16f, .90f),
            [JobGroup.Hand] = new(.36f, .30f, .24f, .90f),
            [JobGroup.Land] = new(.36f, .30f, .24f, .90f),
        }),
        ("Dark", new()
        {
            [JobGroup.Tank] = new(.10f, .13f, .20f, .88f),
            [JobGroup.Healer] = new(.10f, .17f, .11f, .88f),
            [JobGroup.Melee] = new(.20f, .09f, .09f, .88f),
            [JobGroup.PhysicalRanged] = new(.20f, .13f, .08f, .88f),
            [JobGroup.Caster] = new(.18f, .08f, .15f, .88f),
            [JobGroup.Limited] = new(.14f, .14f, .16f, .88f),
            [JobGroup.Logs] = new(.20f, .09f, .09f, .88f),
            [JobGroup.Hand] = new(.14f, .11f, .19f, .88f),
            [JobGroup.Land] = new(.08f, .16f, .15f, .88f),
        }),
    ];

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

    public static Vector4 ColorOf(JobGroup group, Configuration config) =>
        config.RoleColors.TryGetValue(group, out var abgr) ? ImGui.ColorConvertU32ToFloat4(abgr) : DefaultColors[group];
}
