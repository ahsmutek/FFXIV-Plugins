using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace JobProfiles;

// The Job change tab (each role group's HUD layout and macros, with per-job overrides)
// and the Buttons tab.
internal sealed class ConfigWindow : Window
{
    private static readonly string[] Layouts =
        Enumerable.Range(1, Actions.HudLayouts).Select(n => $"Layout {n}").ToArray();

    private readonly Configuration config;
    private readonly Action save;
    private readonly JobGroups groups;
    private readonly IPlayerState player;
    private readonly ITextureProvider textures;
    private readonly Action applyNow;

    private readonly Widgets widgets;
    private readonly ButtonsTab buttons;

    public ConfigWindow(Configuration config, Action save, JobGroups groups, IPlayerState player,
                        ITextureProvider textures, Action applyNow, Widgets widgets, ButtonsTab buttons)
        : base("Job Profiles###JobProfilesConfig")
    {
        this.config = config;
        this.save = save;
        this.groups = groups;
        this.player = player;
        this.textures = textures;
        this.applyNow = applyNow;
        this.widgets = widgets;
        this.buttons = buttons;
        Size = new Vector2(620, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }


    public override void OnClose() => buttons.StopRecording();

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("tabs"))
            return;

        if (ImGui.BeginTabItem("Job change"))
        {
            DrawJobChange();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Buttons"))
        {
            if (buttons.Draw())
                save();
            ImGui.EndTabItem();
        }
        else
        {
            buttons.StopRecording();
        }

        ImGui.EndTabBar();

        // The add-a-step picker, opened by any list's Add.
        if (widgets.DrawPicker())
            save();
    }

    // Each role group's HUD layout and macros, and under it its jobs, each of which can
    // override the group.
    private void DrawJobChange()
    {
        var changed = false;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Apply profiles when my job changes", ref enabled))
        {
            config.Enabled = enabled;
            changed = true;
        }

        var onLogin = config.ApplyOnLogin;
        if (ImGui.Checkbox("Also apply after logging in", ref onLogin))
        {
            config.ApplyOnLogin = onLogin;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Apply now"))
            applyNow();

        ImGui.TextWrapped("Pick a HUD layout and what to run for each group: macros, menus, actions, items or any chat command. Tick Override on a job to give it its own. A class uses its job's profile (Gladiator uses Paladin's).");
        ImGui.Spacing();

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("profiles", 4, flags))
        {
            ImGui.TableSetupColumn("Group / job", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Override", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("HUD layout", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Run", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableHeadersRow();

            foreach (var (group, label, jobs) in JobGroups.All)
            {
                changed |= DrawGroupRow(group, label);
                foreach (var abbreviation in jobs)
                    changed |= DrawJobRow(group, abbreviation);
            }

            ImGui.EndTable();
        }

        if (changed)
            save();
    }

    private bool DrawGroupRow(JobGroup group, string label)
    {
        var changed = false;
        ImGui.PushID(group.ToString());
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();

        config.Groups.TryGetValue(group, out var profile);
        var hud = profile?.HudLayout;
        var list = profile?.Steps is { } saved ? saved.Select(s => s.Copy()).ToList() : [];

        ImGui.TableNextColumn();
        if (HudCombo(ref hud, forJob: false))
            changed = true;
        ImGui.TableNextColumn();
        if (widgets.StepList(list, label, step => AddToGroup(group, step)))
            changed = true;

        if (changed)
        {
            if (hud == null && list.Count == 0)
                config.Groups.Remove(group);
            else
                config.Groups[group] = new Profile { HudLayout = hud, Steps = list.Count > 0 ? list : null };
        }

        ImGui.PopID();
        return changed;
    }

    private bool DrawJobRow(JobGroup group, string abbreviation)
    {
        if (groups.Job(abbreviation) is not { } job)
            return false;

        var changed = false;
        ImGui.PushID(abbreviation);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        // Jobs you haven't unlocked can still be set up, but are dimmed.
        var unlocked = !player.IsLoaded || player.GetClassJobLevel(job) > 0;
        var size = new Vector2(ImGui.GetFrameHeight());
        ImGui.Indent();
        var icon = textures.GetFromGameIcon(new GameIconLookup(JobGroups.JobIconBase + job.RowId)).GetWrapOrEmpty();
        ImGui.Image(icon.Handle, size, Vector2.Zero, Vector2.One, new Vector4(1f, 1f, 1f, unlocked ? 1f : 0.4f));
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        var name = job.Abbreviation.ExtractText();
        if (unlocked)
            ImGui.TextUnformatted(name);
        else
            ImGui.TextDisabled(name);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(unlocked ? JobGroups.DisplayName(job) : $"{JobGroups.DisplayName(job)} (locked)");
        ImGui.Unindent();

        config.Groups.TryGetValue(group, out var fromGroup);
        ImGui.TableNextColumn();
        var overridden = config.Jobs.TryGetValue(abbreviation, out var profile);
        if (ImGui.Checkbox("##override", ref overridden))
        {
            if (overridden)
                config.Jobs[abbreviation] = new Profile();
            else
                config.Jobs.Remove(abbreviation);
            changed = true;
        }

        if (overridden && config.Jobs.TryGetValue(abbreviation, out profile))
        {
            var hud = profile.HudLayout;
            ImGui.TableNextColumn();
            if (HudCombo(ref hud, forJob: true))
            {
                profile.HudLayout = hud;
                changed = true;
            }

            // Ticked: the group's macros. Unticked: this job's own list, starting as a copy
            // of the group's; an empty list runs none.
            ImGui.TableNextColumn();
            var useGroup = profile.Steps == null;
            if (ImGui.Checkbox("Use group's list", ref useGroup))
            {
                profile.Steps = useGroup ? null : (fromGroup?.Steps ?? []).Select(s => s.Copy()).ToList();
                changed = true;
            }
            if (profile.Steps == null)
                ImGui.TextDisabled(widgets.StepNames(fromGroup?.Steps));
            else
                changed |= widgets.StepList(profile.Steps, JobGroups.DisplayName(job), step => (profile.Steps ??= []).Add(step));
        }
        else
        {
            // Show what the group gives this job.
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(fromGroup?.HudLayout is { } layout ? Layouts[layout] : "—");
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(widgets.StepNames(fromGroup?.Steps));
        }

        ImGui.PopID();
        return changed;
    }

    // A step picked for a group goes into its saved profile, made if it has none yet.
    private void AddToGroup(JobGroup group, Step step)
    {
        if (!config.Groups.TryGetValue(group, out var profile))
            config.Groups[group] = profile = new Profile();
        (profile.Steps ??= []).Add(step);
    }

    // Groups: Don't change, Layout 1–4. Jobs also get Use group first.
    private bool HudCombo(ref int? value, bool forJob)
    {
        var options = new List<(int? Value, string Label)>();
        if (forJob)
            options.Add((null, "Use group"));
        options.Add((forJob ? Profile.NoHudLayout : null, "Don't change"));
        for (var i = 0; i < Layouts.Length; i++)
            options.Add((i, Layouts[i]));

        return widgets.Combo("##hud", ref value, options);
    }
}
