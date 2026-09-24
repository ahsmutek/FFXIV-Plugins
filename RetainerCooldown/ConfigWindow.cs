using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace RetainerCooldown;

internal sealed class ConfigWindow : Window
{
    // Number of consecutive icon IDs scanned per browser page.
    private const int PageSpan = 200;

    private readonly Plugin plugin;
    private int browseStart = -1;

    public ConfigWindow(Plugin plugin) : base("Retainer Cooldown Settings###RetainerCooldownConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(420, 430);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var changed = false;

        var presets = Configuration.IconSizePresets;
        var currentPreset = 0;
        for (var i = 0; i < presets.Length; i++)
            if (Math.Abs(presets[i] - plugin.Config.IconSize) < 0.5f) { currentPreset = i; break; }
        // Slider that snaps to the fixed preset sizes; the label shows the px value.
        if (ImGui.SliderInt("Icon size", ref currentPreset, 0, presets.Length - 1, $"{presets[currentPreset]:0}px"))
        {
            plugin.Config.IconSize = presets[currentPreset];
            changed = true;
        }

        var mode = (int)plugin.Config.DisplayMode;
        if (ImGui.Combo("Show venture", ref mode, new[] { "Fastest to complete", "Latest to complete" }, 2))
        {
            plugin.Config.DisplayMode = (VentureDisplayMode)mode;
            changed = true;
        }

        changed |= DrawIconPicker();

        var duration = plugin.Config.VentureDurationMinutes;
        if (ImGui.SliderInt("Radial duration", ref duration, 10, 1440, "%d min")) { plugin.Config.VentureDurationMinutes = duration; changed = true; }
        ImGui.TextWrapped("RetainerManager exposes the exact completion timestamp but not the venture's starting duration. Set this to 60 for Quick Exploration.");

        var locked = plugin.Config.Locked;
        if (ImGui.Checkbox("Lock HUD", ref locked)) { plugin.Config.Locked = locked; changed = true; }
        var timer = plugin.Config.ShowTimerText;
        if (ImGui.Checkbox("Show timer text", ref timer)) { plugin.Config.ShowTimerText = timer; changed = true; }
        var hide = plugin.Config.HideWhenNoVentures;
        if (ImGui.Checkbox("Hide when no ventures are active", ref hide)) { plugin.Config.HideWhenNoVentures = hide; changed = true; }

        if (changed) plugin.Save();
    }

    // Icon face selector: live preview, numeric entry, and a clickable grid.
    private bool DrawIconPicker()
    {
        var changed = false;
        var iconId = (int)plugin.Config.IconId;

        var preview = ImGui.GetTextLineHeightWithSpacing() * 1.6f;
        if (plugin.TryGetIcon(plugin.Config.IconId, out var cur) && cur != null)
        {
            ImGui.Image(cur.Handle, new Vector2(preview));
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("Icon ID", ref iconId))
        {
            plugin.Config.IconId = (uint)Math.Max(0, iconId);
            changed = true;
        }

        if (browseStart < 0)
            browseStart = Math.Max(0, iconId - iconId % PageSpan);

        if (ImGui.Button("< Prev")) browseStart = Math.Max(0, browseStart - PageSpan);
        ImGui.SameLine();
        if (ImGui.Button("Next >")) browseStart += PageSpan;
        ImGui.SameLine();
        ImGui.TextDisabled($"{browseStart}\u2013{browseStart + PageSpan - 1}");

        ImGui.BeginChild("iconGrid", new Vector2(0, 150), true);
        const float cell = 32f;
        var cols = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / (cell + 8f)));
        var shown = 0;
        for (var id = (uint)browseStart; id < browseStart + PageSpan; id++)
        {
            if (!plugin.TryGetIcon(id, out var w) || w == null) continue;

            ImGui.PushID(id.ToString());
            var selected = id == plugin.Config.IconId;
            if (selected)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.9f, .75f, .3f, .8f));
            if (ImGui.ImageButton(w.Handle, new Vector2(cell)))
            {
                plugin.Config.IconId = id;
                changed = true;
            }
            if (selected) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(id.ToString());
                ImGui.EndTooltip();
            }
            ImGui.PopID();

            if (++shown % cols != 0) ImGui.SameLine();
        }
        ImGui.EndChild();

        return changed;
    }
}
