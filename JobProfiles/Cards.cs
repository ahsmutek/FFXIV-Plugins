using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace JobProfiles;

// The Jobs tab's cards: a shaded panel per setting whose title line folds it and carries its value and switches.
// The panel is drawn behind its contents once their height is known, through a split draw list.
internal static class Cards
{
    private static Vector2 start;
    private static float width;
    private static float innerRight;
    private static float inlineX;
    private static bool hovered;
    private static float titleTop;
    private static float titleHeight;

    private static float Pad => 8f * ImGuiHelpers.GlobalScale;

    // True when the body is to be drawn. A card with nothing in its body can't be opened.
    public static bool Begin(string id, FontAwesomeIcon icon, string title, string tooltip, ref bool open, bool hasBody = true)
    {
        ImGui.PushID(id);
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        var style = ImGui.GetStyle();
        start = ImGui.GetCursorScreenPos();
        width = ImGui.GetContentRegionAvail().X;
        innerRight = ImGui.GetCursorPosX() + width - Pad;
        ImGui.Indent(Pad);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Pad);

        var iconBox = ImGui.GetFontSize() * 1.4f;
        titleHeight = MathF.Max(ImGui.GetFrameHeight(), iconBox);
        titleTop = ImGui.GetCursorPosY();
        var textX = iconBox + style.ItemSpacing.X * 2;
        inlineX = ImGui.GetCursorPosX() + textX + ImGui.CalcTextSize(title).X + style.ItemSpacing.X * 3;

        // The whole title line folds the card; the chip and switches drawn over it keep their own clicks.
        // Its own highlight would wash out the title, so hovering shades the whole card instead.
        var min = ImGui.GetCursorScreenPos();
        var flags = ImGuiSelectableFlags.AllowItemOverlap | (hasBody ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        if (ImGui.Selectable("##title", false, flags, new Vector2(innerRight - ImGui.GetCursorPosX(), titleHeight)))
            open = !open;
        ImGui.PopStyleColor(2);
        hovered = hasBody && ImGui.IsItemHovered();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooltip);

        var text = ImGui.GetColorU32(ImGuiCol.Text);
        var middle = min.Y + titleHeight / 2;
        if (hasBody)
            Icon(drawList, open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight,
                 new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFontSize() / 2, middle),
                 hovered ? text : ImGui.GetColorU32(ImGuiCol.TextDisabled));
        Icon(drawList, icon, new Vector2(min.X + iconBox / 2, middle), text);
        drawList.AddText(min + new Vector2(textX, (titleHeight - ImGui.GetTextLineHeight()) / 2), text, title);
        return open && hasBody;
    }

    // Puts the next item on the title line, after the title.
    public static void Inline()
    {
        ImGui.SameLine(inlineX);
        Centre();
    }

    // Puts the next item on the title line, after the previous one. Each needs placing, as SameLine alone
    // would go back to the line's top.
    public static void Next()
    {
        ImGui.SameLine();
        Centre();
    }

    // Puts the next item, of this width, at the right of the title line, before the chevron's place.
    public static void Right(float itemWidth)
    {
        ImGui.SameLine(innerRight - ImGui.GetFontSize() - ImGui.GetStyle().ItemSpacing.X * 2 - itemWidth);
        Centre();
    }

    public static void End()
    {
        var style = ImGui.GetStyle();
        ImGui.Unindent(Pad);
        var drawList = ImGui.GetWindowDrawList();
        var max = new Vector2(start.X + width, ImGui.GetCursorScreenPos().Y - style.ItemSpacing.Y + Pad);
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(start, max, ImGui.GetColorU32(ImGuiCol.FrameBg, hovered ? 0.6f : 0.35f), style.FrameRounding * 2);
        drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, max.Y + style.ItemSpacing.Y));
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopID();
    }

    // A FontAwesome icon at the icon font's own size, centred on a point.
    public static void Icon(ImDrawListPtr drawList, FontAwesomeIcon icon, Vector2 centre, uint color)
    {
        var glyph = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        drawList.AddText(centre - ImGui.CalcTextSize(glyph) / 2, color, glyph);
        ImGui.PopFont();
    }

    public static float IconWidth(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var glyphWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        ImGui.PopFont();
        return glyphWidth;
    }

    // A value on a title line: plain text, or a rounded button with an icon when it can be pressed.
    public static bool Chip(string text, FontAwesomeIcon? icon = null, bool clickable = false, bool dimmed = false)
    {
        var style = ImGui.GetStyle();
        var height = ImGui.GetFrameHeight();
        var iconWidth = icon is { } measured ? IconWidth(measured) + style.ItemInnerSpacing.X : 0;
        var padding = clickable ? style.FramePadding.X + height / 4 : 0;
        var size = new Vector2(ImGui.CalcTextSize(text).X + iconWidth + padding * 2, height);

        var clicked = false;
        if (clickable)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, height / 2);
            clicked = ImGui.Button($"##{text}", size);
            ImGui.PopStyleVar();
        }
        else
        {
            ImGui.Dummy(size);
        }

        var color = ImGui.GetColorU32(dimmed ? ImGuiCol.TextDisabled : ImGuiCol.Text);
        var at = ImGui.GetItemRectMin() + new Vector2(padding, style.FramePadding.Y);
        if (icon is { } shown)
        {
            Icon(ImGui.GetWindowDrawList(), shown, at + new Vector2(IconWidth(shown) / 2, ImGui.GetTextLineHeight() / 2), color);
            at.X += iconWidth;
        }
        ImGui.GetWindowDrawList().AddText(at, color, text);
        return clicked;
    }

    // A two-way switch: one pill with the chosen half filled. It's a single item so it stays on its line; a click picks
    // the half under the mouse and A on the pad flips it. second turns true or false as it changes.
    public static bool Segmented(ref bool second, string firstLabel, string secondLabel)
    {
        var height = ImGui.GetFrameHeight();
        var firstWidth = HalfWidth(firstLabel);
        var size = new Vector2(firstWidth + HalfWidth(secondLabel), height);
        var pressed = ImGui.InvisibleButton("##switch", size);
        var min = ImGui.GetItemRectMin();
        var split = min.X + firstWidth;
        var mouseOnSecond = ImGui.GetMousePos().X >= split;

        var changed = false;
        if (pressed)
        {
            var wanted = ImGui.IsMouseReleased(ImGuiMouseButton.Left) ? mouseOnSecond : !second;
            changed = wanted != second;
            second = wanted;
        }

        var drawList = ImGui.GetWindowDrawList();
        var rounding = height / 2;
        var middle = new Vector2(split, min.Y);
        var max = min + size;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), rounding);
        if (ImGui.IsItemHovered() && mouseOnSecond != second)
            DrawHalf(drawList, mouseOnSecond, min, middle, max, ImGui.GetColorU32(ImGuiCol.ButtonHovered, 0.5f), rounding);
        DrawHalf(drawList, second, min, middle, max, ImGui.GetColorU32(ImGuiCol.ButtonActive), rounding);
        Label(drawList, firstLabel, min, new Vector2(split, max.Y), !second);
        Label(drawList, secondLabel, middle, max, second);
        return changed;
    }

    public static float SegmentedWidth(string firstLabel, string secondLabel) => HalfWidth(firstLabel) + HalfWidth(secondLabel);

    private static float HalfWidth(string label) => ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 3;

    private static void DrawHalf(ImDrawListPtr drawList, bool second, Vector2 min, Vector2 middle, Vector2 max, uint color, float rounding)
    {
        if (second)
            drawList.AddRectFilled(middle, max, color, rounding);
        else
            drawList.AddRectFilled(min, new Vector2(middle.X, max.Y), color, rounding);
    }

    private static void Label(ImDrawListPtr drawList, string label, Vector2 min, Vector2 max, bool active)
    {
        var text = ImGui.CalcTextSize(label);
        drawList.AddText(min + (max - min - text) / 2, ImGui.GetColorU32(active ? ImGuiCol.Text : ImGuiCol.TextDisabled), label);
    }

    private static void Centre() => ImGui.SetCursorPosY(titleTop + (titleHeight - ImGui.GetFrameHeight()) / 2);
}
