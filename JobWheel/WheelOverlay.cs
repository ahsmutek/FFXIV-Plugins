using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace JobWheel;

// Draws the wheel straight onto a draw list: no window, no focus, nothing to click.
// Icons only; the role-colored band behind them groups the jobs. While the settings
// window asks for a preview, the wheel is also drawn (behind other windows) with an
// invisible handle over it so it can be dragged into place.
internal sealed class WheelOverlay(WheelInput input, JobCatalog catalog, IconCache icons, Configuration config, Action save)
{
    // Smallest ring radius at scale 1; rings with many icons grow past it so they never touch.
    private const float MinRadius = 140f;
    private const float TargetGrow = 1.2f;
    // Band thickness as a multiple of the icon size.
    private const float BandWidth = 1.35f;

    // ABGR
    private const uint White = 0xF2FFFFFF;

    // Set by the settings window while its preview checkbox is ticked, and which wheel to show.
    public bool Preview { get; set; }
    public WheelKind PreviewKind { get; set; }

    // Rebuild the preview wheel this often (ms) so new unlocks and gear sets show up.
    private const long PreviewRefresh = 1000;

    // The preview wheel, kept between frames instead of rebuilt on every draw.
    private WheelRing? previewRing;
    private List<WheelJob> previewJobs = [];
    private WheelKind previewBuiltKind;
    private long previewBuiltAt;

    // Called by the settings window when the layout, colors or gear sets change.
    public void InvalidatePreview() => previewRing = null;

    public void Draw()
    {
        if (input.IsOpen)
        {
            input.MarkDrawn();
            DrawWheel(ImGui.GetForegroundDrawList(), input.Ring, input.Jobs, input.Target);
        }
        else if (Preview)
        {
            var now = Environment.TickCount64;
            if (previewRing == null || previewBuiltKind != PreviewKind || now - previewBuiltAt > PreviewRefresh)
            {
                previewRing = JobLayout.Build(PreviewKind, config);
                previewJobs = catalog.Build(previewRing);
                previewBuiltKind = PreviewKind;
                previewBuiltAt = now;
            }

            DrawWheel(ImGui.GetBackgroundDrawList(), previewRing, previewJobs, null);
            DrawDragHandle();
        }
    }

    private (Vector2 Center, float Radius, float Icon) Geometry()
    {
        var scale = Math.Clamp(config.Scale, 0.3f, 3f);
        var icon = config.IconSize * scale;
        var center = ImGui.GetIO().DisplaySize / 2 + new Vector2(config.OffsetX, config.OffsetY);
        // Both wheels share the size the bigger one needs, so swapping doesn't resize;
        // the ring grows if the icons are too big to fit around it.
        var radius = Math.Max(MinRadius * scale, JobLayout.MaxSlots * icon * 1.35f / MathF.Tau);
        return (center, radius, icon);
    }

    private void DrawWheel(ImDrawListPtr dl, WheelRing ring, IReadOnlyList<WheelJob> jobs, WheelJob? target)
    {
        var (center, radius, icon) = Geometry();
        DrawRing(dl, ring, center, radius, icon, target);

        foreach (var job in jobs)
        {
            if (job != target)
                DrawIcon(dl, ring, center, radius, icon, job, false);
        }

        // Targeted icon last so its enlarged frame sits on top of its neighbours.
        if (target != null)
            DrawIcon(dl, ring, center, radius, icon, target, true);
    }

    // An invisible window over the preview; dragging it moves the wheel.
    private void DrawDragHandle()
    {
        var (center, radius, icon) = Geometry();
        var outer = radius + icon * BandWidth / 2;
        ImGui.SetNextWindowPos(center - new Vector2(outer));
        ImGui.SetNextWindowSize(new Vector2(outer * 2));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground |
                                       ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove |
                                       ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus |
                                       ImGuiWindowFlags.NoNav;
        if (ImGui.Begin("##JobWheelPreview", flags))
        {
            ImGui.InvisibleButton("##drag", new Vector2(outer * 2));
            if (ImGui.IsItemHovered() || ImGui.IsItemActive())
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsItemActive())
            {
                var delta = ImGui.GetIO().MouseDelta;
                config.OffsetX += delta.X;
                config.OffsetY += delta.Y;
            }
            if (ImGui.IsItemDeactivated())
                save();
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    // A thick band split into role-colored arcs, with the glyph icons sitting on it.
    // The targeted job's whole slice, center to rim, fills with its role color.
    private void DrawRing(ImDrawListPtr dl, WheelRing ring, Vector2 center, float radius, float icon, WheelJob? target)
    {
        var band = icon * BandWidth;
        var outer = radius + band / 2;
        var slots = ring.Slots;

        if (target != null)
        {
            var a0 = ring.StartOf(target.Slot);
            var a1 = ring.EndOf(target.Slot);
            var color = Color(target.Group);
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
                      ImGui.GetColorU32(Color(slots[start].Group)));
            start = i;
        }

        // The targeted slot's part of the band in a brighter shade of its role.
        if (target is { } lit)
        {
            var color = Vector4.Min(Color(lit.Group) * new Vector4(1.7f, 1.7f, 1.7f, 1f), Vector4.One);
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

        // Jobs without a gear set are faded. Log entries never are.
        var alpha = job.CommandId == 0 && job.GearsetIndex < 0 ? .4f : 1f;
        dl.AddImage(icons.Handle(job.IconId, job.CommandId != 0), min, max, Vector2.Zero, Vector2.One,
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

    private Vector4 Color(JobGroup group) => JobLayout.ColorOf(group, config);

    private static int Segments(float a0, float a1) => Math.Max(4, (int)MathF.Ceiling((a1 - a0) / MathF.Tau * 96));
}
