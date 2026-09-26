using System;
using System.Collections.Generic;
using System.Numerics;

namespace JobWheel;

internal enum JobGroup
{
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    Caster,
    Hand,
    Land,
}

internal enum WheelKind
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
    // Tanks centered north, healers centered south. Clockwise: tanks, melee on the
    // east side, healers, then physical ranged and casters on the west side.
    public static readonly WheelRing Combat = new(
    [
        ("PLD", JobGroup.Tank), ("WAR", JobGroup.Tank), ("DRK", JobGroup.Tank), ("GNB", JobGroup.Tank),
        ("MNK", JobGroup.Melee), ("DRG", JobGroup.Melee), ("NIN", JobGroup.Melee),
        ("SAM", JobGroup.Melee), ("RPR", JobGroup.Melee), ("VPR", JobGroup.Melee),
        ("WHM", JobGroup.Healer), ("SCH", JobGroup.Healer), ("AST", JobGroup.Healer), ("SGE", JobGroup.Healer),
        ("BRD", JobGroup.PhysicalRanged), ("MCH", JobGroup.PhysicalRanged), ("DNC", JobGroup.PhysicalRanged),
        ("BLM", JobGroup.Caster), ("SMN", JobGroup.Caster), ("RDM", JobGroup.Caster),
        ("PCT", JobGroup.Caster), ("BLU", JobGroup.Caster),
    ], (JobGroup.Tank, 0f), (JobGroup.Healer, MathF.PI));

    // Crafters across the top, gatherers along the bottom.
    public static readonly WheelRing Crafting = new(
    [
        ("CRP", JobGroup.Hand), ("BSM", JobGroup.Hand), ("ARM", JobGroup.Hand), ("GSM", JobGroup.Hand),
        ("LTW", JobGroup.Hand), ("WVR", JobGroup.Hand), ("ALC", JobGroup.Hand), ("CUL", JobGroup.Hand),
        ("MIN", JobGroup.Land), ("BTN", JobGroup.Land), ("FSH", JobGroup.Land),
    ], (JobGroup.Hand, 0f));

    public static WheelRing For(WheelKind kind) => kind == WheelKind.Crafting ? Crafting : Combat;

    // Screen-space unit vector (Y grows down) for a clockwise-from-up angle.
    public static Vector2 Direction(float angle) => new(MathF.Sin(angle), -MathF.Cos(angle));

    // Smallest absolute difference between two angles, in radians.
    public static float AngleDistance(float a, float b)
    {
        var d = MathF.IEEERemainder(a - b, MathF.Tau);
        return MathF.Abs(d);
    }
}
