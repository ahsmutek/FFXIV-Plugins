using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;

using Shortcuts;

namespace FlickWheel;

internal sealed class JobsSettings(Configuration config, JobCatalog catalog, IconCache icons, WheelOverlay overlay)
{
    private static readonly (JobGroup Group, string Label)[] GroupLabels =
    [
        (JobGroup.Tank, "Tanks"),
        (JobGroup.Healer, "Healers"),
        (JobGroup.Melee, "Melee DPS"),
        (JobGroup.Limited, "Limited Jobs"),
        (JobGroup.PhysicalRanged, "Physical Ranged DPS"),
        (JobGroup.Caster, "Magical Ranged DPS"),
        (JobGroup.Hand, "Disciples of the Hand"),
        (JobGroup.Land, "Disciples of the Land"),
        (JobGroup.Logs, "Logs"),
    ];

    private List<GearsetInfo> gearsets = [];
    private List<WheelJob> jobs = [];

    public void Refresh()
    {
        gearsets = JobCatalog.Gearsets();
        jobs = JobsModule.Kinds.SelectMany(kind => catalog.Build(JobLayout.Build(kind, config))).ToList();
    }

    public bool Draw(WheelKind kind)
    {
        var changed = false;
        ImGui.TextWrapped("Groups are listed clockwise. Drag a job sideways to move it; right-click it to choose the gear set it equips.");
        ImGui.Spacing();
        changed |= DrawColors();

        ImGui.Spacing();
        ImGui.Separator();
        if (config.GroupOrder.ContainsKey(kind) || config.NorthGroup.ContainsKey(kind))
        {
            if (ImGui.SmallButton("Reset group order"))
            {
                config.GroupOrder.Remove(kind);
                config.NorthGroup.Remove(kind);
                changed = true;
            }
        }

        var groups = JobLayout.GroupOrderOf(kind, config);
        for (var g = 0; g < groups.Count; g++)
            changed |= DrawGroupLayout(kind, groups, g);

        if (changed)
            overlay.PreviewWheel = JobsModule.WheelId(kind);
        return changed;
    }

    private bool DrawColors()
    {
        var changed = false;
        ImGui.TextDisabled("Colors (both job wheels)");
        var style = config.RoleStyle;
        if (GroupEditor.StyleButtons(ref style))
        {
            config.RoleStyle = style;
            changed = true;
        }

        var jobColors = config.UseJobColors;
        if (ImGui.Checkbox("Use job colors", ref jobColors))
        {
            config.UseJobColors = jobColors;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Color groups by role like the game: tanks blue, healers green, DPS red.\nTurn off to pick each group's color.");

        if (!config.UseJobColors && (config.RoleHues.Count > 0 || config.RoleColors.Count > 0))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Default colors"))
            {
                config.RoleHues.Clear();
                config.RoleColors.Clear();
                changed = true;
            }
        }

        return changed;
    }

    private bool DrawGroupLayout(WheelKind kind, List<JobGroup> groups, int index)
    {
        var changed = false;
        var group = groups[index];
        ImGui.PushID(group.ToString());
        ImGui.Spacing();

        if (GroupEditor.MoveArrows(index, groups.Count) is var step and not 0)
            changed |= MoveGroup(kind, groups, index, step);
        ImGui.SameLine();

        var hue = JobLayout.HueOf(group, config);
        var custom = Palette.Abgr(JobLayout.ColorOf(group, config));
        ImGui.BeginDisabled(config.UseJobColors);
        var picked = GroupEditor.ColorSwatch(ref hue, ref custom, config.RoleStyle);
        ImGui.EndDisabled();
        if (picked)
        {
            if (hue >= 0)
            {
                config.RoleHues[group] = hue;
                config.RoleColors.Remove(group);
            }
            else
            {
                config.RoleColors[group] = custom;
                config.RoleHues.Remove(group);
            }
            changed = true;
        }
        var color = JobLayout.ColorOf(group, config);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Label(group));
        ImGui.SameLine();
        if (ImGui.RadioButton("Pin to top", JobLayout.NorthOf(kind, config) == group))
        {
            config.NorthGroup[kind] = group;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Center this group at the top. With tanks pinned, healers stay at the bottom\nunless they sit right next to tanks.");

        var customized = config.RoleColors.ContainsKey(group) || config.RoleHues.ContainsKey(group) ||
                         config.JobOrder.ContainsKey(group);
        if (customized)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset"))
            {
                config.RoleColors.Remove(group);
                config.RoleHues.Remove(group);
                config.JobOrder.Remove(group);
                changed = true;
            }
        }

        var order = JobLayout.OrderOf(group, config);
        var move = GroupEditor.IconRow(order.Count, color, i =>
        {
            var abbreviation = order[i];
            var info = catalog.Info(abbreviation);
            var unlocked = jobs.Any(j => j.Abbreviation == abbreviation);
            var name = info?.Name ?? abbreviation;
            var texture = info is { } found
                ? icons.Get(found.IconId, JobLayout.Commands.ContainsKey(abbreviation))
                : (GameIcon?)null;
            return new RowIcon(abbreviation, texture, abbreviation, unlocked ? name : $"{name} (locked)", !unlocked);
        }, group == JobGroup.Logs ? null : i => changed |= DrawGearsetMenu(order[i]));

        if (move is var (from, to))
        {
            var job = order[from];
            order.RemoveAt(from);
            order.Insert(to, job);
            config.JobOrder[group] = order;
            changed = true;
        }

        if (group == JobGroup.Logs)
            changed |= DrawLogChoices();

        ImGui.PopID();
        return changed;
    }

    private bool DrawLogChoices()
    {
        var changed = false;
        var first = true;
        foreach (var key in JobLayout.Defaults(JobGroup.Logs))
        {
            if (!first)
                ImGui.SameLine();
            first = false;

            var shown = JobLayout.IsShown(key, config);
            if (ImGui.Checkbox($"{catalog.Info(key)?.Name ?? key}##show-{key}", ref shown))
            {
                config.ShownLogs[key] = shown;
                changed = true;
            }
        }

        return changed;
    }

    private bool MoveGroup(WheelKind kind, List<JobGroup> groups, int index, int step)
    {
        var other = index + step;
        if (other < 0 || other >= groups.Count)
            return false;
        var order = new List<JobGroup>(groups);
        (order[index], order[other]) = (order[other], order[index]);
        config.GroupOrder[kind] = order;
        return true;
    }

    private static string Label(JobGroup group) => GroupLabels.First(g => g.Group == group).Label;

    private static string Describe(GearsetInfo set) => $"#{set.Index + 1} {set.Name}";

    private bool DrawGearsetMenu(string abbreviation)
    {
        var job = jobs.FirstOrDefault(j => j.Abbreviation == abbreviation);
        config.GearsetOverrides.TryGetValue(abbreviation, out var number);
        var chosen = gearsets.FirstOrDefault(g => g.Index == number - 1);
        var label = number > 0 && chosen.Name != null ? Describe(chosen) : "Auto";
        if (!ImGui.BeginMenu($"Gear set: {label}", job != null))
            return false;

        var changed = false;
        if (ImGui.MenuItem("Auto (this job's first set)", number <= 0))
        {
            config.GearsetOverrides.Remove(abbreviation);
            changed = true;
        }

        var own = gearsets.Where(g => g.ClassJobId == job!.RowId).ToList();
        changed |= GearsetItems(abbreviation, own, number);
        var others = gearsets.Except(own).ToList();
        if (others.Count > 0 && ImGui.BeginMenu("Other jobs' sets"))
        {
            changed |= GearsetItems(abbreviation, others, number);
            ImGui.EndMenu();
        }

        ImGui.EndMenu();
        return changed;
    }

    private bool GearsetItems(string abbreviation, List<GearsetInfo> sets, int number)
    {
        var changed = false;
        foreach (var set in sets)
        {
            if (ImGui.MenuItem($"{Describe(set)}##{set.Index}", set.Index == number - 1))
            {
                config.GearsetOverrides[abbreviation] = set.Index + 1;
                changed = true;
            }
        }
        return changed;
    }
}
