using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace JobProfiles;

// One table: each role group's HUD layout and macro, and under it its jobs, each of
// which can override the group.
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

    // Refreshed when the window opens so new and renamed macros show up.
    private List<MacroChoice> macros = [];
    // The open macro list's search text; only one list is open at a time.
    private string search = string.Empty;

    public ConfigWindow(Configuration config, Action save, JobGroups groups, IPlayerState player,
                        ITextureProvider textures, Action applyNow)
        : base("Job Profiles###JobProfilesConfig")
    {
        this.config = config;
        this.save = save;
        this.groups = groups;
        this.player = player;
        this.textures = textures;
        this.applyNow = applyNow;
        Size = new Vector2(620, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen() => macros = Actions.Macros();

    public override void Draw()
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

        ImGui.TextWrapped("Pick a HUD layout and/or a macro for each group. Tick Override on a job to give it its own. A class uses its job's profile (Gladiator uses Paladin's).");
        ImGui.Spacing();

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("profiles", 4, flags))
        {
            ImGui.TableSetupColumn("Group / job", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Override", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("HUD layout", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Macro", ImGuiTableColumnFlags.WidthStretch, 2f);
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
        var macro = profile?.MacroId;

        ImGui.TableNextColumn();
        if (HudCombo(ref hud, forJob: false))
            changed = true;
        ImGui.TableNextColumn();
        if (MacroCombo(ref macro, forJob: false))
            changed = true;

        if (changed)
        {
            if (hud == null && macro == null)
                config.Groups.Remove(group);
            else
                config.Groups[group] = new Profile { HudLayout = hud, MacroId = macro };
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
            var macro = profile.MacroId;
            ImGui.TableNextColumn();
            if (HudCombo(ref hud, forJob: true))
            {
                profile.HudLayout = hud;
                changed = true;
            }
            ImGui.TableNextColumn();
            if (MacroCombo(ref macro, forJob: true))
            {
                profile.MacroId = macro;
                changed = true;
            }
        }
        else
        {
            // Show what the group gives this job.
            config.Groups.TryGetValue(group, out var fromGroup);
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(fromGroup?.HudLayout is { } layout ? Layouts[layout] : "—");
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(fromGroup?.MacroId is { } id ? Actions.MacroName(id) : "—");
        }

        ImGui.PopID();
        return changed;
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

        return Combo("##hud", ref value, options);
    }

    // Groups: None, then your macros. Jobs also get Use group first. A picked macro whose
    // slot has since been cleared stays listed so the choice isn't silently lost.
    private bool MacroCombo(ref uint? value, bool forJob)
    {
        var options = new List<(uint? Value, string Label)>();
        if (forJob)
            options.Add((null, "Use group"));
        options.Add((forJob ? Profile.NoMacro : null, "None"));
        options.AddRange(macros.Select(m => ((uint?)m.Id, m.Name)));
        if (value is { } id && id != Profile.NoMacro && macros.All(m => m.Id != id))
            options.Add((id, $"{Actions.MacroName(id)} (empty)"));

        return Combo("##macro", ref value, options, searchable: true);
    }

    private bool Combo<T>(string id, ref T value, List<(T Value, string Label)> options, bool searchable = false)
    {
        var current = value;
        var selected = Math.Max(0, options.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, current)));
        var changed = false;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo(id, options[selected].Label, searchable ? ImGuiComboFlags.HeightLarge : ImGuiComboFlags.None))
        {
            // A search box at the top, empty and focused each time the list opens.
            if (searchable)
            {
                if (ImGui.IsWindowAppearing())
                {
                    search = string.Empty;
                    ImGui.SetKeyboardFocusHere();
                }
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##search", "Search", ref search, 64);
            }

            for (var i = 0; i < options.Count; i++)
            {
                if (search.Length > 0 && searchable &&
                    !options[i].Label.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                    continue;
                if (ImGui.Selectable($"{options[i].Label}##{i}", i == selected) && i != selected)
                {
                    value = options[i].Value;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        return changed;
    }
}
