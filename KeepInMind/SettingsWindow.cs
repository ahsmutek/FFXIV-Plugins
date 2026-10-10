using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Icons;

namespace KeepInMind;

internal sealed class SettingsWindow : Window
{
    private const ImGuiColorEditFlags ColorFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha;

    private static readonly (Grouping Value, string Label)[] GroupingOptions =
    [
        (Grouping.None, "Nothing"),
        (Grouping.Source, "Source"),
        (Grouping.Storage, "Retainers first, then source"),
    ];

    private static readonly (SortOrder Value, string Label)[] SortOptions =
    [
        (SortOrder.Manual, "Your order (drag to move)"),
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
        if (Combo("Group by", config.Grouping, GroupingOptions) is { } grouping)
        {
            config.Grouping = grouping;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Your own groups, NPC vendor, Crafting, and gathered items by zone, in the order below.\n" +
                             "An item with several sources goes in the first that applies; zones take the place\n" +
                             "of the first gathering class.\n" +
                             "With retainers first, items you still need from a retainer or the saddlebag come\n" +
                             "before all of those, under the retainer holding the most. Needs Allagan Tools.");

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
        ImGui.Spacing();
        ImGui.TextDisabled("Groups");
        DrawGroups();
    }

    private void DrawGroups()
    {
        var order = GroupOrder.Of(config);
        CustomGroup? remove = null;
        string? moveId = null;
        var step = 0;
        for (var i = 0; i < order.Count; i++)
        {
            var groupId = order[i];
            using var id = ImRaii.PushId(groupId);

            using (ImRaii.Disabled(i == 0))
            {
                if (ImGui.ArrowButton("##up", ImGuiDir.Up))
                    (moveId, step) = (groupId, -1);
            }
            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            using (ImRaii.Disabled(i == order.Count - 1))
            {
                if (ImGui.ArrowButton("##down", ImGuiDir.Down))
                    (moveId, step) = (groupId, 1);
            }
            ImGui.SameLine();

            if (config.Groups.Find(g => GroupOrder.Id(g) == groupId) is { } group)
            {
                var color = group.Color;
                if (ImGui.ColorEdit4("##color", ref color, ColorFlags))
                {
                    group.Color = color;
                    save();
                }
                ImGui.SameLine();
                if (TablerIcons.Button(TablerIcon.Trash))
                    remove = group;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Delete the group. Its items go back to their source's group.");
                ImGui.SameLine();
                var name = group.Name;
                ImGui.SetNextItemWidth(Math.Max(60f, ImGui.GetContentRegionAvail().X));
                if (ImGui.InputText("##name", ref name, 40) && name.Trim().Length > 0)
                {
                    group.Name = name.Trim();
                    save();
                }
            }
            else if (Enum.TryParse<Source>(groupId, out var source) && source != Source.None)
            {
                var color = config.SourceColors.GetValueOrDefault(source, Sources.DefaultColors[source]);
                if (ColorRow(Sources.Name(source), ref color, Sources.DefaultColors[source]))
                {
                    config.SourceColors[source] = color;
                    save();
                }
            }
            else
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Sources.Name(Source.None));
            }
        }

        if (moveId != null && GroupOrder.Move(config, moveId, step))
            save();
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
        ImGui.Spacing();
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled("Make your own group from an item's right-click menu in the list.");
        ImGui.PopTextWrapPos();
    }

    private static bool ColorRow(string label, ref Vector4 color, Vector4 fallback)
    {
        using var id = ImRaii.PushId(label);
        var changed = ImGui.ColorEdit4("##color", ref color, ColorFlags);
        ImGui.SameLine();
        using (ImRaii.Disabled(color == fallback))
        {
            if (TablerIcons.Button(TablerIcon.ArrowBackUp))
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
