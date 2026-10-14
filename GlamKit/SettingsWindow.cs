using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Icons;

namespace GlamKit;

internal sealed class SettingsWindow : Window
{
    private const ImGuiColorEditFlags ColorFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha;

    private readonly Configuration config;
    private readonly Action save;

    public SettingsWindow(Configuration config, Action save)
        : base("GlamKit settings###GlamKitSettings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.save = save;
    }

    public override void Draw()
    {
        var glamOff = config.GlamOffEnabled;
        if (ImGui.Checkbox("Glam Off", ref glamOff))
        {
            config.GlamOffEnabled = glamOff;
            save();
        }
        Describe("Open the item menu on glamoured gear (right-click, or X on a controller) and choose",
                 "\"Glam Off\" to see it in the Fitting Room without its glamour.");

        ImGui.Spacing();
        var keeper = config.GlamKeeperEnabled;
        if (ImGui.Checkbox("Glam Keeper", ref keeper))
        {
            config.GlamKeeperEnabled = keeper;
            save();
        }
        Describe("Open the item menu on gear and choose \"Keep for Glam\". Kept gear is bordered in your",
                 "bags and Armoury Chest. Once it's in the glamour dresser or armoire, it stops being kept.",
                 "/glamkit lists it all.");

        ImGui.Indent();
        var color = config.KeeperColor;
        if (ImGui.ColorEdit4("Border colour", ref color, ColorFlags))
        {
            config.KeeperColor = color;
            save();
        }
        if (config.KeeperColor != Configuration.DefaultKeeperColor)
        {
            ImGui.SameLine();
            if (TablerIcons.Button(TablerIcon.ArrowBackUp))
            {
                config.KeeperColor = Configuration.DefaultKeeperColor;
                save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reset");
        }
        ImGui.Unindent();
    }

    private static void Describe(params string[] lines)
    {
        ImGui.Indent();
        foreach (var line in lines)
            ImGui.TextDisabled(line);
        ImGui.Unindent();
    }
}
