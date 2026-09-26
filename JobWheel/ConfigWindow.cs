using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Textures;
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
    ];

    private static readonly GamepadButtons[] SwapButtons =
        [GamepadButtons.L1, GamepadButtons.R1, GamepadButtons.L2, GamepadButtons.R2];
    private static readonly string[] SwapButtonNames = ["LB", "RB", "LT", "RT"];

    private readonly Configuration config;
    private readonly Action save;
    private readonly JobCatalog catalog;
    private readonly ITextureProvider textures;
    private readonly VirtualKey[] keys;
    private readonly string[] keyNames;

    // Refreshed on open so new gear sets and unlocks show up.
    private List<GearsetInfo> gearsets = [];
    private List<WheelJob> jobs = [];
    private bool dirty;

    public ConfigWindow(Configuration config, Action save, JobCatalog catalog, ITextureProvider textures, IKeyState keyState)
        : base("Job Wheel Settings###JobWheelConfig")
    {
        this.config = config;
        this.save = save;
        this.catalog = catalog;
        this.textures = textures;
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
        gearsets = JobCatalog.Gearsets();
        jobs = [.. catalog.Build(JobLayout.Build(WheelKind.Combat, config)),
                .. catalog.Build(JobLayout.Build(WheelKind.Crafting, config))];
    }

    public override void OnClose()
    {
        if (dirty)
            save();
        dirty = false;
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

        ImGui.Spacing();
        ImGui.TextDisabled("Stick");
        var threshold = config.SelectThreshold;
        if (ImGui.SliderFloat("Select threshold", ref threshold, 0.35f, 0.95f, "%.2f"))
        {
            config.SelectThreshold = threshold;
            changed = true;
        }
        ImGui.TextWrapped("How far the stick has to tilt to target a job. The target stays when the stick springs back; let go of the shortcut to switch.");

        ImGui.Spacing();
        ImGui.TextDisabled("Shortcut (hold)");
        var modifier = (int)config.Modifier;
        ImGui.SetNextItemWidth(100f);
        if (ImGui.Combo("##modifier", ref modifier, ["None", "Ctrl", "Shift", "Alt"], 4))
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
        ImGui.TextWrapped("Drag a job sideways to move it within its group. Jobs run clockwise, left to right.");

        ImGui.Spacing();
        ImGui.TextDisabled("Color presets");
        changed |= DrawColorPresets();

        foreach (var (kind, title) in new[] { (WheelKind.Combat, "Combat wheel"), (WheelKind.Crafting, "Crafting & gathering wheel") })
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled(title);
            foreach (var group in JobLayout.GroupsOf(kind))
                changed |= DrawGroupLayout(group);
        }

        return changed;
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

    private bool DrawGroupLayout(JobGroup group)
    {
        var changed = false;
        ImGui.PushID(group.ToString());
        ImGui.Spacing();

        var color = JobLayout.ColorOf(group, config);
        if (ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            config.RoleColors[group] = ImGui.ColorConvertFloat4ToU32(color);
            changed = true;
        }
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Label(group));

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
                var wrap = textures.GetFromGameIcon(new GameIconLookup(found.IconId)).GetWrapOrEmpty();
                ImGui.ImageButton(wrap.Handle, new Vector2(icon), Vector2.Zero, Vector2.One, 2, Vector4.Zero,
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
            var inGroup = jobs.Where(j => j.Group == group).ToList();
            if (inGroup.Count == 0)
                continue;

            ImGui.Spacing();
            ImGui.TextUnformatted(label);
            foreach (var job in inGroup)
            {
                ImGui.PushID(job.Abbreviation);
                var wrap = textures.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrEmpty();
                ImGui.Image(wrap.Handle, new Vector2(ImGui.GetFrameHeight()));
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
