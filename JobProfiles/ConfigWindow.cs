using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace JobProfiles;

internal sealed class ConfigWindow : Window
{
    private const string CustomisePopup = "##customise";

    private string LayoutLabel(int layout) =>
        config.HudLayoutNames.ElementAtOrDefault(layout)?.Trim() is { Length: > 0 } name
            ? $"{layout + 1} · {name}"
            : $"Layout {layout + 1}";

    private IEnumerable<(int? Value, string Label)> LayoutOptions() =>
        Enumerable.Range(0, Actions.HudLayouts).Select(i => ((int?)i, LayoutLabel(i)));

    private readonly Configuration config;
    private readonly Action save;
    private readonly JobGroups groups;
    private readonly IPlayerState player;
    private readonly ITextureProvider textures;
    private readonly Action applyNow;
    private readonly Widgets widgets;
    private readonly ButtonsTab buttons;

    private string page = $"g:{JobGroup.Tank}";
    private string customiseSearch = string.Empty;

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
        Size = new Vector2(760, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose() => buttons.StopRecording();

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("tabs"))
            return;

        if (ImGui.BeginTabItem("Jobs"))
        {
            if (DrawJobs())
                save();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Buttons"))
        {
            if (buttons.DrawButtons())
                save();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();

        if (widgets.DrawPicker())
            save();
    }

    private bool DrawJobs()
    {
        var changed = false;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Switch HUD layout and run actions when my job changes", ref enabled))
        {
            config.Enabled = enabled;
            changed = true;
        }

        var onLogin = config.ApplyOnLogin;
        if (ImGui.Checkbox("Also when I log in", ref onLogin))
        {
            config.ApplyOnLogin = onLogin;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Apply for my current job now"))
            applyNow();

        if (ImGui.CollapsingHeader("HUD layout names"))
        {
            if (config.HudLayoutNames.Length < Actions.HudLayouts)
                config.HudLayoutNames = [.. config.HudLayoutNames, .. Enumerable.Repeat(string.Empty, Actions.HudLayouts - config.HudLayoutNames.Length)];
            for (var i = 0; i < Actions.HudLayouts; i++)
            {
                var name = config.HudLayoutNames[i];
                ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
                if (ImGui.InputTextWithHint($"Layout {i + 1}##layoutName{i}", "e.g. Healer, Crafting…", ref name, 32))
                {
                    config.HudLayoutNames[i] = name;
                    changed = true;
                }
            }
            ImGui.TextDisabled("Only used here; the game's own HUD Layout window still shows numbers.");
        }

        ImGui.Spacing();
        var listWidth = 190f * ImGuiHelpers.GlobalScale;
        if (ImGui.BeginChild("##pages", new Vector2(listWidth, 0), true))
            changed |= DrawPageList();
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##page", Vector2.Zero, true))
            changed |= DrawPage();
        ImGui.EndChild();

        return changed;
    }

    private bool DrawPageList()
    {
        var changed = false;
        if (ImGui.Selectable("Everyone", page == string.Empty))
            page = string.Empty;

        ImGui.Spacing();
        foreach (var (group, label, jobs) in JobGroups.All)
        {
            if (ImGui.Selectable($"{label}##{group}", page == $"g:{group}"))
                page = $"g:{group}";

            foreach (var abbreviation in jobs.Where(IsCustomised))
            {
                ImGui.Indent();
                if (ImGui.Selectable($"{JobLabel(abbreviation)}##{abbreviation}", page == $"j:{abbreviation}"))
                    page = $"j:{abbreviation}";
                ImGui.Unindent();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.SmallButton("+ Customise a job…"))
        {
            customiseSearch = string.Empty;
            ImGui.OpenPopup(CustomisePopup);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A job follows its role unless you customise it: then it gets its own page.");

        if (ImGui.BeginPopup(CustomisePopup))
        {
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
            ImGui.InputTextWithHint("##search", "Search", ref customiseSearch, 32);
            foreach (var (_, _, jobs) in JobGroups.All)
            {
                foreach (var abbreviation in jobs.Where(a => !IsCustomised(a)))
                {
                    var name = JobLabel(abbreviation);
                    if (customiseSearch.Length > 0 && !name.Contains(customiseSearch, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    if (ImGui.Selectable(name))
                    {
                        config.Jobs[abbreviation] = new Profile();
                        page = $"j:{abbreviation}";
                        changed = true;
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            ImGui.EndPopup();
        }

        return changed;
    }

    private bool DrawPage()
    {
        if (page.Length == 0)
            return DrawEveryone();
        var (job, group) = ButtonsTab.ParseWho(page);
        if (job != null)
            return DrawJob(job);
        if (group is { } role)
            return DrawRole(role);
        return false;
    }

    private bool DrawEveryone()
    {
        Heading("Everyone");
        ImGui.TextWrapped("Button behaviour here is for every job, unless a role or job page sets the same button for itself.");
        ImGui.Spacing();
        Section("Buttons");
        return buttons.DrawBehaviour(string.Empty);
    }

    private bool DrawRole(JobGroup group)
    {
        var changed = false;
        var (_, label, jobs) = JobGroups.All.First(g => g.Group == group);
        Heading(label);
        ImGui.TextDisabled(string.Join(" ", jobs.Select(JobShort)));

        config.Groups.TryGetValue(group, out var profile);

        ImGui.Spacing();
        Section("When you switch to one of its jobs");
        var hud = profile?.HudLayout;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("HUD layout");
        ImGui.SameLine();
        var options = new List<(int? Value, string Label)> { (null, "Don't change") };
        options.AddRange(LayoutOptions());
        if (widgets.Combo("##hud", ref hud, options, width: 140f))
        {
            RoleProfile(group).HudLayout = hud;
            changed = true;
        }

        var steps = profile?.Steps ?? [];
        changed |= widgets.StepList(steps, label, step => (RoleProfile(group).Steps ??= []).Add(step));
        if (profile is { Steps.Count: 0 })
            profile.Steps = null;

        ImGui.Spacing();
        Section("Buttons");
        changed |= buttons.DrawBehaviour($"g:{group}");
        return changed;
    }

    private bool DrawJob(string abbreviation)
    {
        var changed = false;
        var group = JobGroups.All.First(g => g.Jobs.Contains(abbreviation));
        Heading(JobLabel(abbreviation), abbreviation);
        ImGui.TextDisabled($"Customised from {group.Label}. Anything left on \"Same as {group.Label}\" follows the role.");

        if (!config.Jobs.TryGetValue(abbreviation, out var profile))
            config.Jobs[abbreviation] = profile = new Profile();
        config.Groups.TryGetValue(group.Group, out var role);

        ImGui.Spacing();
        Section("When you switch to it");
        var hud = profile.HudLayout;
        var roleLayout = role?.HudLayout is { } layout ? LayoutLabel(layout) : "Don't change";
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("HUD layout");
        ImGui.SameLine();
        var options = new List<(int? Value, string Label)>
        {
            (null, $"Same as {group.Label} ({roleLayout})"),
            (Profile.NoHudLayout, "Don't change"),
        };
        options.AddRange(LayoutOptions());
        if (widgets.Combo("##hud", ref hud, options, width: 140f))
        {
            profile.HudLayout = hud;
            changed = true;
        }

        var useRole = profile.Steps == null;
        if (ImGui.Checkbox($"Same as {group.Label}", ref useRole))
        {
            profile.Steps = useRole ? null : (role?.Steps ?? []).Select(s => s.Copy()).ToList();
            changed = true;
        }
        if (profile.Steps == null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(widgets.StepNames(role?.Steps));
        }
        else
        {
            changed |= widgets.StepList(profile.Steps, JobLabel(abbreviation), step => (profile.Steps ??= []).Add(step));
        }

        ImGui.Spacing();
        Section("Buttons");
        changed |= buttons.DrawBehaviour($"j:{abbreviation}");

        ImGui.Spacing();
        ImGui.Separator();
        var hasRules = config.Bindings.Any(b => b.Rules.Any(r => r.Job == abbreviation));
        var canStop = (profile.HudLayout == null && profile.Steps == null && !hasRules) || ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!canStop);
        if (ImGui.SmallButton("Stop customising"))
        {
            config.Jobs.Remove(abbreviation);
            foreach (var binding in config.Bindings)
                binding.Rules.RemoveAll(r => r.Job == abbreviation);
            page = $"g:{group.Group}";
            changed = true;
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(canStop
                ? $"Remove this page; the job follows {group.Label} again."
                : $"Hold Ctrl: this removes the job's own settings and button behaviour, and it follows {group.Label} again.");

        return changed;
    }

    private Profile RoleProfile(JobGroup group)
    {
        if (!config.Groups.TryGetValue(group, out var profile))
            config.Groups[group] = profile = new Profile();
        return profile;
    }

    private bool IsCustomised(string abbreviation) =>
        config.Jobs.ContainsKey(abbreviation) || config.Bindings.Any(b => b.Rules.Any(r => r.Job == abbreviation));

    private string JobShort(string abbreviation) =>
        groups.Job(abbreviation) is { } job ? job.Abbreviation.ExtractText() : abbreviation;

    private string JobLabel(string abbreviation) =>
        groups.Job(abbreviation) is { } job ? $"{job.Abbreviation.ExtractText()} – {JobGroups.DisplayName(job)}" : abbreviation;

    private void Heading(string title, string? abbreviation = null)
    {
        if (abbreviation != null && groups.Job(abbreviation) is { } job)
        {
            var unlocked = !player.IsLoaded || player.GetClassJobLevel(job) > 0;
            var icon = textures.GetFromGameIcon(new GameIconLookup(JobGroups.JobIconBase + job.RowId)).GetWrapOrEmpty();
            ImGui.Image(icon.Handle, new Vector2(ImGui.GetFrameHeight()), Vector2.Zero, Vector2.One,
                        new Vector4(1f, 1f, 1f, unlocked ? 1f : 0.4f));
            ImGui.SameLine();
            if (!unlocked)
                title += " (locked)";
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(title);
        ImGui.Separator();
    }

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(title);
    }
}
