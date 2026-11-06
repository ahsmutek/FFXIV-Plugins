using System.Numerics;
using Dalamud.Bindings.ImGui;
using Icons;

namespace Sundries;

internal static class Settings
{
    private const ImGuiColorEditFlags ColorFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha;

    public static bool Color(string label, ref Vector4 color, Vector4 defaultColor)
    {
        var changed = ImGui.ColorEdit4(label, ref color, ColorFlags);
        if (color == defaultColor)
            return changed;
        ImGui.SameLine();
        if (TablerIcons.Button(TablerIcon.ArrowBackUp))
        {
            color = defaultColor;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Reset");
        return changed;
    }
}
