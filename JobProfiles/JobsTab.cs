using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Shortcuts;

namespace JobProfiles;

// Pages are keyed like button lines: "" is All jobs, "g:Tank" a role, "j:WAR" a job.
internal sealed class JobsTab(Configuration config, JobGroups groups, IPlayerState player, ITextureProvider textures,
                              Widgets widgets, ButtonsTab buttons)
{
    private const string All = "";
    private const string CustomisePopup = "##customise";
    private const string CopyPopup = "##copy";
    private const string StopPopup = "##stop";
    private const string NamesPopup = "##layoutNames";
    private const string Inherit = "Inherit";
    private const string Customize = "Customize";

    private sealed record Collection(string Id, string Label, ShortcutKind Kind, FontAwesomeIcon Icon, string Tooltip, string Unit,
                                     Func<Profile, List<uint>?> Get, Action<Profile, List<uint>?> Set);

    private static readonly Collection[] Collections =
    [
        new("mounts", "Mounts", ShortcutKind.Mount, FontAwesomeIcon.Horse, "Mount Roulette rides one of these, drawn at each job change",
            "mount", p => p.Mounts, (p, ids) => p.Mounts = ids),
        new("minions", "Minions", ShortcutKind.Minion, FontAwesomeIcon.Paw, "One is summoned when you switch, never the one already out",
            "minion", p => p.Minions, (p, ids) => p.Minions = ids),
    ];

    // The game's framed role icons; Limited Jobs uses Blue Mage's job icon.
    private static readonly Dictionary<JobGroup, uint> RoleIcons = new()
    {
        [JobGroup.Combat] = 62576, [JobGroup.Tank] = 62581, [JobGroup.Healer] = 62582, [JobGroup.Melee] = 62584,
        [JobGroup.PhysicalRanged] = 62586, [JobGroup.Caster] = 62587, [JobGroup.Hand] = 62588, [JobGroup.Land] = 62589,
    };

    private readonly ProfilePages pages = new(config);
    private string page = $"g:{JobGroup.Tank}";
    private string customiseSearch = string.Empty;
    private readonly HashSet<string> collapsedCards = ["buttons"];
    private readonly HashSet<string> foldedNodes = [];
    private string? copyFrom;
    private bool copyHud = true;
    private bool copyMounts = true;
    private bool copyMinions = true;
    private bool copyButtons;

    public bool Draw()
    {
        var changed = false;
        ImGui.Spacing();

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (ImGui.BeginChild("##pages", new Vector2(210f * ImGuiHelpers.GlobalScale, 0), false, ImGuiWindowFlags.NavFlattened))
            changed |= DrawSidebar();
        ImGui.EndChild();
        var divider = ImGui.GetItemRectMax().X + spacing;
        ImGui.GetWindowDrawList().AddLine(new Vector2(divider, ImGui.GetItemRectMin().Y), new Vector2(divider, ImGui.GetItemRectMax().Y),
                                          ImGui.GetColorU32(ImGuiCol.Separator));

        ImGui.SameLine(0, spacing * 2);
        if (ImGui.BeginChild("##page", Vector2.Zero, false, ImGuiWindowFlags.NavFlattened))
            changed |= DrawPage();
        ImGui.EndChild();
        return changed;
    }

    private bool DrawSidebar()
    {
        PageNode(All, false);
        ImGui.Spacing();
        if (PageNode($"g:{JobGroup.Combat}", true))
        {
            foreach (var (group, _, jobs) in JobGroups.All.Where(g => JobGroups.Parent(g.Group) != null))
                RoleNode(group, jobs);
            ImGui.TreePop();
        }
        foreach (var (group, _, jobs) in JobGroups.All.Where(g => JobGroups.Parent(g.Group) == null))
            RoleNode(group, jobs);

        var bottom = ImGui.GetWindowHeight() - ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y;
        ImGui.SetCursorPosY(MathF.Max(ImGui.GetCursorPosY() + ImGui.GetStyle().ItemSpacing.Y, bottom));
        if (ImGui.Button("+ Customise a job", new Vector2(-1, 0)))
        {
            customiseSearch = string.Empty;
            ImGui.OpenPopup(CustomisePopup);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A job follows its role unless you customise it: then it gets its own page.");
        return DrawCustomisePopup();
    }

    private void RoleNode(JobGroup group, string[] jobs)
    {
        var customised = jobs.Where(IsCustomised).ToList();
        if (!PageNode($"g:{group}", customised.Count > 0))
            return;
        foreach (var abbreviation in customised)
            PageNode($"j:{abbreviation}", false);
        ImGui.TreePop();
    }

    // True when its children are to be drawn, followed by TreePop. A on the pad opens the page without folding it.
    private bool PageNode(string key, bool hasChildren)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth |
                    ImGuiTreeNodeFlags.FramePadding;
        if (!hasChildren)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (page == key)
            flags |= ImGuiTreeNodeFlags.Selected;

        if (hasChildren)
            ImGui.SetNextItemOpen(!foldedNodes.Contains(key));
        var open = ImGui.TreeNodeEx($"##{key}", flags);
        var mouse = ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        if (ImGui.IsItemToggledOpen())
        {
            if (ImGui.IsItemActivated() && !mouse)
                page = key;
            else if (open)
                foldedNodes.Remove(key);
            else
                foldedNodes.Add(key);
        }
        else if (ImGui.IsItemActivated())
        {
            page = key;
        }

        ImGui.SameLine();
        PageIcon(key, ImGui.GetFrameHeight());
        ImGui.SameLine();
        ImGui.TextUnformatted(TitleOf(key));
        return hasChildren && open;
    }

    private bool DrawCustomisePopup()
    {
        if (!ImGui.BeginPopup(CustomisePopup))
            return false;

        var changed = false;
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##search", "Search", ref customiseSearch, 32);
        foreach (var abbreviation in JobGroups.All.SelectMany(g => g.Jobs).Where(a => !IsCustomised(a)))
        {
            var name = JobLabel(abbreviation);
            if (customiseSearch.Length > 0 && !name.Contains(customiseSearch, StringComparison.CurrentCultureIgnoreCase))
                continue;
            if (!ImGui.Selectable(name))
                continue;
            config.Jobs[abbreviation] = new Profile();
            page = $"j:{abbreviation}";
            changed = true;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
        return changed;
    }

    private bool DrawPage()
    {
        var changed = DrawHeader();
        if (page == All)
            return changed | DrawButtonsCard();

        changed |= DrawHudCard();
        foreach (var collection in Collections)
            changed |= DrawCollectionCard(collection);
        changed |= DrawButtonsCard();
        return changed;
    }

    private bool DrawHeader()
    {
        var changed = false;
        var style = ImGui.GetStyle();
        var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        var job = ButtonsTab.ParseWho(page).Job;

        PageIcon(page, ImGui.GetTextLineHeight() * 2 + style.ItemSpacing.Y);
        ImGui.SameLine();
        ImGui.BeginGroup();
        var title = TitleOf(page);
        if (job != null && groups.Job(job) is { } row && player.IsLoaded && player.GetClassJobLevel(row) == 0)
            title += " (locked)";
        ImGui.TextUnformatted(title);
        ImGui.TextDisabled(SubtitleOf(page));
        ImGui.EndGroup();

        var buttonsWidth = Cards.IconWidth(FontAwesomeIcon.Copy) + style.FramePadding.X * 2;
        if (job != null)
            buttonsWidth += style.ItemSpacing.X + Cards.IconWidth(FontAwesomeIcon.Trash) + style.FramePadding.X * 2;
        ImGui.SameLine(right - buttonsWidth);
        if (ImGuiComponents.IconButton("copy", FontAwesomeIcon.Copy))
        {
            copyFrom = null;
            ImGui.OpenPopup(CopyPopup);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy settings from another page");
        changed |= DrawCopyPopup();

        if (job != null)
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton("stop", FontAwesomeIcon.Trash))
                ImGui.OpenPopup(StopPopup);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stop customising");
            changed |= DrawStopPopup(job);
        }

        var margin = new Vector2(0, style.ItemSpacing.Y * 2);
        ImGui.Dummy(margin);
        if (ProfilePages.ParentOf(page) is { } parent)
            DrawInheritance(parent);
        else if (page == All)
            ImGui.TextDisabled("Button behaviour here is for every job, unless another page sets the same button for itself.");
        else if (page == $"g:{JobGroup.Combat}")
            ImGui.TextDisabled("Its roles and their jobs follow it unless they set their own.");
        ImGui.Dummy(margin);

        ImGui.Separator();
        ImGui.Spacing();
        return changed;
    }

    private void DrawInheritance(string parent)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Inherits from");
        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        // A frameless button reads as a link and can still be reached with the D-pad.
        ImGui.PushStyleColor(ImGuiCol.Button, 0u);
        if (ImGui.Button(TitleOf(parent)))
            page = parent;
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Go to {TitleOf(parent)}");

        var profile = pages.ProfileOf(page);
        var overrides = new List<string>();
        if (profile?.HudLayout != null)
            overrides.Add("HUD layout");
        overrides.AddRange(Collections.Where(c => profile != null && c.Get(profile) != null).Select(c => c.Label.ToLowerInvariant()));
        if (buttons.OwnLines(page) > 0)
            overrides.Add("buttons");
        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.TextDisabled(overrides.Count > 0 ? $"· overrides {string.Join(", ", overrides)}" : "· overrides nothing");
    }

    private bool DrawHudCard()
    {
        var changed = false;
        var profile = pages.ProfileOf(page);
        var parent = ProfilePages.ParentOf(page);
        var own = profile?.HudLayout;
        var inherited = parent != null ? Layout(parent) : null;
        var custom = parent == null || own != null;

        var open = false;
        var tooltip = page.StartsWith("j:") ? "Switched to when you change to this job" : "Switched to when you change to one of its jobs";
        Cards.Begin("hud", FontAwesomeIcon.Desktop, "HUD layout", tooltip, ref open, hasBody: false);

        Cards.Inline();
        if (custom)
        {
            var hud = own;
            var options = new List<(int? Value, string Label)> { (parent == null ? null : Profile.NoHudLayout, "Don't change") };
            options.AddRange(Enumerable.Range(0, Actions.HudLayouts).Select(i => ((int?)i, LayoutLabel(i))));
            if (widgets.Combo("##hud", ref hud, options, width: 160f))
            {
                pages.Create(page).HudLayout = hud;
                changed = true;
            }
        }
        else
        {
            Cards.Chip($"Inherited: {LayoutName(inherited)}", dimmed: true);
        }
        Cards.Next();
        if (ImGuiComponents.IconButton("names", FontAwesomeIcon.Pen))
            ImGui.OpenPopup(NamesPopup);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Name layouts");
        changed |= DrawLayoutNames();

        changed |= InheritSwitch(parent, custom, () => pages.Create(page).HudLayout = inherited ?? Profile.NoHudLayout,
                                 () => profile!.HudLayout = null);
        Cards.End();
        return changed;
    }

    private bool DrawLayoutNames()
    {
        if (!ImGui.BeginPopup(NamesPopup))
            return false;

        var changed = false;
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
        ImGui.EndPopup();
        return changed;
    }

    private bool DrawCollectionCard(Collection collection)
    {
        var changed = false;
        var profile = pages.ProfileOf(page);
        var parent = ProfilePages.ParentOf(page);
        var own = profile == null ? null : collection.Get(profile);
        var inherited = parent != null ? Ids(parent, collection.Get) : null;
        var shown = own ?? inherited ?? [];

        var open = !collapsedCards.Contains(collection.Id);
        var body = Cards.Begin(collection.Id, collection.Icon, collection.Label, collection.Tooltip, ref open, shown.Count > 0);
        Fold(collection.Id, open);

        var who = page;
        var choose = new ChooseRequest(collection.Kind, $"{collection.Label} for {TitleOf(who)}", own ?? [],
                                       ids => collection.Set(pages.Create(who), ids.Count > 0 ? ids : null),
                                       () => Sources(collection, profile));
        Cards.Inline();
        if (own != null || parent == null)
        {
            var text = own == null ? "Choose…"
                       : parent != null ? $"Custom: {Count(own.Count, collection.Unit)}" : Count(own.Count, collection.Unit);
            if (Cards.Chip(text, FontAwesomeIcon.Pen, clickable: true))
                widgets.Choose(choose);
        }
        else
        {
            Cards.Chip(inherited != null ? $"Inherited: {Count(inherited.Count, collection.Unit)}" : "Inherited: none", dimmed: true);
        }

        // Customize starts from what's inherited, or from the picker when that's nothing.
        changed |= InheritSwitch(parent, own != null,
                                 () =>
                                 {
                                     if (inherited != null)
                                         collection.Set(pages.Create(page), [.. inherited]);
                                     else
                                         widgets.Choose(choose);
                                 },
                                 () => collection.Set(profile!, null));

        if (body)
        {
            ImGui.Spacing();
            widgets.Collection(shown, collection.Kind, dimmed: own == null);
        }

        Cards.End();
        return changed;
    }

    private bool DrawButtonsCard()
    {
        var changed = false;
        var id = page == All ? "buttons-all" : "buttons";
        var lines = buttons.OwnLines(page);
        var parent = ProfilePages.ParentOf(page);

        var open = !collapsedCards.Contains(id);
        var tooltip = page == All ? "What your buttons do for every job"
                       : page.StartsWith("j:") ? "What your buttons do for this job" : "What your buttons do for its jobs";
        var body = Cards.Begin(id, FontAwesomeIcon.Gamepad, "Buttons", tooltip, ref open, lines > 0);

        Cards.Inline();
        Cards.Chip(lines > 0 ? $"{Count(lines, "line")} here" : parent != null ? $"Inherited ({TitleOf(parent)})" : "None",
                   dimmed: lines == 0);

        const string add = "+ Line";
        Cards.Right(ImGui.CalcTextSize(add).X + ImGui.GetStyle().FramePadding.X * 2);
        ImGui.BeginDisabled(!buttons.CanAddRule);
        if (ImGui.Button(add))
        {
            buttons.AddRule(page);
            open = true;
            changed = true;
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(buttons.CanAddRule ? ButtonsTab.AddRuleHelp : "Add a button or key on the Buttons tab first.");
        Fold(id, open);

        if (body)
        {
            ImGui.Spacing();
            changed |= buttons.DrawBehaviour(page);
        }

        Cards.End();
        return changed;
    }

    // Inherit | Customize at the right of a card's title line, on pages that follow another.
    private static bool InheritSwitch(string? parent, bool custom, Action customize, Action inherit)
    {
        if (parent == null)
            return false;
        Cards.Right(Cards.SegmentedWidth(Inherit, Customize));
        if (!Cards.Segmented(ref custom, Inherit, Customize))
            return false;
        if (custom)
            customize();
        else
            inherit();
        return true;
    }

    private bool DrawCopyPopup()
    {
        if (!ImGui.BeginPopup(CopyPopup))
            return false;

        var changed = false;
        ImGui.TextDisabled($"Copy into {TitleOf(page)}");
        if (page != All)
        {
            ImGui.Checkbox("HUD layout", ref copyHud);
            ImGui.SameLine();
            ImGui.Checkbox("Mounts", ref copyMounts);
            ImGui.SameLine();
            ImGui.Checkbox("Minions", ref copyMinions);
            ImGui.SameLine();
        }
        ImGui.Checkbox("Button lines", ref copyButtons);

        ImGui.BeginChild("##sources", new Vector2(320f, 260f) * ImGuiHelpers.GlobalScale, true);
        foreach (var who in Pages().Where(w => w != page))
        {
            if (ImGui.Selectable($"{TitleOf(who)}##{who}", copyFrom == who, ImGuiSelectableFlags.DontClosePopups))
                copyFrom = who;
        }
        ImGui.EndChild();

        var anything = copyButtons || (page != All && copyFrom != All && (copyHud || copyMounts || copyMinions));
        ImGui.BeginDisabled(copyFrom == null || !anything);
        if (ImGui.Button("Copy"))
        {
            Copy(copyFrom!);
            changed = true;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return changed;
    }

    // A page's effective settings, so the copy does what the source does now.
    private void Copy(string from)
    {
        if (page != All && from != All)
        {
            if (copyHud)
                pages.Create(page).HudLayout = Layout(from) ?? Profile.NoHudLayout;
            foreach (var collection in Collections)
            {
                if (collection.Kind == ShortcutKind.Mount ? copyMounts : copyMinions)
                    collection.Set(pages.Create(page), Ids(from, collection.Get) is { } ids ? [.. ids] : null);
            }
        }
        if (copyButtons)
            buttons.CopyLines(from, page);
    }

    private bool DrawStopPopup(string abbreviation)
    {
        if (!ImGui.BeginPopup(StopPopup))
            return false;

        var changed = false;
        var group = ProfilePages.GroupOf(abbreviation);
        var profile = config.Jobs.GetValueOrDefault(abbreviation);
        var parts = new List<string>();
        if (profile?.HudLayout != null)
            parts.Add("HUD layout");
        parts.AddRange(Collections.Where(c => profile != null && c.Get(profile) != null).Select(c => c.Label.ToLowerInvariant()));
        if (buttons.OwnLines(page) is var lines and > 0)
            parts.Add(Count(lines, "button line"));

        ImGui.TextUnformatted($"Stop customising {TitleOf(page)}?");
        ImGui.TextDisabled(parts.Count == 0
            ? $"It follows {JobGroups.Label(group)} again."
            : $"It follows {JobGroups.Label(group)} again, and its own {string.Join(", ", parts)} are removed.");
        if (ImGui.Button("Remove"))
        {
            config.Jobs.Remove(abbreviation);
            foreach (var binding in config.Bindings)
                binding.Rules.RemoveAll(r => r.Job == abbreviation);
            page = $"g:{group}";
            changed = true;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return changed;
    }

    // Other pages' own lists, to copy from in the mount and minion picker.
    private List<(string Label, List<uint> Ids)> Sources(Collection collection, Profile? current) =>
        Pages().Select(who => (Who: who, Profile: pages.ProfileOf(who)))
               .Where(p => p.Profile != null && p.Profile != current && collection.Get(p.Profile) is { Count: > 0 })
               .Select(p => (TitleOf(p.Who), collection.Get(p.Profile!)!))
               .ToList();

    private IEnumerable<string> Pages() =>
        new[] { All, $"g:{JobGroup.Combat}" }
            .Concat(JobGroups.All.Select(g => $"g:{g.Group}"))
            .Concat(JobGroups.All.SelectMany(g => g.Jobs).Where(IsCustomised).Select(a => $"j:{a}"));

    private void Fold(string card, bool open)
    {
        if (open)
            collapsedCards.Remove(card);
        else
            collapsedCards.Add(card);
    }

    // What a page does now, counting what it leaves to the pages above it.
    private int? Layout(string who) =>
        pages.ProfileOf(who)?.HudLayout ?? (ProfilePages.ParentOf(who) is { } parent ? Layout(parent) : null);

    private List<uint>? Ids(string who, Func<Profile, List<uint>?> get) =>
        (pages.ProfileOf(who) is { } profile ? get(profile) : null) ?? (ProfilePages.ParentOf(who) is { } parent ? Ids(parent, get) : null);

    private string TitleOf(string who) => ButtonsTab.ParseWho(who) switch
    {
        (string job, _) => JobLabel(job),
        (_, JobGroup group) => JobGroups.Label(group),
        _ => "All jobs",
    };

    private string SubtitleOf(string who) => ButtonsTab.ParseWho(who) switch
    {
        (_, JobGroup.Combat) => string.Join(" ", JobGroups.All.Where(g => JobGroups.Parent(g.Group) != null).SelectMany(g => g.Jobs).Select(JobShort)),
        (_, JobGroup group) => string.Join(" ", JobGroups.All.First(g => g.Group == group).Jobs.Select(JobShort)),
        (string job, _) => $"Customised from {JobGroups.Label(ProfilePages.GroupOf(job))}",
        _ => "Every job",
    };

    private void PageIcon(string who, float size)
    {
        var (job, group) = ButtonsTab.ParseWho(who);
        uint? icon = job != null && groups.Job(job) is { } row ? JobGroups.JobIconBase + row.RowId
                     : group == JobGroup.Limited && groups.Job("BLU") is { } blu ? JobGroups.JobIconBase + blu.RowId
                     : group is { } g ? RoleIcons.GetValueOrDefault(g)
                     : null;
        if (icon is not { } id)
        {
            var min = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(size));
            Cards.Icon(ImGui.GetWindowDrawList(), FontAwesomeIcon.Users, min + new Vector2(size / 2), ImGui.GetColorU32(ImGuiCol.Text));
            return;
        }

        var locked = job != null && groups.Job(job) is { } locks && player.IsLoaded && player.GetClassJobLevel(locks) == 0;
        var wrap = textures.GetFromGameIcon(new GameIconLookup(id)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(size), Vector2.Zero, Vector2.One, new Vector4(1f, 1f, 1f, locked ? 0.4f : 1f));
    }

    private string LayoutLabel(int layout) =>
        config.HudLayoutNames.ElementAtOrDefault(layout)?.Trim() is { Length: > 0 } name
            ? $"{layout + 1} · {name}"
            : $"Layout {layout + 1}";

    private string LayoutName(int? layout) => layout is >= 0 ? LayoutLabel(layout.Value) : "Don't change";

    private static string Count(int count, string unit) => $"{count} {unit}{(count == 1 ? string.Empty : "s")}";

    private bool IsCustomised(string abbreviation) =>
        config.Jobs.ContainsKey(abbreviation) || config.Bindings.Any(b => b.Rules.Any(r => r.Job == abbreviation));

    private string JobShort(string abbreviation) =>
        groups.Job(abbreviation) is { } job ? job.Abbreviation.ExtractText() : abbreviation;

    private string JobLabel(string abbreviation) =>
        groups.Job(abbreviation) is { } job ? $"{job.Abbreviation.ExtractText()} – {JobGroups.DisplayName(job)}" : abbreviation;
}
