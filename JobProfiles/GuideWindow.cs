using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Shortcuts;

namespace JobProfiles;

// Which roles and jobs use one mount or minion, opened from the game's guides. A box shows what a page uses now;
// changing one that follows the page above gives it its own list, starting from what it inherited.
internal sealed class GuideWindow : Window
{
    private static readonly string Combat = $"g:{JobGroup.Combat}";

    private readonly Action save;
    private readonly JobGroups groups;
    private readonly IPlayerState player;
    private readonly ShortcutCatalog catalog;
    private readonly IconCache icons;
    private readonly ProfilePages pages;

    // Each page's own list, as opened and as ticked; null follows the page above.
    private readonly Dictionary<string, List<uint>?> opened = new();
    private readonly Dictionary<string, List<uint>?> draft = new();
    private Dictionary<string, string>? jobLabels;
    private ShortcutKind kind;
    private uint id;
    private string name = string.Empty;
    private uint iconId;
    private string? currentJob;
    private bool unsaved;

    public GuideWindow(Configuration config, Action save, JobGroups groups, IPlayerState player, ShortcutCatalog catalog,
                       IconCache icons)
        : base("Job Profiles###JobProfilesGuide", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.save = save;
        this.groups = groups;
        this.player = player;
        this.catalog = catalog;
        this.icons = icons;
        pages = new ProfilePages(config);
    }

    public void Open(ShortcutKind kind, uint id)
    {
        this.kind = kind;
        this.id = id;
        var info = catalog.Describe(new Step { Kind = kind, RowId = id });
        name = info?.Name ?? "(no longer in the game)";
        iconId = info?.IconId ?? 0;
        currentJob = player.IsLoaded ? groups.Of(player.ClassJob.RowId)?.Abbreviation : null;
        jobLabels ??= JobGroups.All.SelectMany(g => g.Jobs)
                               .ToDictionary(j => j, j => groups.Job(j) is { } row ? row.Abbreviation.ExtractText() : j);

        opened.Clear();
        draft.Clear();
        foreach (var who in AllPages())
        {
            var own = pages.ProfileOf(who) is { } profile ? Get(profile) : null;
            opened[who] = own;
            draft[who] = own is null ? null : [.. own];
        }
        unsaved = false;

        IsOpen = true;
        BringToFront();
    }

    public override void Draw()
    {
        var style = ImGui.GetStyle();
        icons.Get(iconId, false).Image(new Vector2(ImGui.GetTextLineHeight() * 2 + style.ItemSpacing.Y));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(name);
        ImGui.TextDisabled("Tick the roles and jobs that use it. A faint box follows the page above.");
        ImGui.EndGroup();
        ImGui.Spacing();

        var jobsX = ImGui.GetCursorPosX() + ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + style.ItemSpacing.X * 3 +
                    JobGroups.All.Max(g => ImGui.CalcTextSize(g.Label).X + (JobGroups.Parent(g.Group) != null ? style.IndentSpacing : 0));
        PageBox(Combat, JobGroups.CombatLabel);
        foreach (var (group, label, jobs) in JobGroups.All)
        {
            var indented = JobGroups.Parent(group) != null;
            if (indented)
                ImGui.Indent();
            PageBox($"g:{group}", label);
            ImGui.SameLine(jobsX);
            for (var i = 0; i < jobs.Length; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                PageBox($"j:{jobs[i]}", jobLabels![jobs[i]]);
            }
            if (indented)
                ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.BeginDisabled(!unsaved);
        if (ImGui.Button("Save"))
            Save();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Y on a controller.");
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            IsOpen = false;

        if (unsaved && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
            ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false))
            Save();
    }

    private void PageBox(string who, string label)
    {
        var parent = ProfilePages.ParentOf(who);
        var follows = parent != null && draft[who] == null;
        var ticked = Effective(who)?.Contains(id) == true;
        var job = ButtonsTab.ParseWho(who).Job;
        var current = job != null && job == currentJob;

        if (follows)
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f);
        if (current)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.CheckMark));
        if (ImGui.Checkbox($"{label}##{who}", ref ticked))
            Toggle(who, ticked);
        if (current)
            ImGui.PopStyleColor();
        if (follows)
            ImGui.PopStyleVar();

        if (!ImGui.IsItemHovered())
            return;
        var lines = new List<string>();
        if (job != null && groups.Job(job) is { } row)
            lines.Add(JobGroups.DisplayName(row));
        if (current)
            lines.Add("Your current job");
        if (follows && ButtonsTab.ParseWho(parent!).Group is { } group)
            lines.Add($"Follows {JobGroups.Label(group)}; changing it gives it a list of its own.");
        if (lines.Count > 0)
            ImGui.SetTooltip(string.Join("\n", lines));
    }

    // A page that followed goes back to following when it ends up with what it inherits.
    private void Toggle(string who, bool tick)
    {
        List<uint> ids = Effective(who) is { } effective ? [.. effective] : [];
        ids.Remove(id);
        if (tick)
            ids.Add(id);
        var inherited = ProfilePages.ParentOf(who) is { } parent ? Effective(parent) : null;
        draft[who] = ids.Count == 0 || (opened[who] == null && SameIds(ids, inherited)) ? null : ids;
        unsaved = draft.Keys.Any(Changed);
    }

    private void Save()
    {
        foreach (var (who, ids) in draft)
        {
            if (Changed(who))
                Set(pages.Create(who), ids);
        }
        save();
        IsOpen = false;
    }

    private bool Changed(string who) => !SameIds(draft[who], opened[who]);

    private static bool SameIds(List<uint>? a, List<uint>? b) => (a ?? []).ToHashSet().SetEquals(b ?? []);

    private List<uint>? Effective(string who) =>
        draft[who] ?? (ProfilePages.ParentOf(who) is { } parent ? Effective(parent) : null);

    private List<uint>? Get(Profile profile) => kind == ShortcutKind.Mount ? profile.Mounts : profile.Minions;

    private void Set(Profile profile, List<uint>? ids)
    {
        if (kind == ShortcutKind.Mount)
            profile.Mounts = ids;
        else
            profile.Minions = ids;
    }

    private static IEnumerable<string> AllPages() =>
        JobGroups.All.SelectMany(g => g.Jobs.Select(j => $"j:{j}").Prepend($"g:{g.Group}")).Prepend(Combat);
}
