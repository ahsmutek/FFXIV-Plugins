using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace JobWheel;

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
}

public enum WheelKind
{
    Combat,
    Crafting,
}

// One ring of fixed job positions. A job's slot never depends on what's unlocked,
// so positions stay put for muscle memory; locked jobs just leave their slot empty.
//
// Anchored groups sit centered on a fixed compass angle at the ring's base slot
// width. The groups between two anchors share the arc left over, spread evenly,
// so their slots can end up a little wider or narrower than the anchored ones.
internal sealed class WheelRing
{
    private readonly float[] centers;
    private readonly float[] starts;
    private readonly float[] ends;

    public (string Abbreviation, JobGroup Group)[] Slots { get; }

    public WheelRing((string, JobGroup)[] slots, params (JobGroup Group, float Angle)[] anchors)
    {
        Slots = slots;
        var n = slots.Length;
        var step = MathF.Tau / n;
        centers = new float[n];
        starts = new float[n];
        ends = new float[n];

        // Contiguous runs of one group, in clockwise order.
        var runs = new List<(JobGroup Group, int First, int Count)>();
        for (var i = 0; i < n; i++)
        {
            if (runs.Count > 0 && runs[^1].Group == slots[i].Item2)
                runs[^1] = runs[^1] with { Count = runs[^1].Count + 1 };
            else
                runs.Add((slots[i].Item2, i, 1));
        }

        var anchored = new List<int>();
        for (var r = 0; r < runs.Count; r++)
        {
            foreach (var (group, angle) in anchors)
            {
                if (group != runs[r].Group)
                    continue;
                Place(runs[r].First, runs[r].Count, angle - runs[r].Count * step / 2, step);
                anchored.Add(r);
            }
        }

        // Fill the slots between each anchored run and the next one (wrapping around).
        for (var k = 0; k < anchored.Count; k++)
        {
            var from = runs[anchored[k]];
            var to = runs[anchored[(k + 1) % anchored.Count]];
            var firstFree = from.First + from.Count;
            var freeCount = (to.First - firstFree + n) % n;
            if (freeCount == 0)
                continue;

            var arcStart = ends[from.First + from.Count - 1];
            var arcEnd = starts[to.First];
            while (arcEnd <= arcStart)
                arcEnd += MathF.Tau;
            var width = (arcEnd - arcStart) / freeCount;
            for (var j = 0; j < freeCount; j++)
            {
                var slot = (firstFree + j) % n;
                Place(slot, 1, arcStart + j * width, width);
            }
        }
    }

    // Radians clockwise from straight up.
    public float AngleOf(int slot) => centers[slot];
    public float StartOf(int slot) => starts[slot];
    public float EndOf(int slot) => ends[slot];

    private void Place(int first, int count, float start, float width)
    {
        for (var i = 0; i < count; i++)
        {
            starts[first + i] = start + i * width;
            ends[first + i] = start + (i + 1) * width;
            centers[first + i] = start + (i + 0.5f) * width;
        }
    }
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

    // Crafters across the top, gatherers along the bottom.
    private static readonly (JobGroup Group, string[] Jobs)[] CraftingGroups =
    [
        (JobGroup.Hand, ["CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL"]),
        (JobGroup.Land, ["MIN", "BTN", "FSH"]),
    ];

    private static readonly (JobGroup, float)[] CombatAnchors = [(JobGroup.Tank, 0f), (JobGroup.Healer, MathF.PI)];
    private static readonly (JobGroup, float)[] CraftingAnchors = [(JobGroup.Hand, 0f)];

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
            [JobGroup.Hand] = new(.14f, .11f, .19f, .88f),
            [JobGroup.Land] = new(.08f, .16f, .15f, .88f),
        }),
    ];

    // The most slots either wheel has; both wheels are drawn at the size this needs.
    public static readonly int MaxSlots = Math.Max(
        CombatGroups.Sum(g => g.Jobs.Length), CraftingGroups.Sum(g => g.Jobs.Length));

    public static IEnumerable<JobGroup> GroupsOf(WheelKind kind) =>
        (kind == WheelKind.Crafting ? CraftingGroups : CombatGroups).Select(g => g.Group);

    public static WheelRing Build(WheelKind kind, Configuration config)
    {
        var groups = kind == WheelKind.Crafting ? CraftingGroups : CombatGroups;
        var slots = groups.SelectMany(g => OrderOf(g.Group, config).Select(job => (job, g.Group))).ToArray();
        return new WheelRing(slots, kind == WheelKind.Crafting ? CraftingAnchors : CombatAnchors);
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

    // Screen-space unit vector (Y grows down) for a clockwise-from-up angle.
    public static Vector2 Direction(float angle) => new(MathF.Sin(angle), -MathF.Cos(angle));

    // Smallest absolute difference between two angles, in radians.
    public static float AngleDistance(float a, float b)
    {
        var d = MathF.IEEERemainder(a - b, MathF.Tau);
        return MathF.Abs(d);
    }
}
