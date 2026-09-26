using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace JobWheel;

internal sealed class ConfigWindow : Window
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

    private static readonly GamepadButtons[] SwapButtons =
        [GamepadButtons.L1, GamepadButtons.R1, GamepadButtons.L2, GamepadButtons.R2];
    private static readonly string[] SwapButtonNames = ["LB", "RB", "LT", "RT"];

    private static readonly string[] ConfirmNames = ["I let go of the shortcut", "The stick returns to center"];
    private static readonly string[] ModifierNames = ["None", "Ctrl", "Shift", "Alt"];
    private static readonly string[] WheelNames = ["Combat wheel", "Crafting & gathering wheel"];
    private static readonly (WheelKind Kind, string Title)[] WheelTitles =
        [(WheelKind.Combat, "Combat wheel"), (WheelKind.Crafting, "Crafting & gathering wheel")];

    private readonly Configuration config;
    private readonly Action save;
    private readonly JobCatalog catalog;
    private readonly WheelOverlay overlay;
    private readonly IconCache icons;
    private readonly VirtualKey[] keys;
    private readonly string[] keyNames;

    // Refreshed on open so new gear sets and unlocks show up.
    private List<GearsetInfo> gearsets = [];
    private List<WheelJob> jobs = [];
    private bool dirty;

    public ConfigWindow(Configuration config, Action save, JobCatalog catalog, WheelOverlay overlay,
                        IconCache icons, IKeyState keyState)
        : base("Job Wheel Settings###JobWheelConfig")
    {
        this.config = config;
        this.save = save;
        this.catalog = catalog;
        this.overlay = overlay;
        this.icons = icons;
        keys = keyState.GetValidVirtualKeys()
                       .Where(k => k is not (VirtualKey.CONTROL or VirtualKey.SHIFT or VirtualKey.MENU or
                                             VirtualKey.LBUTTON or VirtualKey.RBUTTON or VirtualKey.MBUTTON))
                       .ToArray();
        keyNames = keys.Select(k => k.GetFancyName()).ToArray();
        Size = new Vector2(460, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        overlay.PreviewKind = config.LastWheel;
        gearsets = JobCatalog.Gearsets();
        jobs = [.. catalog.Build(JobLayout.Build(WheelKind.Combat, config)),
                .. catalog.Build(JobLayout.Build(WheelKind.Crafting, config))];
    }

    public override void OnClose()
    {
        if (dirty)
            save();
        dirty = false;
        overlay.Preview = false;
    }

    public override void Draw()
    {
        var changed = false;

        if (ImGui.BeginTabBar("tabs"))
        {
            if (ImGui.BeginTabItem("General"))
            {
                changed |= DrawGeneral();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Wheel layout"))
            {
                changed |= DrawLayout();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Gear sets"))
            {
                changed |= DrawGearsets();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (changed)
            overlay.InvalidatePreview();

        // Sliders, color pickers and icon drags change the config every frame;
        // write it to disk once they're let go instead.
        dirty |= changed;
        if (dirty && !ImGui.IsAnyItemActive())
        {
            save();
            dirty = false;
        }
    }

    private bool DrawGeneral()
    {
        var changed = false;

        ImGui.TextDisabled("Wheel");
        var scale = config.Scale;
        if (ImGui.SliderFloat("Wheel scale", ref scale, 0.5f, 2f, "%.2fx")) { config.Scale = scale; changed = true; }
        var iconSize = config.IconSize;
        if (ImGui.SliderFloat("Icon size", ref iconSize, 24f, 64f, "%.0fpx")) { config.IconSize = iconSize; changed = true; }
        var offset = new Vector2(config.OffsetX, config.OffsetY);
        if (ImGui.DragFloat2("Offset from center", ref offset, 1f, -2000f, 2000f, "%.0f"))
        {
            config.OffsetX = offset.X;
            config.OffsetY = offset.Y;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Center"))
        {
            config.OffsetX = 0;
            config.OffsetY = 0;
            changed = true;
        }

        DrawPreviewControls();

        ImGui.Spacing();
        ImGui.TextDisabled("Stick");
        var threshold = config.SelectThreshold;
        if (ImGui.SliderFloat("Select threshold", ref threshold, 0.35f, 0.95f, "%.2f"))
        {
            config.SelectThreshold = threshold;
            changed = true;
        }
        ImGui.TextWrapped("How far the stick has to tilt to target a job.");

        var confirm = (int)config.Confirm;
        if (ImGui.Combo("Switch job when", ref confirm, ConfirmNames, ConfirmNames.Length))
        {
            config.Confirm = (ConfirmMode)confirm;
            changed = true;
        }
        ImGui.TextWrapped(config.Confirm == ConfirmMode.ShortcutRelease
            ? "The target stays when the stick springs back, so you can flick and then let go of the shortcut."
            : "Flick toward a job and let the stick spring back. Letting go of the shortcut with a job targeted also switches.");

        ImGui.Spacing();
        ImGui.TextDisabled("Shortcut (hold)");
        var modifier = (int)config.Modifier;
        ImGui.SetNextItemWidth(100f);
        if (ImGui.Combo("##modifier", ref modifier, ModifierNames, ModifierNames.Length))
        {
            config.Modifier = (HotkeyModifier)modifier;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("+");
        ImGui.SameLine();
        var keyIndex = Math.Max(0, Array.IndexOf(keys, config.Key));
        ImGui.SetNextItemWidth(140f);
        if (ImGui.Combo("##key", ref keyIndex, keyNames, keyNames.Length))
        {
            config.Key = keys[keyIndex];
            changed = true;
        }

        var buttonIndex = Math.Max(0, Array.IndexOf(SwapButtons, config.SwapButton));
        ImGui.SetNextItemWidth(100f);
        if (ImGui.Combo("Tap to swap wheels", ref buttonIndex, SwapButtonNames, SwapButtonNames.Length))
        {
            config.SwapButton = SwapButtons[buttonIndex];
            changed = true;
        }

        return changed;
    }

    // Per group: its band color, and its jobs in clockwise order. Drag an icon
    // sideways onto a neighbour to swap them. Groups keep their place on the wheel.
    private bool DrawLayout()
    {
        var changed = false;
        ImGui.TextWrapped("Groups are listed clockwise; use the arrows to move a group. Drag a job sideways to move it within its group (left to right is clockwise). Use Pin to top to choose the group centered at the top. With tanks pinned, healers are also kept at the bottom unless they sit right next to tanks.");
        ImGui.Spacing();
        DrawPreviewControls();

        ImGui.Spacing();
        ImGui.TextDisabled("Color presets");
        changed |= DrawColorPresets();

        foreach (var (kind, title) in WheelTitles)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled(title);
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
                    overlay.PreviewKind = kind; // show the wheel being edited
                }
            }
        }

        return changed;
    }

    // Preview checkbox plus which wheel it shows; used on both the General and Wheel layout tabs.
    private void DrawPreviewControls()
    {
        var preview = overlay.Preview;
        if (ImGui.Checkbox("Show wheel preview", ref preview))
            overlay.Preview = preview;
        ImGui.SameLine();
        var kind = (int)overlay.PreviewKind;
        ImGui.SetNextItemWidth(170f);
        if (ImGui.Combo("##previewKind", ref kind, WheelNames, WheelNames.Length))
            overlay.PreviewKind = (WheelKind)kind;
        ImGui.TextDisabled("Drag the preview to move the wheel.");
    }

    // One button per palette with a strip of its colors; clicking recolors every group.
    // The first preset is the default, so picking it clears the custom colors.
    private bool DrawColorPresets()
    {
        var changed = false;
        var groups = JobLayout.GroupsOf(WheelKind.Combat).Concat(JobLayout.GroupsOf(WheelKind.Crafting)).ToArray();
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
        ImGui.BeginDisabled(index == 0);
        if (ImGui.ArrowButton("##up", ImGuiDir.Up))
            changed |= MoveGroup(kind, groups, index, -1);
        ImGui.EndDisabled();
        ImGui.SameLine(0, 2f);
        ImGui.BeginDisabled(index == groups.Count - 1);
        if (ImGui.ArrowButton("##down", ImGuiDir.Down))
            changed |= MoveGroup(kind, groups, index, 1);
        ImGui.EndDisabled();
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
        // Every button is the icon plus 2px frame padding per side; 4px between buttons.
        const float icon = 36f, gap = 4f, pitch = icon + 4f + gap;
        var rowStart = ImGui.GetCursorScreenPos().X;
        var dragged = -1;
        for (var i = 0; i < order.Count; i++)
        {
            var abbreviation = order[i];
            var info = catalog.Info(abbreviation);
            var unlocked = jobs.Any(j => j.Abbreviation == abbreviation);

            ImGui.PushID(abbreviation);
            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * new Vector4(1.4f, 1.4f, 1.4f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, color * new Vector4(1.7f, 1.7f, 1.7f, 1f));
            if (info is { } found)
            {
                ImGui.ImageButton(icons.Handle(found.IconId, JobLayout.Commands.ContainsKey(abbreviation)), new Vector2(icon),
                                  Vector2.Zero, Vector2.One, 2, Vector4.Zero,
                                  new Vector4(1f, 1f, 1f, unlocked ? 1f : .4f));
            }
            else
            {
                ImGui.Button(abbreviation, new Vector2(icon + 4f));
            }
            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered() && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                ImGui.SetTooltip(unlocked ? info?.Name ?? abbreviation : $"{info?.Name ?? abbreviation} (locked)");
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                dragged = i;

            ImGui.PopID();
            if (i < order.Count - 1)
                ImGui.SameLine(0, gap);
        }

        // Move the dragged job to whichever slot the mouse is over (clamped to the row).
        if (dragged >= 0)
        {
            var slot = (int)MathF.Floor((ImGui.GetMousePos().X - rowStart + gap / 2) / pitch);
            slot = Math.Clamp(slot, 0, order.Count - 1);
            if (slot != dragged)
            {
                var job = order[dragged];
                order.RemoveAt(dragged);
                order.Insert(slot, job);
                config.JobOrder[group] = order;
                changed = true;
            }
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
