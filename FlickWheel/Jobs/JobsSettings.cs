using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

    private bool IsOn(WheelKind kind) => !config.WheelsOn.TryGetValue(JobsModule.WheelId(kind), out var on) || on;

    public bool DrawLayout()
    {
        var changed = false;
        ImGui.TextWrapped("Groups are listed clockwise; use the arrows to move a group. Drag a job sideways to move it within its group (left to right is clockwise). Use Pin to top to choose the group centered at the top. With tanks pinned, healers are also kept at the bottom unless they sit right next to tanks.");
        ImGui.Spacing();
        changed |= DrawColors();

        foreach (var kind in JobsModule.Kinds.Where(IsOn))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled(JobsModule.Title(kind));
            if (config.GroupOrder.ContainsKey(kind) || config.NorthGroup.ContainsKey(kind))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Reset groups##{kind}"))
                {
                    config.GroupOrder.Remove(kind);
                    config.NorthGroup.Remove(kind);
                    changed = true;
                }
            }

            var groups = JobLayout.GroupOrderOf(kind, config);
            for (var g = 0; g < groups.Count; g++)
            {
                if (DrawGroupLayout(kind, groups, g))
                {
                    changed = true;
                    overlay.PreviewWheel = JobsModule.WheelId(kind);
                }
            }
        }

        return changed;
    }

    private bool DrawColors()
    {
        var changed = false;
        ImGui.TextDisabled("Colors");
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
                ? icons.Handle(found.IconId, JobLayout.Commands.ContainsKey(abbreviation))
                : (ImTextureID?)null;
            return new RowIcon(abbreviation, texture, abbreviation, unlocked ? name : $"{name} (locked)", !unlocked);
        });

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

    private string ShortName(WheelJob job) =>
        catalog.Abbreviation(job.RowId) is { Length: > 0 } name ? name : job.Abbreviation;

    public bool DrawGearsets()
    {
        var changed = false;
        ImGui.TextDisabled("Gear set per job");
        ImGui.TextWrapped("Auto uses the first gear set for that job.");

        if (jobs.Count == 0)
        {
            ImGui.TextColored(new Vector4(.7f, .7f, .74f, 1f), "Log in to see your jobs.");
            return false;
        }

        var options = new string[gearsets.Count + 1];
        options[0] = "Auto";
        for (var i = 0; i < gearsets.Count; i++)
            options[i + 1] = $"#{gearsets[i].Index + 1} {gearsets[i].Name}";

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var widestName = jobs.Where(j => j.CommandId == 0)
                             .Select(j => ImGui.CalcTextSize(ShortName(j)).X)
                             .DefaultIfEmpty(0f)
                             .Max();
        var dropdownX = ImGui.GetCursorPosX() + ImGui.GetFrameHeight() + spacing + widestName + spacing;

        var groupsOn = JobsModule.Kinds.Where(IsOn).SelectMany(JobLayout.GroupsOf).ToHashSet();
        foreach (var (group, label) in GroupLabels.Where(g => groupsOn.Contains(g.Group)))
        {
            var inGroup = jobs.Where(j => j.Group == group && j.CommandId == 0).ToList();
            if (inGroup.Count == 0)
                continue;

            ImGui.Spacing();
            ImGui.TextUnformatted(label);
            foreach (var job in inGroup)
            {
                ImGui.PushID(job.Abbreviation);
                var size = new Vector2(ImGui.GetFrameHeight());
                var min = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(min, min + size, ImGui.GetColorU32(JobLayout.ColorOf(job.Group, config)), 3f);
                ImGui.Image(icons.Handle(job.IconId, false), size);
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(ShortName(job));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(job.Name);
                ImGui.SameLine(dropdownX);

                config.GearsetOverrides.TryGetValue(job.Abbreviation, out var number);
                var selected = gearsets.FindIndex(g => g.Index == number - 1) + 1;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##gearset", ref selected, options, options.Length))
                {
                    if (selected == 0)
                        config.GearsetOverrides.Remove(job.Abbreviation);
                    else
                        config.GearsetOverrides[job.Abbreviation] = gearsets[selected - 1].Index + 1;
                    changed = true;
                }
                ImGui.PopID();
            }
        }

        return changed;
    }
}
