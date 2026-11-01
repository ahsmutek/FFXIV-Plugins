using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

using Shortcuts;

namespace FlickWheel;

internal sealed class WheelOverlay(WheelInput input, WheelSet wheels, IconCache icons, IFontHandle glyphFont,
                                   Configuration config, Action save)
{
    private const float MinRadius = 140f;
    // Wheels with more icons shrink them rather than grow.
    private const int RingSlots = 24;
    private const float IconPitch = 1.35f;
    private const float TargetGrow = 1.2f;
    private const float BandWidth = 1.35f;

    // ABGR
    private const uint White = 0xF2FFFFFF;
    private const uint Outline = 0xF2000000;

    private const long PreviewRefresh = 1000;

    public bool Preview { get; set; }
    public string PreviewWheel { get; set; } = string.Empty;

    private WheelRing? previewRing;
    private List<WheelEntry> previewEntries = [];
    private string previewBuiltFor = string.Empty;
    private long previewBuiltAt;

    public void InvalidatePreview() => previewRing = null;

    public void Draw()
    {
        if (input.IsOpen)
        {
            input.MarkDrawn();
            DrawWheel(ImGui.GetForegroundDrawList(), input.Ring, input.Entries, input.Target);
        }
        else if (Preview && (wheels.Find(PreviewWheel) ?? wheels.Wheels.FirstOrDefault()) is { } wheel)
        {
            var now = Environment.TickCount64;
            if (previewRing == null || previewBuiltFor != wheel.Id || now - previewBuiltAt > PreviewRefresh)
            {
                previewRing = wheel.Layout();
                previewEntries = wheel.Entries(previewRing);
                previewBuiltFor = wheel.Id;
                previewBuiltAt = now;
            }

            DrawWheel(ImGui.GetBackgroundDrawList(), previewRing, previewEntries, null);
            DrawDragHandle();
        }
    }

    public void DrawPreviewControls()
    {
        var preview = Preview;
        if (ImGui.Checkbox("Show wheel preview", ref preview))
            Preview = preview;

        var all = wheels.Wheels;
        if (all.Count > 0)
        {
            ImGui.SameLine();
            var index = Math.Max(0, all.FindIndex(w => w.Id == PreviewWheel));
            var titles = all.Select(w => w.Title).ToArray();
            ImGui.SetNextItemWidth(170f);
            if (ImGui.Combo("##previewWheel", ref index, titles, titles.Length))
                PreviewWheel = all[index].Id;
        }
        ImGui.TextDisabled("Drag the preview to move the wheel.");
    }

    private (Vector2 Center, float Radius, float Icon) Geometry()
    {
        var scale = Math.Clamp(config.Scale, 0.3f, 3f);
        var icon = config.IconSize * scale;
        var center = ImGui.GetIO().DisplaySize / 2 + new Vector2(config.OffsetX, config.OffsetY);
        // Independent of icon count so every wheel is the same size.
        var radius = Math.Max(MinRadius * scale, RingSlots * icon * IconPitch / MathF.Tau);
        return (center, radius, icon);
    }

    private void DrawWheel(ImDrawListPtr dl, WheelRing ring, IReadOnlyList<WheelEntry> entries, WheelEntry? target)
    {
        var (center, radius, icon) = Geometry();
        DrawRing(dl, ring, center, radius, icon, target);

        var glyph = Math.Min(icon, radius * MathF.Tau / (Math.Max(1, ring.Slots.Length) * IconPitch));

        foreach (var entry in entries)
        {
            if (entry != target)
                DrawIcon(dl, ring, center, radius, icon, glyph, entry, false);
        }

        // Targeted icon last so its enlarged frame sits on top of its neighbours.
        if (target != null)
        {
            DrawIcon(dl, ring, center, radius, icon, glyph, target, true);
            DrawOutlinedText(dl, target.Name, center, Math.Clamp(icon * 0.4f, 14f, 32f), Align.Center);
        }
    }

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
        if (ImGui.Begin("##FlickWheelPreview", flags))
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

    private static void DrawRing(ImDrawListPtr dl, WheelRing ring, Vector2 center, float radius, float icon, WheelEntry? target)
    {
        var band = icon * BandWidth;
        var outer = radius + band / 2;
        var slots = ring.Slots;

        if (target != null)
        {
            var a0 = ring.StartOf(target.Slot);
            var a1 = ring.EndOf(target.Slot);
            var color = slots[target.Slot].Color;
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
                      ImGui.GetColorU32(slots[start].Color));
            start = i;
        }

        if (target is { } lit)
        {
            var color = Vector4.Min(slots[lit.Slot].Color * new Vector4(1.7f, 1.7f, 1.7f, 1f), Vector4.One);
            StrokeArc(dl, center, radius, ring.StartOf(lit.Slot), ring.EndOf(lit.Slot), band, ImGui.GetColorU32(color));
        }

        var rim = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, .55f));
        dl.AddCircle(center, radius - band / 2, rim, 96, 1.5f);
        dl.AddCircle(center, outer, rim, 96, 1.5f);
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Group == slots[(i + slots.Length - 1) % slots.Length].Group)
                continue;
            var dir = WheelRing.Direction(ring.StartOf(i));
            dl.AddLine(center + dir * (radius - band / 2), center + dir * outer, rim, 2f);
        }
    }

    // icon sizes the band; glyph is the drawn size, smaller when crowded.
    private void DrawIcon(ImDrawListPtr dl, WheelRing ring, Vector2 center, float radius, float icon, float glyph,
                          WheelEntry entry, bool targeted)
    {
        var size = targeted ? glyph * TargetGrow : glyph;
        var dir = WheelRing.Direction(ring.AngleOf(entry.Slot));
        var pos = center + dir * radius;
        var min = pos - new Vector2(size / 2);
        var max = pos + new Vector2(size / 2);

        var shade = entry.Faded ? .4f : 1f;
        if (entry.Glyph is { } pluginGlyph)
            DrawGlyph(dl, pluginGlyph, pos, size, shade);
        else
            icons.Get(entry.IconId, entry.KeyOutIcon).Draw(dl, min, max, ImGui.GetColorU32(new Vector4(shade, shade, shade, 1f)));

        if (entry.Count is { } count)
            DrawOutlinedText(dl, count.ToString(), max, size * 0.34f, Align.BottomRight);

        if (entry.IsCurrent)
        {
            var dot = center + dir * (radius + icon * BandWidth / 2 + Math.Max(4f, icon * 0.12f));
            dl.AddCircleFilled(dot, Math.Max(2.5f, icon * 0.08f), White);
        }
    }

    private void DrawGlyph(ImDrawListPtr dl, PluginGlyph glyph, Vector2 at, float size, float shade)
    {
        using (glyphFont.Push())
        {
            var text = glyph.Glyph.ToString();
            var fontSize = size * 0.8f;
            var textSize = ImGui.CalcTextSize(text) * (fontSize / ImGui.GetFontSize());
            var color = ImGui.GetColorU32(ImGui.ColorConvertU32ToFloat4(glyph.Color) * new Vector4(shade, shade, shade, 1f));
            dl.AddText(ImGui.GetFont(), fontSize, at - textSize / 2, color, text);
        }
    }

    private enum Align
    {
        Center,
        BottomRight,
    }

    private static void DrawOutlinedText(ImDrawListPtr dl, string text, Vector2 at, float fontSize, Align align)
    {
        var font = ImGui.GetFont();
        var textSize = ImGui.CalcTextSize(text) * (fontSize / ImGui.GetFontSize());
        var pos = align == Align.Center ? at - textSize / 2 : at - textSize;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0)
                    dl.AddText(font, fontSize, pos + new Vector2(dx, dy), Outline, text);
            }
        }
        dl.AddText(font, fontSize, pos, White, text);
    }

    // ImGui arc angles run clockwise from +X; ours run clockwise from up.
    private static void StrokeArc(ImDrawListPtr dl, Vector2 center, float radius, float a0, float a1, float thickness, uint color)
    {
        dl.PathArcTo(center, radius, a0 - MathF.PI / 2, a1 - MathF.PI / 2, Segments(a0, a1));
        dl.PathStroke(color, ImDrawFlags.None, thickness);
    }

    private static int Segments(float a0, float a1) => Math.Max(4, (int)MathF.Ceiling((a1 - a0) / MathF.Tau * 96));
}
