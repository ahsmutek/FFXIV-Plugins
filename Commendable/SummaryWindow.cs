using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Icons;
using ActionRow = Lumina.Excel.Sheets.Action;
using Status = Lumina.Excel.Sheets.Status;
using World = Lumina.Excel.Sheets.World;

namespace Commendable;

internal enum SummaryView
{
    Current,
    Last,
    Replay,
}

internal sealed class SummaryWindow : Window
{
    private const uint JobIconBase = 62000;
    private const string PlayerMenu = "##player";
    public const string CommendationWindow = "BannerMIP";
    private static readonly Vector4 OutgoingColor = new(0.55f, 0.8f, 1f, 1f);
    private static readonly Vector4 BookmarkColor = new(1f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 HighlightColor = new(0.55f, 0.9f, 0.55f, 1f);
    // How often relationship durations tick while one is still going.
    private static readonly TimeSpan LiveEvery = TimeSpan.FromSeconds(1);

    private static readonly (SortOrder Value, string Label)[] SortOptions =
    [
        (SortOrder.Party, "Party order"),
        (SortOrder.Bookmarks, "Bookmarks first"),
        (SortOrder.Recent, "Most recent"),
        (SortOrder.Name, "Name"),
    ];

    private readonly Configuration config;
    private readonly Action save;
    private readonly Sessions sessions;
    private readonly IDataManager data;
    private readonly ITextureProvider textures;
    private readonly IGameGui gameGui;
    private readonly Action openSettings;
    private readonly PadTabs tabs = new();
    private readonly List<(SummaryView View, DutySession Session, string Label)> views = [];
    private readonly Dictionary<DutySession, (int Version, DateTime BuiltAt, Rows Rows)> rows = [];
    private readonly Dictionary<uint, (uint Icon, string Name)> actions = [];
    private readonly Dictionary<uint, string> statusNames = [];
    private readonly Dictionary<uint, string> worldNames = [];
    private readonly HashSet<PlayerRecord> expanded = [];

    private DutySession? replay;
    private SummaryView? selectView;
    private string noteDraft = "";

    private sealed record Row(PlayerRecord Player, string Suffix, List<Line> Lines);

    private sealed record Rows(List<Row> Shown, int Hidden, bool Live);

    public SummaryWindow(Configuration config, Action save, Sessions sessions, IDataManager data, ITextureProvider textures,
                         IGameGui gameGui, Action openSettings)
        : base("Commendable###CommendableSummary")
    {
        this.config = config;
        this.save = save;
        this.sessions = sessions;
        this.data = data;
        this.textures = textures;
        this.gameGui = gameGui;
        this.openSettings = openSettings;
        // B hands the controller back to the game and leaves the summary up beside the commendations.
        RespectCloseHotkey = false;
        Size = new Vector2(320, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(240, 160) };
    }

    public bool HasRows(DutySession session) => RowsOf(session).Shown.Count > 0;

    public void Invalidate() => rows.Clear();

    // A focused window takes the controller, which should stay with the game.
    public void OpenWithoutFocus(SummaryView view)
    {
        if (IsOpen)
            return;
        selectView = view;
        PlaceBesideCommendations();
        Flags |= ImGuiWindowFlags.NoFocusOnAppearing;
        IsOpen = true;
    }

    public void Show(SummaryView view)
    {
        selectView = view;
        if (!IsOpen)
        {
            PlaceBesideCommendations();
            IsOpen = true;
        }
        if (sessions.Recording)
            Flags |= ImGuiWindowFlags.NoFocusOnAppearing;
        else
            BringToFront();
    }

    // Mid-duty the controller and keyboard stay with the game, even after a click on the summary.
    public override void PreDraw()
    {
        if (sessions.Recording)
            Flags |= ImGuiWindowFlags.NoNav;
        else
            Flags &= ~ImGuiWindowFlags.NoNav;
    }

    public void ShowReplay(DutySession session)
    {
        replay = session;
        rows.Remove(session);
        Show(SummaryView.Replay);
    }

    // A replay and open event logs only last while the window is up.
    public override void OnClose()
    {
        replay = null;
        expanded.Clear();
        rows.Clear();
    }

    public override void Draw()
    {
        Flags &= ~ImGuiWindowFlags.NoFocusOnAppearing;

        views.Clear();
        if (sessions.Current is { } current)
            views.Add((SummaryView.Current, current, "This duty"));
        if (sessions.Last is { } last && last != sessions.Current)
            views.Add((SummaryView.Last, last, "Previous duty"));
        if (replay != null)
            views.Add((SummaryView.Replay, replay, "Replay"));

        var shown = views.Count == 1 ? views[0].Session : null;
        if (views.Count > 1 && ImGui.BeginTabBar("##views"))
        {
            foreach (var (view, session, label) in views)
            {
                if (!tabs.Item(label, view == selectView))
                    continue;
                shown = session;
                ImGui.EndTabItem();
            }
            tabs.End();
            ImGui.EndTabBar();
        }
        selectView = null;

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        using (var list = ImRaii.Child("##list", new Vector2(0, -footer)))
        {
            if (list)
                DrawSession(shown);
        }

        ImGui.Separator();
        DrawFooter(shown);
    }

    private void DrawSession(DutySession? session)
    {
        if (session == null)
        {
            Wrapped("No duty yet. Commendable starts recording when you enter one.");
            return;
        }

        ImGui.TextUnformatted(session.DutyName);
        ImGui.SameLine();
        ImGui.TextDisabled(session.IsCompleted ? "complete" : session == sessions.Current ? "in progress" : "not completed");

        var shown = RowsOf(session).Shown;
        if (shown.Count == 0)
        {
            Wrapped(session.IsCompleted
                        ? "Nothing Commendable tracks happened with anyone in this duty."
                        : "Nothing yet. Partners, raises, protection, cards and the like show up here as they happen.");
            return;
        }

        foreach (var row in shown)
            DrawRow(session, row);
    }

    private void DrawRow(DutySession session, Row row)
    {
        var player = row.Player;
        using var id = ImRaii.PushId($"{player.Key.Name}@{player.Key.WorldId}");
        using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, player.LeftAt != null);

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var height = ImGui.GetFrameHeight();
        var iconSize = new Vector2(config.Compact ? ImGui.GetTextLineHeight() : height);
        if (player.JobId != 0)
            ImGui.Image(textures.GetFromGameIcon(new GameIconLookup(JobIconBase + player.JobId)).GetWrapOrEmpty().Handle,
                        iconSize);
        else
            ImGui.Dummy(iconSize);
        ImGui.SameLine();

        var bookmarkWidth = config.Bookmarks ? height + spacing : 0;
        var nameWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X - bookmarkWidth);
        var open = expanded.Contains(player);
        if (ImGui.Selectable("##name", open, ImGuiSelectableFlags.None, new Vector2(nameWidth, height)) &&
            !expanded.Remove(player))
            expanded.Add(player);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(open ? "Hide the event log" : "Show the event log.\nRight-click (Y) to bookmark or add a note.");
        var textAt = ImGui.GetItemRectMin() + new Vector2(0, (height - ImGui.GetTextLineHeight()) / 2);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(textAt, ImGui.GetColorU32(ImGuiCol.Text), player.Key.Name);
        drawList.AddText(textAt + new Vector2(ImGui.CalcTextSize(player.Key.Name).X + spacing, 0),
                         ImGui.GetColorU32(ImGuiCol.TextDisabled), row.Suffix);
        DrawPlayerMenu(session, player);

        if (config.Bookmarks)
        {
            ImGui.SameLine();
            var color = player.Bookmarked ? BookmarkColor : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            using (ImRaii.PushColor(ImGuiCol.Text, color))
            {
                if (TablerIcons.Button(TablerIcon.Bookmark))
                    Bookmarks.Toggle(session, player);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(player.Bookmarked ? "Remove bookmark" : "Bookmark");
        }

        var indent = iconSize.X + spacing;
        ImGui.Indent(indent);
        if (config.Compact)
        {
            if (row.Lines.Count > 0)
                Wrapped(string.Join(" · ", row.Lines.Select(l => l.Text)), disabled: false);
        }
        else
        {
            foreach (var line in row.Lines)
                DrawLine(line);
        }
        if (player.Note.Length > 0)
            Wrapped($"“{player.Note}”");
        if (open)
            DrawEvents(session, player);
        ImGui.Unindent(indent);
    }

    private void DrawEvents(DutySession session, PlayerRecord player)
    {
        var any = false;
        foreach (var interaction in player.Interactions.Where(i => Shown(i.Category, player)).OrderBy(i => i.At))
        {
            ImGui.TextDisabled(Lines.Event(interaction, session.StartedAt, ActionName, StatusName));
            any = true;
        }
        if (!any)
            ImGui.TextDisabled("Nothing recorded.");
    }

    private void DrawLine(Line line)
    {
        var size = new Vector2(ImGui.GetTextLineHeight());
        if (line.ActionId != 0)
            ImGui.Image(textures.GetFromGameIcon(new GameIconLookup(ActionInfo(line.ActionId).Icon)).GetWrapOrEmpty().Handle,
                        size);
        else
            ImGui.Dummy(size);
        ImGui.SameLine();

        Vector4? color = line.Direction == Direction.Outgoing ? OutgoingColor
                         : line.Highlight ? HighlightColor
                         : line.Outcome == Outcome.Offered ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]
                         : null;
        using (ImRaii.PushColor(ImGuiCol.Text, color ?? default, color.HasValue))
            ImGui.TextUnformatted(line.Text);
    }

    private void DrawPlayerMenu(DutySession session, PlayerRecord player)
    {
        if (!PadNav.ContextMenu(PlayerMenu))
            return;

        if (ImGui.IsWindowAppearing())
            noteDraft = player.Note;
        ImGui.TextDisabled(player.Key.Name);
        if (!config.Bookmarks)
        {
            ImGui.TextDisabled("Bookmarks are off in settings.");
        }
        else
        {
            if (ImGui.Selectable(player.Bookmarked ? "Remove bookmark" : "Bookmark"))
                Bookmarks.Toggle(session, player);
            ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
            if (ImGui.InputTextWithHint("##note", "Note (bookmarks them too)", ref noteDraft, 100))
                Bookmarks.SetNote(session, player, noteDraft.Trim());
        }
        ImGui.EndPopup();
    }

    private void DrawFooter(DutySession? session)
    {
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        var current = Array.Find(SortOptions, o => o.Value == config.Sort).Label ?? config.Sort.ToString();
        if (ImGui.BeginCombo("##sort", current))
        {
            foreach (var (value, label) in SortOptions)
            {
                if (ImGui.Selectable(label, value == config.Sort))
                {
                    config.Sort = value;
                    save();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Sort players");

        if (session != null && RowsOf(session).Hidden is > 0 and var hidden)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled($"{hidden} hidden");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Players with nothing recorded, or who left, as set in settings.");
        }

        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
        if (TablerIcons.Button(TablerIcon.Settings))
            openSettings();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Settings");
    }

    private Rows RowsOf(DutySession session)
    {
        var now = DateTime.UtcNow;
        if (rows.TryGetValue(session, out var cached) && cached.Version == session.Version &&
            (!cached.Rows.Live || now - cached.BuiltAt < LiveEvery))
            return cached.Rows;

        var end = session.CompletedAt ?? now;
        var all = session.Players.Values
                         .Select(p => new Row(p, Suffix(p), Lines.Of(p, c => Shown(c, p), ActionName, end)))
                         .ToList();
        var shown = Sorted(all.Where(r => (!config.HideLeft || r.Player.LeftAt == null) &&
                                          (!config.HideQuiet || r.Lines.Count > 0 ||
                                           (config.Bookmarks && r.Player.Bookmarked)))).ToList();
        var live = shown.Any(r => r.Player.Interactions.Any(i => Lines.IsRelationship(i.Category) && i.EndedAt == null));
        var built = new Rows(shown, all.Count - shown.Count, live);
        rows[session] = (session.Version, now, built);
        return built;
    }

    private bool Shown(Category category, PlayerRecord player) => category switch
    {
        Category.Healing => config.Healing == HealingShown.All ||
                            (config.Healing == HealingShown.NonHealers && !Registry.HealerJobs.Contains(player.JobId)),
        _ => !config.DisabledCategories.Contains(category),
    };

    private IEnumerable<Row> Sorted(IEnumerable<Row> shown) => config.Sort switch
    {
        SortOrder.Bookmarks => shown.OrderByDescending(r => r.Player.Bookmarked).ThenBy(r => r.Player.Membership)
                                    .ThenBy(r => r.Player.Order),
        SortOrder.Recent => shown.OrderByDescending(r => r.Player.LastAt ?? DateTime.MinValue)
                                 .ThenBy(r => r.Player.Membership).ThenBy(r => r.Player.Order),
        SortOrder.Name => shown.OrderBy(r => r.Player.Key.Name, StringComparer.CurrentCultureIgnoreCase),
        _ => shown.OrderBy(r => r.Player.Membership).ThenBy(r => r.Player.Order)
                  .ThenBy(r => r.Player.Key.Name, StringComparer.CurrentCultureIgnoreCase),
    };

    private string Suffix(PlayerRecord player)
    {
        if (!worldNames.TryGetValue(player.Key.WorldId, out var world))
            worldNames[player.Key.WorldId] = world =
                data.GetExcelSheet<World>().GetRowOrDefault(player.Key.WorldId)?.Name.ExtractText() ?? "";
        var suffix = world.Length > 0 ? $"@{world}" : "";
        return player.LeftAt != null ? $"{suffix} · left" : suffix;
    }

    private (uint Icon, string Name) ActionInfo(uint actionId)
    {
        if (actions.TryGetValue(actionId, out var action))
            return action;
        var row = data.GetExcelSheet<ActionRow>().GetRowOrDefault(actionId);
        return actions[actionId] = (row?.Icon ?? 0, row?.Name.ExtractText() is { Length: > 0 } name ? name : $"Action {actionId}");
    }

    private string ActionName(uint actionId) => ActionInfo(actionId).Name;

    private string StatusName(uint statusId)
    {
        if (!statusNames.TryGetValue(statusId, out var name))
            statusNames[statusId] = name =
                data.GetExcelSheet<Status>().GetRowOrDefault(statusId)?.Name.ExtractText() is { Length: > 0 } found
                    ? found
                    : $"status {statusId}";
        return name;
    }

    // Only while the window isn't kept where you left it.
    private void PlaceBesideCommendations()
    {
        Position = null;
        if (config.RememberPosition)
            return;
        var addon = gameGui.GetAddonByName(CommendationWindow);
        if (addon.IsNull || !addon.IsVisible)
            return;
        Position = ImGuiHelpers.MainViewport.Pos + addon.Position +
                   new Vector2(addon.ScaledSize.X + 8f * ImGuiHelpers.GlobalScale, 0);
        PositionCondition = ImGuiCond.Appearing;
    }

    private static void Wrapped(string text, bool disabled = true)
    {
        ImGui.PushTextWrapPos();
        if (disabled)
            ImGui.TextDisabled(text);
        else
            ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}
