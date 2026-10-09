using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace KeepInMind;

internal sealed class SettingsWindow : Window
{
    private const ImGuiColorEditFlags ColorFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha;

    private static readonly (SortOrder Value, string Label)[] SortOptions =
    [
        (SortOrder.Added, "Order added"),
        (SortOrder.Name, "Name"),
        (SortOrder.Remaining, "Most still needed"),
    ];

    private static readonly (CompletedMode Value, string Label)[] CompletedOptions =
    [
        (CompletedMode.InPlace, "Keep in place"),
        (CompletedMode.Section, "Move to a Completed section"),
        (CompletedMode.Hidden, "Hide"),
    ];

    private readonly Configuration config;
    private readonly Action save;

    public SettingsWindow(Configuration config, Action save)
        : base("Keep in Mind settings###KeepInMindSettings")
    {
        this.config = config;
        this.save = save;
        Size = new Vector2(340, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextDisabled("List");
        var grouped = config.GroupBySource;
        if (ImGui.Checkbox("Group by source", ref grouped))
        {
            config.GroupBySource = grouped;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Your own groups first, then Botanist, Miner, Fisher, NPC vendor and Crafting.\n" +
                             "An item with several sources goes in the first that applies.");

        if (Combo("Sort", config.Sort, SortOptions) is { } sort)
        {
            config.Sort = sort;
            save();
        }
        if (Combo("Completed items", config.Completed, CompletedOptions) is { } completed)
        {
            config.Completed = completed;
            save();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Colours");
        var enough = config.StorageEnoughColor;
        if (ColorRow("Enough in storage", ref enough, Sources.DefaultStorageEnoughColor))
        {
            config.StorageEnoughColor = enough;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Your bags, retainers and saddlebag hold all you need. Needs Allagan Tools.");
        var storage = config.StorageColor;
        if (ColorRow("Some in storage", ref storage, Sources.DefaultStorageColor))
        {
            config.StorageColor = storage;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Your retainers or saddlebag hold some, but not all you need. Needs Allagan Tools.");
        foreach (var source in Sources.Order)
        {
            var color = config.SourceColors.GetValueOrDefault(source, Sources.DefaultColors[source]);
            if (ColorRow(Sources.Name(source), ref color, Sources.DefaultColors[source]))
            {
                config.SourceColors[source] = color;
                save();
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Your groups");
        if (config.Groups.Count == 0)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted("None yet. Right-click an item in the list and choose Group to make one.");
            ImGui.PopTextWrapPos();
        }
        DrawGroups();
    }

    private void DrawGroups()
    {
        CustomGroup? remove = null;
        (int From, int To)? move = null;
        for (var i = 0; i < config.Groups.Count; i++)
        {
            var group = config.Groups[i];
            using var id = ImRaii.PushId(group.Id.ToString());

            var color = group.Color;
            if (ImGui.ColorEdit4("##color", ref color, ColorFlags))
            {
                group.Color = color;
                save();
            }
            ImGui.SameLine();
            var name = group.Name;
            ImGui.SetNextItemWidth(Math.Max(60f, ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() * 3 -
                                                 ImGui.GetStyle().ItemSpacing.X * 3));
            if (ImGui.InputText("##name", ref name, 40) && name.Trim().Length > 0)
            {
                group.Name = name.Trim();
                save();
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(i == 0))
            {
                if (ImGui.ArrowButton("##up", ImGuiDir.Up))
                    move = (i, i - 1);
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(i == config.Groups.Count - 1))
            {
                if (ImGui.ArrowButton("##down", ImGuiDir.Down))
                    move = (i, i + 1);
            }
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                remove = group;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Delete the group. Its items go back to their source's group.");
        }

        if (move is var (from, to))
        {
            (config.Groups[from], config.Groups[to]) = (config.Groups[to], config.Groups[from]);
            save();
        }
        if (remove != null)
        {
            config.Groups.Remove(remove);
            foreach (var kept in config.Items)
            {
                if (kept.GroupId == remove.Id)
                    kept.GroupId = null;
            }
            save();
        }
    }

    private static bool ColorRow(string label, ref Vector4 color, Vector4 fallback)
    {
        using var id = ImRaii.PushId(label);
        var changed = ImGui.ColorEdit4("##color", ref color, ColorFlags);
        ImGui.SameLine();
        using (ImRaii.Disabled(color == fallback))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
            {
                color = fallback;
                changed = true;
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Reset");
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(label);
        return changed;
    }

    private static T? Combo<T>(string label, T current, (T Value, string Label)[] options) where T : struct, Enum
    {
        T? picked = null;
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        var shown = Array.Find(options, o => o.Value.Equals(current)).Label ?? current.ToString();
        if (!ImGui.BeginCombo(label, shown))
            return null;
        foreach (var (value, text) in options)
        {
            if (ImGui.Selectable(text, value.Equals(current)))
                picked = value;
        }
        ImGui.EndCombo();
        return picked;
    }
}
