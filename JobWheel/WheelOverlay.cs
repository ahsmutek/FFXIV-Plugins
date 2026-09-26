using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

namespace JobWheel;

// Draws the wheel straight onto the foreground draw list: no window, no focus,
// nothing to click. Icons only; the role tint behind them is the only grouping cue.
internal sealed class WheelOverlay(WheelInput input, ITextureProvider textures, Configuration config)
{
    // Smallest ring radius at scale 1; rings with many icons grow past it so they never touch.
    private const float MinRadius = 140f;
    private const float TargetGrow = 1.2f;

    // ABGR
    private const uint Gold = 0xFF73D6FF;
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

        DrawPointer(dl, center, radius, icon);
    }

    private static void DrawRing(ImDrawListPtr dl, WheelRing ring, Vector2 center, float radius, float icon)
    {
        dl.AddCircle(center, radius, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, .45f)), 96, icon * 1.25f);

        // A thin role-tinted arc under each group, with a tick where the role changes.
        var half = ring.Step / 2;
        var arcRadius = radius + icon * 0.62f;
        var slots = ring.Slots;
        var start = 0;
        for (var i = 1; i <= slots.Length; i++)
        {
            if (i < slots.Length && slots[i].Group == slots[start].Group)
                continue;

            var a0 = ring.AngleOf(start) - half;
            var a1 = ring.AngleOf(i - 1) + half;
            // ImGui angles run clockwise from +X; ours run clockwise from up.
            dl.PathArcTo(center, arcRadius, a0 - MathF.PI / 2, a1 - MathF.PI / 2, 24);
            dl.PathStroke(RoleColor(slots[start].Group), ImDrawFlags.None, Math.Max(2f, icon * 0.07f));

            var tick = JobLayout.Direction(a0);
            dl.AddLine(center + tick * (radius - icon * 0.62f), center + tick * (arcRadius + icon * 0.08f),
                       ImGui.GetColorU32(new Vector4(1f, 1f, 1f, .18f)), 1.5f);
            start = i;
        }
    }

    private void DrawIcon(ImDrawListPtr dl, WheelRing ring, Vector2 center, float radius, float icon, WheelJob job, bool targeted)
    {
        var size = targeted ? icon * TargetGrow : icon;
        var pos = center + JobLayout.Direction(ring.AngleOf(job.Slot)) * radius;
        var min = pos - new Vector2(size / 2);
        var max = pos + new Vector2(size / 2);

        var alpha = job.GearsetIndex < 0 ? .5f : 1f;
        var wrap = textures.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrEmpty();
        dl.AddImage(wrap.Handle, min, max, Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));

        if (job.IsCurrent)
        {
            dl.AddCircle(pos, size * 0.52f, White, 32, 1.5f);
            dl.AddCircleFilled(pos + new Vector2(0, size * 0.62f), Math.Max(2f, size * 0.07f), White);
        }

        if (targeted)
        {
            dl.AddCircle(pos, size * 0.55f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, .6f)), 32, 4f);
            dl.AddCircle(pos, size * 0.55f, Gold, 32, 2.5f);
        }
    }

    // A short wedge from the center toward where the stick points.
    private void DrawPointer(ImDrawListPtr dl, Vector2 center, float radius, float icon)
    {
        if (input.StickTilt < config.SelectThreshold)
        {
            dl.AddCircleFilled(center, icon * 0.12f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, .35f)));
            return;
        }

        var dir = input.StickDirection;
        var side = new Vector2(-dir.Y, dir.X);
        var tip = center + dir * radius * 0.3f;
        var baseCenter = center + dir * icon * 0.1f;
        dl.AddTriangleFilled(tip, baseCenter + side * icon * 0.14f, baseCenter - side * icon * 0.14f, Gold);
    }

    private static uint RoleColor(JobGroup group) => ImGui.GetColorU32(group switch
    {
        JobGroup.Tank => new Vector4(.35f, .55f, .95f, .75f),
        JobGroup.Healer => new Vector4(.40f, .80f, .45f, .75f),
        JobGroup.Melee => new Vector4(.85f, .35f, .35f, .75f),
        JobGroup.PhysicalRanged => new Vector4(.90f, .50f, .30f, .75f),
        JobGroup.Caster => new Vector4(.80f, .35f, .60f, .75f),
        JobGroup.Hand => new Vector4(.80f, .65f, .40f, .6f),
        _ => new Vector4(.55f, .75f, .60f, .6f),
    });
}
