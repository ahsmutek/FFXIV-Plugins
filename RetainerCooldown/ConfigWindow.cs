using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace RetainerCooldown;

internal sealed class ConfigWindow : Window
{
    // Number of consecutive icon IDs scanned per browser page.
    private const int PageSpan = 200;

    private static readonly string[] DisplayModeLabels = ["Fastest to complete", "Latest to complete"];
    private static readonly string[] LengthLabels =
        Array.ConvertAll(Configuration.VentureLengthPresets, preset => preset.Label);

    private readonly Plugin plugin;
    private int browseStart = -1;
    // Icon IDs on the current browser page that exist, found once per page.
    private readonly List<uint> pageIcons = [];
    private int pageIconsStart = -1;
    // Sliders and the ID field change the config every frame; save once they're let go.
    private bool dirty;
    // Which slot the ID field and grid write to.
    private bool editingComplete;
    // Icon IDs when the window was opened, so a pick can be undone.
    private uint savedIconId;
    private uint savedCompleteIconId;

    public ConfigWindow(Plugin plugin) : base("Retainer Cooldown Settings###RetainerCooldownConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(480, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        savedIconId = plugin.Config.IconId;
        savedCompleteIconId = plugin.Config.CompleteIconId;
    }

    public override void OnClose()
    {
        if (dirty)
            plugin.Save();
        dirty = false;
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
        if (ImGui.Combo("Show venture", ref mode, DisplayModeLabels, DisplayModeLabels.Length))
        {
            plugin.Config.DisplayMode = (VentureDisplayMode)mode;
            changed = true;
        }

        changed |= DrawIconPicker();

        var lengths = Configuration.VentureLengthPresets;
        var durationIndex = 0;
        for (var i = 0; i < lengths.Length; i++)
            if (lengths[i].Minutes == plugin.Config.VentureDurationMinutes) { durationIndex = i; break; }
        if (ImGui.Combo("Radial duration", ref durationIndex, LengthLabels, LengthLabels.Length))
        {
            plugin.Config.VentureDurationMinutes = lengths[durationIndex].Minutes;
            changed = true;
        }
        ImGui.TextWrapped("The game reports when a venture finishes, not how long it was. Pick the length that matches the venture so the radial fill lines up. 1h is Quick Exploration.");

        var locked = plugin.Config.Locked;
        if (ImGui.Checkbox("Lock HUD", ref locked)) { plugin.Config.Locked = locked; changed = true; }
        var timer = plugin.Config.ShowTimerText;
        if (ImGui.Checkbox("Show timer text", ref timer)) { plugin.Config.ShowTimerText = timer; changed = true; }
        var hide = plugin.Config.HideWhenNoVentures;
        if (ImGui.Checkbox("Hide when no ventures are active", ref hide)) { plugin.Config.HideWhenNoVentures = hide; changed = true; }

        dirty |= changed;
        if (dirty && !ImGui.IsAnyItemActive())
        {
            plugin.Save();
            dirty = false;
        }
    }

    private bool DrawIconPicker()
    {
        var changed = false;
        DrawIconSlot(false, "In progress");
        ImGui.SameLine(0, 16f);
        DrawIconSlot(true, "All complete");

        var iconDirty = plugin.Config.IconId != savedIconId || plugin.Config.CompleteIconId != savedCompleteIconId;
        if (iconDirty)
        {
            ImGui.SameLine(0, 16f);
            if (ImGui.Button("Cancel"))
            {
                plugin.Config.IconId = savedIconId;
                plugin.Config.CompleteIconId = savedCompleteIconId;
                changed = true;
            }
        }

        var iconId = (int)(editingComplete ? plugin.Config.CompleteIconId : plugin.Config.IconId);
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt(editingComplete ? "Complete icon ID" : "Icon ID", ref iconId))
        {
            var id = (uint)Math.Max(0, iconId);
            if (editingComplete) plugin.Config.CompleteIconId = id;
            else plugin.Config.IconId = id;
            changed = true;
        }

        if (browseStart < 0)
            browseStart = Math.Max(0, iconId - iconId % PageSpan);

        if (ImGui.Button("< Prev")) browseStart = Math.Max(0, browseStart - PageSpan);
        ImGui.SameLine();
        if (ImGui.Button("Next >")) browseStart += PageSpan;
        ImGui.SameLine();
        ImGui.TextDisabled($"{browseStart}\u2013{browseStart + PageSpan - 1}");

        ImGui.BeginChild("iconGrid", new Vector2(0, 240), true);
        const float cell = 48f;
        var cols = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / (cell + 8f)));
        var shown = 0;
        foreach (var id in PageIcons())
        {
            if (!plugin.TryGetIcon(id, out var w) || w == null) continue;

            ImGui.PushID((int)id);
            var selectedId = editingComplete ? plugin.Config.CompleteIconId : plugin.Config.IconId;
            var selected = id == selectedId;
            if (selected)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.9f, .75f, .3f, .8f));
            if (ImGui.ImageButton(w.Handle, new Vector2(cell)))
            {
                if (editingComplete) plugin.Config.CompleteIconId = id;
                else plugin.Config.IconId = id;
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

    private List<uint> PageIcons()
    {
        if (pageIconsStart == browseStart)
            return pageIcons;

        pageIcons.Clear();
        for (var id = (uint)browseStart; id < browseStart + PageSpan; id++)
            if (plugin.IconExists(id))
                pageIcons.Add(id);
        pageIconsStart = browseStart;
        return pageIcons;
    }

    // Selectable preview for one HUD icon slot. Clicking it points the picker at that slot.
    private void DrawIconSlot(bool complete, string label)
    {
        var iconId = complete ? plugin.Config.CompleteIconId : plugin.Config.IconId;
        var selected = editingComplete == complete;

        ImGui.BeginGroup();
        ImGui.PushID(label);
        if (selected)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.9f, .75f, .3f, .8f));

        var clicked = plugin.TryGetIcon(iconId, out var wrap) && wrap != null
            ? ImGui.ImageButton(wrap.Handle, new Vector2(64f))
            : ImGui.Button("##empty", new Vector2(64f));

        if (selected) ImGui.PopStyleColor();
        if (clicked)
        {
            editingComplete = complete;
            browseStart = (int)(iconId - iconId % PageSpan);
        }

        ImGui.TextUnformatted(label);
        ImGui.PopID();
        ImGui.EndGroup();
    }
}
