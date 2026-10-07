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
    private static readonly (JobGroup Group, string[] Jobs)[] CombatGroups =
    [
        (JobGroup.Tank, ["PLD", "WAR", "DRK", "GNB"]),
        (JobGroup.Melee, ["MNK", "DRG", "NIN", "SAM", "RPR", "VPR"]),
        (JobGroup.Limited, ["BLU", "BST"]),
        (JobGroup.Healer, ["WHM", "SCH", "AST", "SGE"]),
        (JobGroup.PhysicalRanged, ["BRD", "MCH", "DNC"]),
        (JobGroup.Caster, ["BLM", "SMN", "RDM", "PCT"]),
    ];

    private static readonly (JobGroup Group, string[] Jobs)[] CraftingGroups =
    [
        (JobGroup.Hand, ["CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL"]),
        (JobGroup.Land, ["MIN", "BTN", "FSH"]),
        (JobGroup.Logs, ["FISHLOG", "FISHGUIDE", "GATHERLOG", "CRAFTLOG"]),
    ];

    public static readonly Dictionary<string, uint> Commands = new()
    {
        ["CRAFTLOG"] = 9,   // Crafting Log
        ["GATHERLOG"] = 7,  // Gathering Log
        ["FISHLOG"] = 29,   // Fishing Log
        ["FISHGUIDE"] = 41, // Fish Guide
    };

    private static readonly HashSet<string> HiddenLogs = ["FISHGUIDE"];

    public static bool IsShown(string key, Configuration config) =>
        !Commands.ContainsKey(key) ||
        (config.ShownLogs.TryGetValue(key, out var shown) ? shown : !HiddenLogs.Contains(key));

    private static readonly Dictionary<JobGroup, int> DefaultHues = new()
    {
        [JobGroup.Tank] = 11,
        [JobGroup.Healer] = 6,
        [JobGroup.Melee] = 0,
        [JobGroup.PhysicalRanged] = 2,
        [JobGroup.Caster] = 15,
        [JobGroup.Limited] = 17,
        [JobGroup.Hand] = 13,
        [JobGroup.Land] = 8,
        [JobGroup.Logs] = 0,
    };

    private static readonly Dictionary<JobGroup, int> JobColorHues = new()
    {
        [JobGroup.Tank] = 11,
        [JobGroup.Healer] = 6,
        [JobGroup.Melee] = 0,
        [JobGroup.PhysicalRanged] = 0,
        [JobGroup.Caster] = 0,
        [JobGroup.Limited] = 17,
        [JobGroup.Hand] = 13,
        [JobGroup.Land] = 3,
        [JobGroup.Logs] = 0,
    };

    public static IEnumerable<JobGroup> GroupsOf(WheelKind kind) =>
        (kind == WheelKind.Crafting ? CraftingGroups : CombatGroups).Select(g => g.Group);

    public static JobGroup NorthOf(WheelKind kind, Configuration config) =>
        config.NorthGroup.TryGetValue(kind, out var group) && GroupsOf(kind).Contains(group)
            ? group
            : GroupsOf(kind).First();

    private static (int, float)[] AnchorsOf(WheelKind kind, Configuration config)
    {
        var north = NorthOf(kind, config);
        return kind == WheelKind.Combat && north == JobGroup.Tank
            ? [((int)JobGroup.Tank, 0f), ((int)JobGroup.Healer, MathF.PI)]
            : [((int)north, 0f)];
    }

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

    public static List<string> OrderOf(JobGroup group, Configuration config)
    {
        var defaults = Defaults(group);
        var order = new List<string>();
        if (config.JobOrder.TryGetValue(group, out var saved))
            order.AddRange(saved.Where(defaults.Contains).Distinct());
        order.AddRange(defaults.Where(job => !order.Contains(job)));
        order.RemoveAll(job => !IsShown(job, config));
        return order;
    }

    public static string[] Defaults(JobGroup group) =>
        CombatGroups.Concat(CraftingGroups).First(g => g.Group == group).Jobs;

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
