using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Shortcuts;

namespace FlickWheel;

internal readonly record struct RowIcon(string Id, GameIcon? Texture, string Label, string Tooltip, bool Dim);

internal static class GroupEditor
{
    // Every button is the icon plus 2px frame padding per side; 4px between buttons.
    private const float Icon = 36f;
    private const float Gap = 4f;
    private const float Pitch = Icon + 4f + Gap;

    public static int MoveArrows(int index, int count)
    {
        var step = 0;
        ImGui.BeginDisabled(index == 0);
        if (ImGui.ArrowButton("##up", ImGuiDir.Up))
            step = -1;
        ImGui.EndDisabled();
        ImGui.SameLine(0, 2f);
        ImGui.BeginDisabled(index == count - 1);
        if (ImGui.ArrowButton("##down", ImGuiDir.Down))
            step = 1;
        ImGui.EndDisabled();
        return step;
    }

    public static bool StyleButtons(ref int style)
    {
        const float buttonWidth = 90f;
        var changed = false;
        var swatch = new Vector2(ImGui.GetFrameHeight());
        for (var s = 0; s < Palette.Styles.Length; s++)
        {
            ImGui.PushID(s);
            var selected = style == s;
            if (selected)
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            if (ImGui.Button(Palette.Styles[s].Name, new Vector2(buttonWidth, 0)) && !selected)
            {
                style = s;
                changed = true;
            }
            if (selected)
                ImGui.PopStyleColor();

            for (var h = 0; h < Palette.Hues.Length; h++)
            {
                ImGui.SameLine(0, h == 0 ? 6f : 1f);
                ImGui.ColorButton($"##{h}", Palette.Color(h, s),
                                  ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker |
                                  ImGuiColorEditFlags.NoDragDrop | ImGuiColorEditFlags.NoAlpha, swatch);
            }
            ImGui.PopID();
        }

        return changed;
    }

    public static bool ColorSwatch(ref int hue, ref uint custom, int style)
    {
        const int perRow = 6;
        const ImGuiColorEditFlags swatchFlags = ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha |
                                                ImGuiColorEditFlags.NoDragDrop;
        var changed = false;
        var color = hue >= 0 ? Palette.Color(hue, style) : Palette.FromAbgr(custom);
        if (ImGui.ColorButton("##swatch", color, swatchFlags, new Vector2(ImGui.GetFrameHeight())))
            ImGui.OpenPopup("##palette");

        if (!ImGui.BeginPopup("##palette"))
            return false;

        for (var i = 0; i < Palette.Hues.Length; i++)
        {
            if (i % perRow != 0)
                ImGui.SameLine(0, 4f);
            ImGui.PushID(i);
            if (ImGui.ColorButton("##hue", Palette.Color(i, style), swatchFlags, new Vector2(28f)))
            {
                hue = i;
                changed = true;
                ImGui.CloseCurrentPopup();
            }
            if (i == hue)
                ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin() - new Vector2(2f), ImGui.GetItemRectMax() + new Vector2(2f),
                                                  0xFFFFFFFF, 3f, ImDrawFlags.None, 2f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Palette.Hues[i].Name);
            ImGui.PopID();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled(hue < 0 ? "Custom (in use)" : "Custom");
        var picked = color;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.ColorPicker4("##custom", ref picked, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoSidePreview))
        {
            hue = -1;
            custom = Palette.Abgr(picked);
            changed = true;
        }

        ImGui.EndPopup();
        return changed;
    }

    public static (int From, int To)? IconRow(int count, Vector4 color, Func<int, RowIcon> icon,
                                              Action<int>? context = null)
    {
        var rowStart = ImGui.GetCursorScreenPos().X;
        var dragged = -1;
        for (var i = 0; i < count; i++)
        {
            var item = icon(i);
            ImGui.PushID(item.Id);
            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * new Vector4(1.4f, 1.4f, 1.4f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, color * new Vector4(1.7f, 1.7f, 1.7f, 1f));
            if (item.Texture is { } texture)
            {
                ImGui.Button("##icon", new Vector2(Icon + 4f));
                texture.Draw(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin() + new Vector2(2f),
                             ImGui.GetItemRectMax() - new Vector2(2f),
                             ImGui.GetColorU32(new Vector4(1f, 1f, 1f, item.Dim ? .4f : 1f)));
            }
            else
            {
                ImGui.Button(item.Label, new Vector2(Icon + 4f));
            }
            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered() && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                ImGui.SetTooltip(item.Tooltip);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                dragged = i;
            if (context != null && ImGui.BeginPopupContextItem("##context"))
            {
                context(i);
                ImGui.EndPopup();
            }

            ImGui.PopID();
            if (i < count - 1)
                ImGui.SameLine(0, Gap);
        }

        if (dragged < 0)
            return null;

        var slot = (int)MathF.Floor((ImGui.GetMousePos().X - rowStart + Gap / 2) / Pitch);
        slot = Math.Clamp(slot, 0, count - 1);
        return slot != dragged ? (dragged, slot) : null;
    }
}
