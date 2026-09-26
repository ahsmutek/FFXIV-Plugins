using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FlickWheel;

// One icon in a group's settings row: a stable ID (so a drag keeps hold of the same
// icon as it moves), a texture (or a text button when there is none), its hover text,
// and whether it's shown dimmed (e.g. a locked job).
internal readonly record struct RowIcon(string Id, ImTextureID? Texture, string Label, string Tooltip, bool Dim);

// Settings controls shared by every wheel's group editor.
internal static class GroupEditor
{
    // Every button is the icon plus 2px frame padding per side; 4px between buttons.
    private const float Icon = 36f;
    private const float Gap = 4f;
    private const float Pitch = Icon + 4f + Gap;

    /// <summary>Up/down arrows that move a group one step. Returns -1, 0 or 1.</summary>
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

    /// <summary>
    /// A row of the group's icons on its band color. Drag one sideways to move it; the
    /// result is the move to make (from index, to index), or null. <paramref name="context"/>
    /// is drawn as a right-click menu for an icon, if given.
    /// </summary>
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
                ImGui.ImageButton(texture, new Vector2(Icon), Vector2.Zero, Vector2.One, 2, Vector4.Zero,
                                  new Vector4(1f, 1f, 1f, item.Dim ? .4f : 1f));
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

        // Move the dragged icon to whichever slot the mouse is over (clamped to the row).
        var slot = (int)MathF.Floor((ImGui.GetMousePos().X - rowStart + Gap / 2) / Pitch);
        slot = Math.Clamp(slot, 0, count - 1);
        return slot != dragged ? (dragged, slot) : null;
    }
}
