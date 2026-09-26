using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

namespace JobWheel;

// Draws the wheel straight onto the foreground draw list: no window, no focus,
// nothing to click. Icons only; the role-colored band behind them groups the jobs.
internal sealed class WheelOverlay(WheelInput input, ITextureProvider textures, Configuration config)
{
    // Smallest ring radius at scale 1; rings with many icons grow past it so they never touch.
    private const float MinRadius = 140f;
    private const float TargetGrow = 1.2f;
    // Band thickness as a multiple of the icon size.
    private const float BandWidth = 1.35f;

    // ABGR
    private const uint White = 0xF2FFFFFF;

    public void Draw()
    {
        if (!input.IsOpen)
            return;

        var dl = ImGui.GetForegroundDrawList();
        var io = ImGui.GetIO();
        var scale = Math.Clamp(config.Scale, 0.3f, 3f);
        var icon = config.IconSize * scale;
        var center = io.DisplaySize / 2 + new Vector2(config.OffsetX, config.OffsetY);
        // Grow the ring if the icons are too big to fit around it.
        var ring = input.Ring;
        // Both wheels share the size the bigger one needs, so swapping doesn't resize.
        var slots = Math.Max(JobLayout.Combat.Slots.Length, JobLayout.Crafting.Slots.Length);
        var radius = Math.Max(MinRadius * scale, slots * icon * 1.35f / MathF.Tau);

        DrawRing(dl, ring, center, radius, icon);

        foreach (var job in input.Jobs)
        {
            if (job != input.Target)
                DrawIcon(dl, ring, center, radius, icon, job, false);
        }

        // Targeted icon last so its enlarged frame sits on top of its neighbours.
        if (input.Target is { } target)
            DrawIcon(dl, ring, center, radius, icon, target, true);
    }

    // A thick band split into role-colored arcs, with the glyph icons sitting on it.
    // The targeted job's whole slice, center to rim, fills with its role color.
    private void DrawRing(ImDrawListPtr dl, WheelRing ring, Vector2 center, float radius, float icon)
    {
        var band = icon * BandWidth;
        var outer = radius + band / 2;
        var slots = ring.Slots;

        if (input.Target is { } target)
        {
            var a0 = ring.StartOf(target.Slot);
            var a1 = ring.EndOf(target.Slot);
            var color = RoleColor(target.Group);
            dl.PathLineTo(center);
            dl.PathArcTo(center, outer, a0 - MathF.PI / 2, a1 - MathF.PI / 2, Segments(a0, a1));
            dl.PathFillConvex(ImGui.GetColorU32(color with { W = .6f }));
        }

        var start = 0;
        for (var i = 1; i <= slots.Length; i++)
        {
            if (i < slots.Length && slots[i].Group == slots[start].Group)
                continue;

            StrokeArc(dl, center, radius, ring.StartOf(start), ring.EndOf(i - 1), band,
                      ImGui.GetColorU32(RoleColor(slots[start].Group)));
            start = i;
        }

        // The targeted slot's part of the band in a brighter shade of its role.
        if (input.Target is { } lit)
        {
            var color = Vector4.Min(RoleColor(lit.Group) * new Vector4(1.7f, 1.7f, 1.7f, 1f), Vector4.One);
            StrokeArc(dl, center, radius, ring.StartOf(lit.Slot), ring.EndOf(lit.Slot), band, ImGui.GetColorU32(color));
        }

        // Dark rims and separators so the groups read as distinct blocks.
        var rim = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, .55f));
        dl.AddCircle(center, radius - band / 2, rim, 96, 1.5f);
        dl.AddCircle(center, outer, rim, 96, 1.5f);
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Group == slots[(i + slots.Length - 1) % slots.Length].Group)
                continue;
            var dir = JobLayout.Direction(ring.StartOf(i));
            dl.AddLine(center + dir * (radius - band / 2), center + dir * outer, rim, 2f);
        }
    }

    private void DrawIcon(ImDrawListPtr dl, WheelRing ring, Vector2 center, float radius, float icon, WheelJob job, bool targeted)
    {
        var size = targeted ? icon * TargetGrow : icon;
        var dir = JobLayout.Direction(ring.AngleOf(job.Slot));
        var pos = center + dir * radius;
        var min = pos - new Vector2(size / 2);
        var max = pos + new Vector2(size / 2);

        var alpha = job.GearsetIndex < 0 ? .4f : 1f;
        var wrap = textures.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrEmpty();
        dl.AddImage(wrap.Handle, min, max, Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));

        // Current job: a small white dot just outside the band.
        if (job.IsCurrent)
        {
            var dot = center + dir * (radius + icon * BandWidth / 2 + Math.Max(4f, icon * 0.12f));
            dl.AddCircleFilled(dot, Math.Max(2.5f, icon * 0.08f), White);
        }
    }

    // ImGui arc angles run clockwise from +X; ours run clockwise from up.
    private static void StrokeArc(ImDrawListPtr dl, Vector2 center, float radius, float a0, float a1, float thickness, uint color)
    {
        dl.PathArcTo(center, radius, a0 - MathF.PI / 2, a1 - MathF.PI / 2, Segments(a0, a1));
        dl.PathStroke(color, ImDrawFlags.None, thickness);
    }

    private static int Segments(float a0, float a1) => Math.Max(4, (int)MathF.Ceiling((a1 - a0) / MathF.Tau * 96));

    // Muted versions of the game's role colors, dark enough for the gold glyphs to read on.
    private static Vector4 RoleColor(JobGroup group) => group switch
    {
        JobGroup.Tank => new Vector4(.16f, .26f, .52f, .88f),
        JobGroup.Healer => new Vector4(.17f, .38f, .16f, .88f),
        JobGroup.Melee => new Vector4(.48f, .14f, .14f, .88f),
        JobGroup.PhysicalRanged => new Vector4(.50f, .24f, .10f, .88f),
        JobGroup.Caster => new Vector4(.42f, .13f, .33f, .88f),
        JobGroup.Hand => new Vector4(.33f, .22f, .45f, .88f),
        _ => new Vector4(.14f, .36f, .34f, .88f),
    };
}
