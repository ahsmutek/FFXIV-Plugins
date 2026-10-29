using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace HuntMap;

internal sealed class HuntWindow : Window
{
    private enum Tab { Bills, Here, Browse, Search, Settings }

    private const GamepadButtons Watched =
        GamepadButtons.DpadUp | GamepadButtons.DpadDown | GamepadButtons.DpadLeft | GamepadButtons.DpadRight |
        GamepadButtons.South | GamepadButtons.East | GamepadButtons.North | GamepadButtons.West | GamepadButtons.L1 |
        GamepadButtons.R1 | GamepadButtons.Start;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan RepeatEvery = TimeSpan.FromMilliseconds(60);
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.15f, 0.1f, 1f);
    private static readonly Vector4 Grey = new(0.55f, 0.55f, 0.55f, 0.9f);
    // Below twice this, the map preview is left out.
    private const float MinListWidth = 260f;

    private readonly Marks marks;
    private readonly Bills bills;
    private readonly Checks checks;
    private readonly Configuration config;
    private readonly Action save;
    private readonly MapMarkers markers;
    private readonly IClientState client;
    private readonly IObjectTable objects;
    private readonly IGameGui gameGui;
    private readonly IGamepadState gamepad;
    private readonly ITextureProvider textures;

    private Tab tab = Tab.Bills;
    private bool selectTab = true;

    private GamepadButtons held;
    private GamepadButtons repeating;
    private DateTime repeatAt;
    private bool scrollToSelection;
    // The row under the mouse, once it moves; it shows in the preview until the pad moves the selection.
    private int mouseRow = -1;

    private List<Target> targets = new();
    private int billIndex;

    private uint hereTerritory;
    private int hereIndex;

    // Browse depth: 0 regions, 1 zones, 2 marks.
    private readonly int[] browseIndex = new int[3];
    private int browseDepth;
    private List<Region> browseRegions = new();
    private List<Mark> browseMarks = new();
    private List<(string Letter, List<Mark> Marks)> letters = new();
    private readonly HashSet<string> openLetters = new();
    // A letter's header has no mark.
    private List<(string Letter, Mark? Mark)> azRows = new();

    private string query = string.Empty;
    private List<Mark> results = new();
    private int searchIndex;
    private bool focusSearch;

    // A mark's spots, shown in its tab in place of the list until B.
    private Mark? spotsOf;
    private Tab spotsTab;
    // Where the hunt was started, so reopening during it goes back there.
    private Tab huntTab = Tab.Here;
    private bool takeFocus;
    // Spot indices, nearest first when the mark was opened.
    private List<int> spotOrder = new();
    private int spotIndex;

    public DateTime LastDrawn { get; private set; }

    // The pad only drives the window while it has focus, so clicking the game hands the pad back to it.
    public bool Focused { get; private set; }

    public HuntWindow(Marks marks, Bills bills, Checks checks, Configuration config, Action save, MapMarkers markers,
                      IClientState client, IObjectTable objects, IGameGui gameGui, IGamepadState gamepad, ITextureProvider textures)
        : base("Hunt Map###HuntMap")
    {
        this.marks = marks;
        this.bills = bills;
        this.checks = checks;
        this.config = config;
        this.save = save;
        this.markers = markers;
        this.client = client;
        this.objects = objects;
        this.gameGui = gameGui;
        this.gamepad = gamepad;
        this.textures = textures;
        Size = new Vector2(760, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        RefreshZones();
    }

    public void OpenSearch(string text)
    {
        query = text;
        Refresh();
        tab = Tab.Search;
        selectTab = true;
        spotsOf = null;
        IsOpen = true;
    }

    public void OpenSettings()
    {
        tab = Tab.Settings;
        selectTab = true;
        IsOpen = true;
    }

    public override void OnOpen()
    {
        takeFocus = true;
        // The press that opened the window shouldn't count.
        held = HeldButtons();
        repeating = GamepadButtons.None;
        targets = bills.Held();
        var here = targets.FindIndex(t => t.Mark.Map.TerritoryId == client.TerritoryType && !Bills.Done(t));
        if (markers.Target is { } hunted)
        {
            tab = huntTab;
            selectTab = true;
            OpenSpots(hunted);
        }
        else if (here >= 0)
        {
            tab = Tab.Bills;
            selectTab = true;
            billIndex = here;
            OpenSpots(targets[here].Mark);
        }
        else if (tab == Tab.Bills && targets.Count == 0 || tab == Tab.Here && HereMarks().Count == 0)
        {
            tab = HereMarks().Count > 0 ? Tab.Here : Tab.Browse;
            selectTab = true;
        }
    }

    public override void Draw()
    {
        LastDrawn = DateTime.UtcNow;
        if (takeFocus)
        {
            ImGui.SetWindowFocus();
            takeFocus = false;
        }
        Focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        targets = bills.Held();
        if (hereTerritory != client.TerritoryType)
        {
            hereTerritory = client.TerritoryType;
            hereIndex = 0;
        }

        HandlePad();

        if (ImGui.BeginTabBar("##tabs"))
        {
            DrawTab(Tab.Bills, "Bills", DrawBills);
            DrawTab(Tab.Here, "Here", DrawHere);
            DrawTab(Tab.Browse, "Browse", DrawBrowse);
            DrawTab(Tab.Search, "Search", DrawSearch);
            DrawTab(Tab.Settings, "Settings", DrawSettings);
            ImGui.EndTabBar();
        }
        selectTab = false;

        ImGui.Separator();
        DrawFooter();
    }

    // The Start/Menu button is shown as its ≡ icon, since the word "Start" read as part of the hint.
    private void DrawFooter()
    {
        var hint = OpenMark != null
                       ? "D-pad move · A show on map · X checked · Y nearest unchecked · B back"
                       : "D-pad move · A open · B back · L1/R1 tab · Y search · X region or A–Z";
        var hunt = OpenMark is { } open ? markers.Target == open ? "end hunt" : "hunt" : markers.Target != null ? "end hunt" : null;
        if (hunt == null)
        {
            ImGui.TextColored(Dim, Fit(hint, ImGui.GetContentRegionAvail().X));
            return;
        }

        var icon = FontAwesomeIcon.Bars.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var iconWidth = ImGui.CalcTextSize(icon).X;
        ImGui.PopFont();
        var huntWidth = ImGui.CalcTextSize($" · {hunt}").X + iconWidth + ImGui.GetStyle().ItemSpacing.X;
        ImGui.TextColored(Dim, Fit(hint, MathF.Max(0, ImGui.GetContentRegionAvail().X - huntWidth)));
        ImGui.SameLine(0, 0);
        ImGui.TextColored(Dim, " · ");
        ImGui.SameLine(0, 0);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(Dim, icon);
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextColored(Dim, hunt);
    }

    private Mark? OpenMark => tab == spotsTab ? spotsOf : null;

    private void DrawTab(Tab which, string label, Action draw)
    {
        var flags = selectTab && tab == which ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (!ImGui.BeginTabItem(label, flags))
            return;
        if (!selectTab && tab != which)
        {
            tab = which;
            scrollToSelection = true;
        }
        if (spotsOf != null && spotsTab == which)
            DrawSpots(spotsOf);
        else
            draw();
        ImGui.EndTabItem();
    }

    private void DrawSettings()
    {
        var close = config.CloseAfterFlag;
        if (ImGui.Checkbox("Close after opening the map", ref close))
        {
            config.CloseAfterFlag = close;
            save();
        }

        var arrive = config.CheckOnArrival;
        if (ImGui.Checkbox("Check off a spot when you reach it", ref arrive))
        {
            config.CheckOnArrival = arrive;
            save();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Ranks shown in Here, Browse and Search");
        foreach (var rank in Marks.Ranks)
        {
            var shown = !config.HiddenRanks.Contains(rank);
            if (!ImGui.Checkbox($"{rank} rank", ref shown))
                continue;
            if (shown)
                config.HiddenRanks.Remove(rank);
            else
                config.HiddenRanks.Add(rank);
            SettingsChanged();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Expansions shown in Browse and Search");
        foreach (var (id, name) in marks.Expansions)
        {
            var shown = !config.HiddenExpansions.Contains(id);
            if (!ImGui.Checkbox(name, ref shown))
                continue;
            if (shown)
                config.HiddenExpansions.Remove(id);
            else
                config.HiddenExpansions.Add(id);
            SettingsChanged();
        }
    }

    private void SettingsChanged()
    {
        save();
        browseDepth = 0;
        RefreshZones();
    }

    private void DrawBills()
    {
        if (targets.Count == 0)
        {
            ImGui.TextColored(Dim, "You don't hold an Elite Mark Bill. Any mark can still be found in Browse or Search.");
            return;
        }
        DrawList(targets.Count, ref billIndex, i => (targets[i].Mark.Name, targets[i].Mark.Zone, Progress(targets[i])),
                 i => OpenSpots(targets[i].Mark), i => Preview(targets[i].Mark), i => Bills.Done(targets[i]));
    }

    private void DrawHere()
    {
        var list = HereMarks();
        if (list.Count == 0)
        {
            ImGui.TextColored(Dim, "No hunt marks in this zone.");
            return;
        }

        ImGui.TextUnformatted(list[0].Zone);
        DrawList(list.Count, ref hereIndex, i => (list[i].Name, null, list[i].Rank), i => OpenSpots(list[i]), i => Preview(list[i]));
    }

    private void DrawBrowse()
    {
        var region = BrowseRegion();
        var zone = BrowseZone();

        if (browseDepth > 0)
        {
            if (ImGui.SmallButton("‹ Back"))
                Back();
            ImGui.SameLine();
        }
        else
        {
            if (ImGui.RadioButton("By region", !config.BrowseByName) && config.BrowseByName)
                ToggleBrowseOrder();
            ImGui.SameLine();
            if (ImGui.RadioButton("A–Z", config.BrowseByName) && !config.BrowseByName)
                ToggleBrowseOrder();
            ImGui.SameLine();
        }
        var title = (region, zone) switch
        {
            (not null, not null) => $"{region.Name} › {zone.Name}",
            (not null, null) => region.Name,
            _ => "",
        };
        ImGui.TextUnformatted(Fit(title, ImGui.GetContentRegionAvail().X));

        if (browseRegions.Count == 0)
        {
            ImGui.TextColored(Dim, "Every rank or expansion is hidden in Settings.");
            return;
        }

        if (config.BrowseByName)
        {
            DrawList(azRows.Count, ref browseIndex[0], AzRow, _ => Activate(), i => azRows[i].Mark is { } m ? Preview(m) : null);
        }
        else if (zone != null)
        {
            DrawList(zone.Marks.Count, ref browseIndex[browseDepth], i => (zone.Marks[i].Name, null, zone.Marks[i].Rank),
                     i => OpenSpots(zone.Marks[i]), i => Preview(zone.Marks[i]));
        }
        else if (browseDepth == 1)
        {
            var zones = BrowseZones();
            DrawList(zones.Count, ref browseIndex[browseDepth], i => (zones[i].Name, null, "›"), _ => Drill(),
                     i => (zones[i].Map, null, -1));
        }
        else
        {
            DrawList(browseRegions.Count, ref browseIndex[0], i => (browseRegions[i].Name, null, "›"), _ => Drill(), _ => null);
        }
    }

    private void DrawSearch()
    {
        if (focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            focusSearch = false;
        }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##query", "Part of a mark or zone name, or a rank", ref query, 64))
            Refresh();

        if (query.Trim().Length == 0)
            return;
        if (results.Count == 0)
        {
            ImGui.TextColored(Dim, "No matches.");
            return;
        }
        DrawList(results.Count, ref searchIndex, i => (results[i].Name, results[i].Zone, results[i].Rank), i => OpenSpots(results[i]),
                 i => Preview(results[i]));
    }

    private void DrawSpots(Mark mark)
    {
        if (ImGui.SmallButton("‹ Back"))
            Back();
        ImGui.SameLine();
        if (ImGui.SmallButton(markers.Target == mark ? "Stop hunting" : "Hunt"))
            ToggleHunt(mark);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Or press the Start/Menu button");
        ImGui.SameLine();
        var spotChecks = checks.Of(mark);
        var target = targets.FirstOrDefault(t => t.Mark == mark);
        ImGui.TextUnformatted(Fit($"{mark.Name} ({mark.Rank}) · {mark.Zone} · {(target != null ? Progress(target) + " · " : "")}" +
                                  $"{spotChecks?.Count ?? 0}/{mark.Spots.Count} spots checked", ImGui.GetContentRegionAvail().X));

        var player = PlayerCoord(mark.Map);
        DrawList(spotOrder.Count, ref spotIndex, i =>
                 {
                     var spot = mark.Spots[spotOrder[i]];
                     var name = $"{(spotChecks?.Contains(spot.Index) == true ? "✓" : "·")}  X {spot.Coord.X:0.0}  Y {spot.Coord.Y:0.0}";
                     return (name, null, player is { } at ? $"{Vector2.Distance(at, spot.Coord):0.0} away" : "");
                 },
                 i => Flag(mark, mark.Spots[spotOrder[i]]), i => (mark.Map, mark, spotOrder[i]),
                 i => spotChecks?.Contains(spotOrder[i]) == true);
    }

    private void DrawList(int count, ref int selected, Func<int, (string Name, string? Detail, string Right)> row,
                          Action<int> activate, Func<int, (MapInfo Map, Mark? Mark, int Highlight)?> preview,
                          Func<int, bool>? dim = null)
    {
        // Leaves room for the footer.
        var footer = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
        var avail = ImGui.GetContentRegionAvail() - new Vector2(0, footer);
        var previewSize = avail.X >= MinListWidth * 2 ? MathF.Min(avail.X / 2, avail.Y) : 0;
        var listWidth = previewSize > 0 ? avail.X - previewSize - ImGui.GetStyle().ItemSpacing.X : 0;

        selected = Math.Clamp(selected, 0, Math.Max(0, count - 1));
        var shown = selected;
        // Run after drawing, since activating can change the rows (an A–Z letter opening).
        var clicked = -1;
        if (ImGui.BeginChild("##list", new Vector2(listWidth, avail.Y)))
        {
            var top = ImGui.GetWindowPos().Y;
            var bottom = top + ImGui.GetWindowSize().Y;
            var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var mouseMoved = ImGui.GetIO().MouseDelta != Vector2.Zero;
            var anyHovered = false;
            for (var i = 0; i < count; i++)
            {
                var (name, detail, side) = row(i);
                var isSelected = i == selected;
                var start = ImGui.GetCursorPos();
                if (ImGui.Selectable($"##row{i}", isSelected))
                {
                    selected = i;
                    clicked = i;
                }
                if (ImGui.IsItemHovered())
                {
                    anyHovered = true;
                    if (mouseMoved)
                        mouseRow = i;
                }
                if (isSelected && scrollToSelection)
                {
                    var min = ImGui.GetItemRectMin().Y;
                    var max = ImGui.GetItemRectMax().Y;
                    if (min < top || max > bottom)
                        ImGui.SetScrollHereY(min < top ? 0f : 1f);
                    scrollToSelection = false;
                }

                var sideWidth = ImGui.CalcTextSize(side).X;
                var room = right - sideWidth - spacing - start.X;
                ImGui.SetCursorPos(start);
                var fitted = Fit(name, room);
                if (dim?.Invoke(i) == true)
                    ImGui.TextColored(Dim, fitted);
                else
                    ImGui.TextUnformatted(fitted);
                room -= ImGui.CalcTextSize(fitted).X + spacing;
                if (detail != null && fitted == name && room > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Dim, Fit(detail, room));
                }
                ImGui.SameLine(right - sideWidth);
                ImGui.TextColored(Dim, side);
            }
            if (!anyHovered)
                mouseRow = -1;
            if (mouseRow >= 0 && mouseRow < count)
                shown = mouseRow;
        }
        ImGui.EndChild();

        if (previewSize > 0 && count > 0 && preview(shown) is { } map)
        {
            ImGui.SameLine();
            DrawMap(map.Map, map.Mark, map.Highlight, previewSize);
        }
        if (clicked >= 0)
            activate(clicked);
    }

    private void DrawMap(MapInfo map, Mark? mark, int highlighted, float size)
    {
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddImage(textures.GetFromGame(map.Texture).GetWrapOrEmpty().Handle, min, min + new Vector2(size));

        var radius = MathF.Max(3f, size / 90f);
        var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        if (mark != null)
        {
            var spotChecks = checks.Of(mark);
            foreach (var spot in mark.Spots)
            {
                var center = min + spot.Pixel / 2048f * size;
                drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(spotChecks?.Contains(spot.Index) == true ? Grey : Red));
                if (spot.Index == highlighted)
                {
                    drawList.AddCircle(center, radius, white, 0, 2f);
                    drawList.AddCircle(center, radius * 2.8f, ImGui.GetColorU32(Red), 0, 2f);
                }
            }
        }

        if (PlayerCoord(map) is { } at)
        {
            var center = min + map.PixelOf(at) / 2048f * size;
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 1f)));
            drawList.AddCircle(center, radius, white, 0, 1.5f);
        }
    }

    private static string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
            return text;
        for (var length = text.Length - 1; length > 0; length--)
        {
            var trimmed = text[..length].TrimEnd() + "…";
            if (ImGui.CalcTextSize(trimmed).X <= width)
                return trimmed;
        }
        return "…";
    }

    private static (MapInfo, Mark?, int)? Preview(Mark mark) => (mark.Map, mark, -1);

    private static string Progress(Target target) =>
        Bills.Done(target) ? "done" : $"{Bills.Kills(target)}/{target.Needed} killed";

    private void HandlePad()
    {
        var now = HeldButtons();
        var pressed = now & ~held;
        held = now;
        if (!Focused)
            return;

        var vertical = now & (GamepadButtons.DpadUp | GamepadButtons.DpadDown);
        var step = 0;
        if ((pressed & vertical) != 0)
        {
            repeating = pressed & vertical;
            repeatAt = DateTime.UtcNow + RepeatDelay;
            step = repeating.HasFlag(GamepadButtons.DpadUp) ? -1 : 1;
        }
        else if (repeating != GamepadButtons.None && (now & repeating) != 0 && DateTime.UtcNow >= repeatAt)
        {
            repeatAt = DateTime.UtcNow + RepeatEvery;
            step = repeating.HasFlag(GamepadButtons.DpadUp) ? -1 : 1;
        }
        else if ((now & repeating) == 0)
        {
            repeating = GamepadButtons.None;
        }

        if (step != 0)
            Move(step);
        if ((pressed & (GamepadButtons.DpadLeft | GamepadButtons.L1)) != 0)
            SwitchTab(-1);
        if ((pressed & (GamepadButtons.DpadRight | GamepadButtons.R1)) != 0)
            SwitchTab(1);
        if (pressed.HasFlag(GamepadButtons.North))
        {
            if (OpenMark is { } mark)
            {
                FlagNearestUnchecked(mark);
            }
            else
            {
                tab = Tab.Search;
                selectTab = focusSearch = true;
            }
        }
        if (pressed.HasFlag(GamepadButtons.West))
        {
            if (OpenMark is { } mark && spotOrder.Count > 0)
                ToggleChecked(mark, spotOrder[Math.Clamp(spotIndex, 0, spotOrder.Count - 1)]);
            else if (OpenMark == null && tab == Tab.Browse)
                ToggleBrowseOrder();
        }
        if (pressed.HasFlag(GamepadButtons.Start))
        {
            if (OpenMark is { } open)
                ToggleHunt(open);
            else if (markers.Target != null)
                markers.Hunt(null);
        }
        if (pressed.HasFlag(GamepadButtons.South))
            Activate();
        if (pressed.HasFlag(GamepadButtons.East))
            Back();
    }

    private GamepadButtons HeldButtons()
    {
        var buttons = GamepadButtons.None;
        foreach (var button in Enum.GetValues<GamepadButtons>())
            if ((Watched & button) != 0 && gamepad.Raw(button) > 0)
                buttons |= button;
        return buttons;
    }

    private void Move(int step)
    {
        if (OpenMark != null)
        {
            spotIndex = Wrap(spotIndex + step, spotOrder.Count);
        }
        else
        {
            switch (tab)
            {
                case Tab.Bills:
                    billIndex = Wrap(billIndex + step, targets.Count);
                    break;
                case Tab.Here:
                    hereIndex = Wrap(hereIndex + step, HereMarks().Count);
                    break;
                case Tab.Browse:
                    browseIndex[browseDepth] = Wrap(browseIndex[browseDepth] + step, BrowseCount());
                    break;
                case Tab.Search:
                    searchIndex = Wrap(searchIndex + step, results.Count);
                    break;
            }
        }
        scrollToSelection = true;
        mouseRow = -1;
    }

    private static int Wrap(int index, int count) => count == 0 ? 0 : (index % count + count) % count;

    private void SwitchTab(int step)
    {
        tab = (Tab)Wrap((int)tab + step, Enum.GetValues<Tab>().Length);
        selectTab = scrollToSelection = true;
    }

    private void Activate()
    {
        if (OpenMark is { } mark)
        {
            if (spotOrder.Count > 0)
                Flag(mark, mark.Spots[Pick(spotOrder, spotIndex)]);
            return;
        }
        switch (tab)
        {
            case Tab.Bills when targets.Count > 0:
                OpenSpots(Pick(targets, billIndex).Mark);
                break;
            case Tab.Here when HereMarks() is { Count: > 0 } list:
                OpenSpots(Pick(list, hereIndex));
                break;
            case Tab.Browse when config.BrowseByName:
                if (azRows.Count == 0)
                    break;
                var row = Pick(azRows, browseIndex[0]);
                if (row.Mark != null)
                    OpenSpots(row.Mark);
                else
                    ToggleLetter(row.Letter);
                break;
            case Tab.Browse when BrowseZone() is { } zone:
                OpenSpots(Pick(zone.Marks, browseIndex[browseDepth]));
                break;
            case Tab.Browse:
                Drill();
                break;
            case Tab.Search when results.Count > 0:
                OpenSpots(Pick(results, searchIndex));
                break;
        }
    }

    private void Back()
    {
        if (OpenMark != null)
        {
            spotsOf = null;
            scrollToSelection = true;
            mouseRow = -1;
        }
        else if (tab == Tab.Browse && config.BrowseByName && azRows.Count > 0 &&
                 Pick(azRows, browseIndex[0]).Letter is var letter && openLetters.Contains(letter))
        {
            ToggleLetter(letter);
        }
        else if (tab == Tab.Browse && browseDepth > 0)
        {
            browseDepth--;
            scrollToSelection = true;
        }
        else
        {
            IsOpen = false;
        }
    }

    private void Drill()
    {
        if (BrowseCount() == 0)
            return;
        browseDepth++;
        browseIndex[browseDepth] = 0;
        scrollToSelection = true;
    }

    private List<Mark> HereMarks() => marks.In(client.TerritoryType).Where(m => !config.HiddenRanks.Contains(m.Rank)).ToList();

    // By region: regions, then zones, then marks. A–Z: one list of letters and the marks under the open ones.
    private Region? BrowseRegion() =>
        !config.BrowseByName && browseDepth > 0 && browseRegions.Count > 0 ? Pick(browseRegions, browseIndex[0]) : null;

    private List<Zone> BrowseZones() => BrowseRegion()?.Zones ?? [];

    private Zone? BrowseZone() =>
        browseDepth > 1 && BrowseZones() is { Count: > 0 } zones ? Pick(zones, browseIndex[1]) : null;

    private int BrowseCount() =>
        config.BrowseByName ? azRows.Count :
        BrowseZone() is { } zone ? zone.Marks.Count :
        browseDepth == 1 ? BrowseZones().Count : browseRegions.Count;

    private (string Name, string? Detail, string Right) AzRow(int i)
    {
        var (letter, mark) = azRows[i];
        if (mark != null)
            return ("    " + mark.Name, mark.Zone, mark.Rank);
        var count = letters.First(l => l.Letter == letter).Marks.Count;
        return (letter, null, $"{count}  {(openLetters.Contains(letter) ? "⌄" : "›")}");
    }

    // Keeps the selection on the letter's header.
    private void ToggleLetter(string letter)
    {
        if (!openLetters.Remove(letter))
            openLetters.Add(letter);
        BuildAzRows();
        browseIndex[0] = azRows.FindIndex(r => r.Mark == null && r.Letter == letter);
        scrollToSelection = true;
        mouseRow = -1;
    }

    private void BuildAzRows()
    {
        azRows = new();
        foreach (var (letter, list) in letters)
        {
            azRows.Add((letter, null));
            if (openLetters.Contains(letter))
                azRows.AddRange(list.Select(m => (letter, (Mark?)m)));
        }
    }

    private static T Pick<T>(List<T> list, int index) => list[Math.Clamp(index, 0, list.Count - 1)];

    private void ToggleBrowseOrder()
    {
        config.BrowseByName = !config.BrowseByName;
        save();
        browseDepth = 0;
        browseIndex[0] = 0;
        scrollToSelection = true;
    }

    private void RefreshZones()
    {
        browseRegions = marks.Regions
                             .Select(r => r with
                             {
                                 Zones = r.Zones.Where(z => !config.HiddenExpansions.Contains(z.Expansion))
                                              .Select(z => z with { Marks = z.Marks.Where(m => !config.HiddenRanks.Contains(m.Rank)).ToList() })
                                              .Where(z => z.Marks.Count > 0)
                                              .ToList(),
                             })
                             .Where(r => r.Zones.Count > 0)
                             .ToList();
        browseMarks = browseRegions.SelectMany(r => r.Zones).SelectMany(z => z.Marks).ToList();
        letters = Marks.Search("", browseMarks)
                       .GroupBy(m => Marks.Letter(m.Name))
                       .Select(g => (g.Key, g.ToList()))
                       .ToList();
        BuildAzRows();
        Refresh();
    }

    private void Refresh()
    {
        results = query.Trim().Length == 0 ? new() : Marks.Search(query, browseMarks);
        searchIndex = 0;
    }

    private void OpenSpots(Mark mark)
    {
        spotsOf = mark;
        spotsTab = tab;
        spotOrder = PlayerCoord(mark.Map) is { } at
                        ? mark.Spots.OrderBy(s => Vector2.DistanceSquared(at, s.Coord)).Select(s => s.Index).ToList()
                        : mark.Spots.Select(s => s.Index).ToList();
        spotIndex = 0;
        scrollToSelection = true;
        mouseRow = -1;
    }

    private void FlagNearestUnchecked(Mark mark)
    {
        var spotChecks = checks.Of(mark);
        var at = PlayerCoord(mark.Map);
        var nearest = mark.Spots.Where(s => spotChecks?.Contains(s.Index) != true)
                          .OrderBy(s => at is { } p ? Vector2.DistanceSquared(p, s.Coord) : s.Index)
                          .FirstOrDefault();
        if (nearest == null)
            return;
        spotIndex = Math.Max(0, spotOrder.IndexOf(nearest.Index));
        scrollToSelection = true;
        mouseRow = -1;
        Flag(mark, nearest);
    }

    private void ToggleHunt(Mark mark)
    {
        if (markers.Target == mark)
        {
            markers.Hunt(null);
            return;
        }
        huntTab = spotsTab;
        markers.Hunt(mark);
        IsOpen = false;
    }

    private void ToggleChecked(Mark mark, int spot)
    {
        checks.Toggle(mark, spot);
        markers.Redraw();
    }

    private Vector2? PlayerCoord(MapInfo map) =>
        client.TerritoryType == map.TerritoryId && objects.LocalPlayer is { } player
            ? map.CoordOf(new Vector2(player.Position.X, player.Position.Z))
            : null;

    private void Flag(Mark mark, Spot spot)
    {
        if (markers.Target != mark)
            huntTab = spotsTab;
        markers.Hunt(mark);
        gameGui.OpenMapWithMapLink(mark.Map.TerritoryId, mark.Map.MapId, new Vector3(spot.World.X, 0, spot.World.Y));
        if (config.CloseAfterFlag)
            IsOpen = false;
    }
}
