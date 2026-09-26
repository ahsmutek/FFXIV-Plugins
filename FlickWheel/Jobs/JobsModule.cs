using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FlickWheel;

// The combat and crafting & gathering wheels: every job in a fixed, role-grouped spot,
// plus the Crafting and Gathering Log.
internal sealed class JobsModule : IWheelModule
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
        (JobGroup.Logs, "Crafting & Gathering Logs"),
    ];

    private static readonly WheelKind[] Kinds = [WheelKind.Combat, WheelKind.Crafting];

    private readonly Configuration config;
    private readonly JobCatalog catalog;
    private readonly JobSwitcher switcher;
    private readonly IconCache icons;
    private readonly WheelOverlay overlay;

    // Refreshed when settings open so new gear sets and unlocks show up.
    private List<GearsetInfo> gearsets = [];
    private List<WheelJob> jobs = [];

    public JobsModule(Configuration config, JobCatalog catalog, JobSwitcher switcher, IconCache icons, WheelOverlay overlay)
    {
        this.config = config;
        this.catalog = catalog;
        this.switcher = switcher;
        this.icons = icons;
        this.overlay = overlay;
        Wheels = Kinds.Select(kind => (IWheel)new JobsWheel(this, kind)).ToArray();
        SettingsTabs = [new("Job layout", DrawLayout), new("Gear sets", DrawGearsets)];
    }

    public string Id => "jobs";
    public string Name => "Jobs";
    public IReadOnlyList<IWheel> Wheels { get; }
    public IReadOnlyList<SettingsTab> SettingsTabs { get; }

    public static string WheelId(WheelKind kind) => kind == WheelKind.Combat ? "combat" : "crafting";

    private static string Title(WheelKind kind) =>
        kind == WheelKind.Combat ? "Combat wheel" : "Crafting & gathering wheel";

    public void OnSettingsOpen()
    {
        gearsets = JobCatalog.Gearsets();
        jobs = Kinds.SelectMany(kind => catalog.Build(JobLayout.Build(kind, config))).ToList();
    }

    // Jobs without a gear set are faded. Log entries never are.
    private WheelEntry ToEntry(WheelJob job) =>
        new(job.Slot, job.Name, job.IconId, () => switcher.Switch(job),
            KeyOutIcon: job.CommandId != 0,
            Faded: job.CommandId == 0 && job.GearsetIndex < 0,
            IsCurrent: job.IsCurrent);

    private sealed class JobsWheel(JobsModule module, WheelKind kind) : IWheel
    {
        public string Id => WheelId(kind);
        public string Title => JobsModule.Title(kind);
        public int MaxSlots => JobLayout.SlotCount(kind);
        public WheelRing Layout() => JobLayout.Build(kind, module.config);
        public List<WheelEntry> Entries(WheelRing ring) => module.catalog.Build(ring).Select(module.ToEntry).ToList();
    }

    // Per group: its band color, and its jobs in clockwise order. Drag an icon
    // sideways onto a neighbour to swap them. Groups keep their place on the wheel.
    private bool DrawLayout()
    {
        var changed = false;
        ImGui.TextWrapped("Groups are listed clockwise; use the arrows to move a group. Drag a job sideways to move it within its group (left to right is clockwise). Use Pin to top to choose the group centered at the top. With tanks pinned, healers are also kept at the bottom unless they sit right next to tanks.");
        ImGui.Spacing();
        overlay.DrawPreviewControls();

        ImGui.Spacing();
        ImGui.TextDisabled("Color presets");
        changed |= DrawColorPresets();

        foreach (var kind in Kinds)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled(Title(kind));
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
                    overlay.PreviewWheel = WheelId(kind); // show the wheel being edited
                }
            }
        }

        return changed;
    }

    // One button per palette with a strip of its colors; clicking recolors every group.
    // The first preset is the default, so picking it clears the custom colors.
    private bool DrawColorPresets()
    {
        var changed = false;
        var groups = Kinds.SelectMany(JobLayout.GroupsOf).ToArray();
        const float buttonWidth = 110f;
        for (var p = 0; p < JobLayout.ColorPresets.Length; p++)
        {
            var (name, colors) = JobLayout.ColorPresets[p];
            ImGui.PushID(name);
            if (ImGui.Button(name, new Vector2(buttonWidth, 0)))
            {
                config.RoleColors.Clear();
                if (p > 0)
                {
                    foreach (var (group, color) in colors)
                        config.RoleColors[group] = ImGui.ColorConvertFloat4ToU32(color);
                }
                changed = true;
            }

            foreach (var group in groups)
            {
                ImGui.SameLine(0, 2f);
                ImGui.ColorButton($"##{group}", colors[group],
                                  ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop,
                                  new Vector2(ImGui.GetFrameHeight()));
            }
            ImGui.PopID();
        }

        return changed;
    }

    private bool DrawGroupLayout(WheelKind kind, List<JobGroup> groups, int index)
    {
        var changed = false;
        var group = groups[index];
        ImGui.PushID(group.ToString());
        ImGui.Spacing();

        // Move the group one step counter-clockwise (up) or clockwise (down).
        if (GroupEditor.MoveArrows(index, groups.Count) is var step and not 0)
            changed |= MoveGroup(kind, groups, index, step);
        ImGui.SameLine();

        var color = JobLayout.ColorOf(group, config);
        if (ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            config.RoleColors[group] = ImGui.ColorConvertFloat4ToU32(color);
            changed = true;
        }
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Label(group));
        ImGui.SameLine();
        if (ImGui.RadioButton("Pin to top", JobLayout.NorthOf(kind, config) == group))
        {
            config.NorthGroup[kind] = group;
            changed = true;
        }

        var customized = config.RoleColors.ContainsKey(group) || config.JobOrder.ContainsKey(group);
        if (customized)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset"))
            {
                config.RoleColors.Remove(group);
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

        ImGui.PopID();
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

    private bool DrawGearsets()
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

        foreach (var (group, label) in GroupLabels)
        {
            var inGroup = jobs.Where(j => j.Group == group && j.CommandId == 0).ToList();
            if (inGroup.Count == 0)
                continue;

            ImGui.Spacing();
            ImGui.TextUnformatted(label);
            foreach (var job in inGroup)
            {
                ImGui.PushID(job.Abbreviation);
                var handle = icons.Handle(job.IconId, false);
                ImGui.Image(handle, new Vector2(ImGui.GetFrameHeight()));
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(job.Name);
                ImGui.SameLine(200f);

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
