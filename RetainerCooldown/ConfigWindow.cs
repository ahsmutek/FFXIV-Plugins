using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace RetainerCooldown;

internal sealed class ConfigWindow : Window
{
    private static readonly string[] DisplayModeLabels = ["Fastest to complete", "Latest to complete"];
    private static readonly string[] LengthLabels =
        Array.ConvertAll(Configuration.VentureLengthPresets, preset => preset.Label);

    private readonly Plugin plugin;
    private bool dirty;
    private bool editingComplete;
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

        if (plugin.IconPicker.Available)
        {
            ImGui.TextDisabled("Click an icon to choose another in Icon Atlas.");
            return changed;
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
        ImGui.TextDisabled("Load Icon Atlas to choose from every icon.");
        return changed;
    }

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
            if (plugin.IconPicker.Available)
                plugin.IconPicker.Open(complete ? "Retainer Cooldown, all complete" : "Retainer Cooldown, in progress",
                                       iconId, picked =>
                                       {
                                           if (complete) plugin.Config.CompleteIconId = picked;
                                           else plugin.Config.IconId = picked;
                                           dirty = true;
                                       });
        }

        ImGui.TextUnformatted(label);
        ImGui.PopID();
        ImGui.EndGroup();
    }
}
