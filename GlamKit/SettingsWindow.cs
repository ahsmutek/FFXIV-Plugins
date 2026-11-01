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

        ImGui.Spacing();
        var later = config.KeepForLaterEnabled;
        if (ImGui.Checkbox("Keep for Later", ref later))
        {
            config.KeepForLaterEnabled = later;
            save();
        }
        Describe("For gear worth keeping for its stats or item level. Open the item menu on gear and choose",
                 "\"Keep for Later\". It's bordered in your bags and Armoury Chest until you stop keeping it.");

        ImGui.Indent();
        var laterColor = config.LaterColor;
        if (ImGui.ColorEdit4("Border colour##later", ref laterColor, ColorFlags))
        {
            config.LaterColor = laterColor;
            save();
        }
        if (config.LaterColor != Configuration.DefaultLaterColor)
        {
            ImGui.SameLine();
            if (TablerIcons.Button(TablerIcon.ArrowBackUp, "later"))
            {
                config.LaterColor = Configuration.DefaultLaterColor;
                save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reset");
        }
        ImGui.Unindent();

        ImGui.Spacing();
        var jobLevels = config.JobLevelsEnabled;
        if (ImGui.Checkbox("Job Levels", ref jobLevels))
        {
            config.JobLevelsEnabled = jobLevels;
            save();
        }
        Describe("Gear tooltips show your level on each job that can wear it: green from its level to a few",
                 "above, yellow a few below, grey otherwise. Gear most jobs can wear shows only the green jobs.");

        ImGui.Indent();
        var green = config.GreenAbove;
        if (LevelSlider("Green up to", "levels above", ref green))
        {
            config.GreenAbove = green;
            save();
        }

        var yellow = config.YellowBelow;
        if (LevelSlider("Yellow up to", "levels below", ref yellow))
        {
            config.YellowBelow = yellow;
            save();
        }

        var uncolour = config.UncolourUnwearableLevel;
        if (ImGui.Checkbox("Colour the equip level only if your current job can wear it", ref uncolour))
        {
            config.UncolourUnwearableLevel = uncolour;
            save();
        }
        ImGui.Unindent();
    }

    private static bool LevelSlider(string before, string after, ref int value)
    {
        var start = ImGui.GetCursorPosX();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(before);
        ImGui.SameLine(start + ImGui.CalcTextSize("Yellow up to").X + ImGui.GetStyle().ItemSpacing.X);
        ImGui.SetNextItemWidth(120);
        var changed = ImGui.SliderInt($"##{before}", ref value, 0, 30);
        ImGui.SameLine();
        ImGui.TextUnformatted(after);
        return changed;
    }

    private static void Describe(params string[] lines)
    {
        ImGui.Indent();
        foreach (var line in lines)
            ImGui.TextDisabled(line);
        ImGui.Unindent();
    }
}
