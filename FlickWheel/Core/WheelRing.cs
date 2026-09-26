using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FlickWheel;

// One slot on a ring: what goes there, which group it belongs to (for the colored
// band and the separators) and that group's color.
internal readonly record struct RingSlot(string Key, int Group, Vector4 Color);

// One ring of fixed positions. A slot never depends on what's available right now,
// so positions stay put for muscle memory; empty slots are just left blank.
//
// Anchored groups sit centered on a fixed compass angle at the ring's base slot
// width. The groups between two anchors share the arc left over, spread evenly,
// so their slots can end up a little wider or narrower than the anchored ones.
internal sealed class WheelRing
{
    private readonly float[] centers;
    private readonly float[] starts;
    private readonly float[] ends;

    public RingSlot[] Slots { get; }

    public WheelRing(RingSlot[] slots, params (int Group, float Angle)[] anchors)
    {
        Slots = slots;
        var n = slots.Length;
        centers = new float[n];
        starts = new float[n];
        ends = new float[n];
        if (n == 0)
            return;

        var step = MathF.Tau / n;

        // Contiguous runs of one group, in clockwise order.
        var runs = new List<(int Group, int First, int Count)>();
        for (var i = 0; i < n; i++)
        {
            if (runs.Count > 0 && runs[^1].Group == slots[i].Group)
                runs[^1] = runs[^1] with { Count = runs[^1].Count + 1 };
            else
                runs.Add((slots[i].Group, i, 1));
        }

        // Anchors are applied in priority order. Two anchored groups side by side would
        // leave an empty arc between them, so a later anchor next to a kept one is dropped
        // and that group just fills in like the others.
        var anchored = new List<int>();
        foreach (var (group, angle) in anchors)
        {
            var r = runs.FindIndex(run => run.Group == group);
            if (r < 0 || anchored.Any(k => Math.Abs(k - r) == 1 || Math.Abs(k - r) == runs.Count - 1))
                continue;
            Place(runs[r].First, runs[r].Count, angle - runs[r].Count * step / 2, step);
            anchored.Add(r);
        }

        // Nothing could be anchored (e.g. the pinned group is empty): the first group goes on top.
        if (anchored.Count == 0)
        {
            Place(runs[0].First, runs[0].Count, -runs[0].Count * step / 2, step);
            anchored.Add(0);
        }
        anchored.Sort();

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

    // Screen-space unit vector (Y grows down) for a clockwise-from-up angle.
    public static Vector2 Direction(float angle) => new(MathF.Sin(angle), -MathF.Cos(angle));

    // Smallest absolute difference between two angles, in radians.
    public static float AngleDistance(float a, float b)
    {
        var d = MathF.IEEERemainder(a - b, MathF.Tau);
        return MathF.Abs(d);
    }

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
