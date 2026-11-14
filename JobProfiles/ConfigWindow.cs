using System;
using System.Numerics;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace JobProfiles;

internal sealed class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly Action save;
    private readonly Action applyNow;
    private readonly Widgets widgets;
    private readonly ButtonsTab buttons;
    private readonly JobsTab jobs;
    private readonly PadTabs tabs = new();

    public ConfigWindow(Configuration config, Action save, JobGroups groups, IPlayerState player,
                        ITextureProvider textures, Action applyNow, Widgets widgets, ButtonsTab buttons)
        : base("Job Profiles###JobProfilesConfig")
    {
        this.config = config;
        this.save = save;
        this.applyNow = applyNow;
        this.widgets = widgets;
        this.buttons = buttons;
        jobs = new JobsTab(config, groups, player, textures, widgets, buttons);
        Size = new Vector2(820, 660);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose() => buttons.StopRecording();

    public override void Draw()
    {
        if (buttons.Record())
            save();

        if (!ImGui.BeginTabBar("tabs"))
            return;

        if (tabs.Item("Jobs"))
        {
            if (jobs.Draw())
                save();
            ImGui.EndTabItem();
        }

        if (tabs.Item("Buttons"))
        {
            if (buttons.DrawButtons())
                save();
            ImGui.EndTabItem();
        }

        if (tabs.Item("Settings"))
        {
            if (DrawSettings())
                save();
            ImGui.EndTabItem();
        }

        tabs.End();
        ImGui.EndTabBar();

        if (widgets.DrawPicker())
            save();
    }

    private bool DrawSettings()
    {
        ImGui.Spacing();
        var changed = Toggle("##enabled", "Auto-apply on job change",
                             "Switches your HUD layout, mount and minion whenever your job changes, however you change it.",
                             config.Enabled, on => config.Enabled = on);
        changed |= Toggle("##login", "Apply on login",
                          "A few seconds after you log in, switches to your job's HUD layout and summons one of its minions.",
                          config.ApplyOnLogin, on => config.ApplyOnLogin = on);
        if (ImGui.Button("Apply for my current job now"))
            applyNow();
        ImGui.SameLine();
        ImGui.TextDisabled("or /jobprofiles apply");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        changed |= Toggle("##buttons", "Use controller buttons and keys",
                          "Turns everything set up on the Buttons tab on or off.",
                          config.BindingsEnabled, on => config.BindingsEnabled = on);
        return changed;
    }

    private static bool Toggle(string id, string label, string description, bool value, Action<bool> set)
    {
        var changed = ImGuiComponents.ToggleButton(id, ref value);
        if (changed)
            set(value);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.PushTextWrapPos(0);
        ImGui.TextDisabled(description);
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.Spacing();
        return changed;
    }
}
