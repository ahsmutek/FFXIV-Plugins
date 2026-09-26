using System;
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
internal sealed class WheelRing
{
    private readonly float startAngle;

    public (string Abbreviation, JobGroup Group)[] Slots { get; }
    public float Step { get; }

    public WheelRing((string, JobGroup)[] slots)
    {
        Slots = slots;
        Step = MathF.Tau / slots.Length;

        // Center the first group on the top of the wheel.
        var firstGroup = 1;
        while (firstGroup < slots.Length && slots[firstGroup].Item2 == slots[0].Item2)
            firstGroup++;
        startAngle = -(firstGroup - 1) / 2f * Step;
    }

    // Radians clockwise from straight up.
    public float AngleOf(int slot) => startAngle + slot * Step;
}

internal static class JobLayout
{
    // Tanks on top, then clockwise: healers, melee, physical ranged, casters.
    public static readonly WheelRing Combat = new(
    [
        ("PLD", JobGroup.Tank), ("WAR", JobGroup.Tank), ("DRK", JobGroup.Tank), ("GNB", JobGroup.Tank),
        ("WHM", JobGroup.Healer), ("SCH", JobGroup.Healer), ("AST", JobGroup.Healer), ("SGE", JobGroup.Healer),
        ("MNK", JobGroup.Melee), ("DRG", JobGroup.Melee), ("NIN", JobGroup.Melee),
        ("SAM", JobGroup.Melee), ("RPR", JobGroup.Melee), ("VPR", JobGroup.Melee),
        ("BRD", JobGroup.PhysicalRanged), ("MCH", JobGroup.PhysicalRanged), ("DNC", JobGroup.PhysicalRanged),
        ("BLM", JobGroup.Caster), ("SMN", JobGroup.Caster), ("RDM", JobGroup.Caster),
        ("PCT", JobGroup.Caster), ("BLU", JobGroup.Caster),
    ]);

    // Crafters across the top, gatherers along the bottom.
    public static readonly WheelRing Crafting = new(
    [
        ("CRP", JobGroup.Hand), ("BSM", JobGroup.Hand), ("ARM", JobGroup.Hand), ("GSM", JobGroup.Hand),
        ("LTW", JobGroup.Hand), ("WVR", JobGroup.Hand), ("ALC", JobGroup.Hand), ("CUL", JobGroup.Hand),
        ("MIN", JobGroup.Land), ("BTN", JobGroup.Land), ("FSH", JobGroup.Land),
    ]);

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
