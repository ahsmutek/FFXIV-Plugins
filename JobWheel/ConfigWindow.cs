using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
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
        (JobGroup.PhysicalRanged, "Physical Ranged DPS"),
        (JobGroup.Caster, "Magical Ranged DPS"),
        (JobGroup.Hand, "Disciples of the Hand"),
        (JobGroup.Land, "Disciples of the Land"),
    ];

    private readonly Configuration config;
    private readonly Action save;
    private readonly JobCatalog catalog;
    private readonly ITextureProvider textures;
    private readonly VirtualKey[] keys;
    private readonly string[] keyNames;

    // Refreshed on open so new gear sets and unlocks show up.
    private List<GearsetInfo> gearsets = [];
    private List<WheelJob> jobs = [];

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
        jobs = [.. catalog.Build(JobLayout.Combat), .. catalog.Build(JobLayout.Crafting)];
    }

    public override void Draw()
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
        var deadzone = config.Deadzone;
        if (ImGui.SliderFloat("Center deadzone", ref deadzone, 0.05f, 0.5f, "%.2f"))
        {
            config.Deadzone = deadzone;
            config.SelectThreshold = Math.Max(config.SelectThreshold, deadzone + 0.1f);
            changed = true;
        }
        var threshold = config.SelectThreshold;
        if (ImGui.SliderFloat("Select threshold", ref threshold, 0.2f, 0.95f, "%.2f"))
        {
            config.SelectThreshold = threshold;
            config.Deadzone = Math.Min(config.Deadzone, threshold - 0.1f);
            changed = true;
        }
        ImGui.TextWrapped("The stick has to tilt past the threshold to target a job, and back inside the deadzone to switch.");

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

        ImGui.Spacing();
        ImGui.Separator();
        changed |= DrawGearsets();

        if (changed) save();
    }

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
