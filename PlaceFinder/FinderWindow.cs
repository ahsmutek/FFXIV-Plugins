using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace PlaceFinder;

internal sealed class FinderWindow : Window
{
    private enum Tab { Here, Browse, Search, Settings }

    private const GamepadButtons Watched =
        GamepadButtons.DpadUp | GamepadButtons.DpadDown | GamepadButtons.DpadLeft | GamepadButtons.DpadRight |
        GamepadButtons.South | GamepadButtons.East | GamepadButtons.North | GamepadButtons.West | GamepadButtons.L1 |
        GamepadButtons.R1;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan RepeatEvery = TimeSpan.FromMilliseconds(60);
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);
    // Below twice this, the map preview is left out.
    private const float MinListWidth = 260f;

    private readonly Places places;
    private readonly Configuration config;
    private readonly Action save;
    private readonly IClientState client;
    private readonly IGameGui gameGui;
    private readonly IGamepadState gamepad;
    private readonly ITextureProvider textures;

    private Tab tab = Tab.Here;
    private bool selectTab = true;

    private GamepadButtons held;
    private GamepadButtons repeating;
    private DateTime repeatAt;
    private bool scrollToSelection;
    // The row under the mouse, once it moves; it shows in the preview until the pad moves the selection.
    private int mouseRow = -1;

    private uint hereTerritory;
    private int hereIndex;

    // Browse depth: 0 regions, 1 zones, 2 places.
    private readonly int[] browseIndex = new int[3];
    private int browseDepth;
    private List<Region> browseRegions = new();
    private List<Zone> browseZones = new();
    private List<(string Letter, List<Place> Places)> letters = new();
    private readonly HashSet<string> openLetters = new();
    // A letter's header has no place.
    private List<(string Letter, Place? Place)> azRows = new();

    private string query = string.Empty;
    private List<Place> results = new();
    private int searchIndex;
    private bool focusSearch;

    public DateTime LastDrawn { get; private set; }

    public FinderWindow(Places places, Configuration config, Action save, IClientState client, IGameGui gameGui,
                        IGamepadState gamepad, ITextureProvider textures)
        : base("PlaceFinder###PlaceFinder")
    {
        this.places = places;
        this.config = config;
        this.save = save;
        this.client = client;
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
        // The press that opened the window shouldn't count.
        held = HeldButtons();
        repeating = GamepadButtons.None;
        if (tab == Tab.Here && places.In(client.TerritoryType).Count == 0)
        {
            tab = Tab.Browse;
            selectTab = true;
        }
    }

    public override void Draw()
    {
        LastDrawn = DateTime.UtcNow;
        if (hereTerritory != client.TerritoryType)
        {
            hereTerritory = client.TerritoryType;
            hereIndex = 0;
        }

        HandlePad();

        if (ImGui.BeginTabBar("##tabs"))
        {
            DrawTab(Tab.Here, "Here", DrawHere);
            DrawTab(Tab.Browse, "Browse", DrawBrowse);
            DrawTab(Tab.Search, "Search", DrawSearch);
            DrawTab(Tab.Settings, "Settings", DrawSettings);
            ImGui.EndTabBar();
        }
        selectTab = false;

        ImGui.Separator();
        ImGui.TextColored(Dim, Fit("D-pad move · A open map · B back · L1/R1 tab · Y search · X region or A–Z",
                                   ImGui.GetContentRegionAvail().X));
    }

    private void DrawSettings()
    {
        var close = config.CloseAfterFlag;
        if (ImGui.Checkbox("Close after opening the map", ref close))
        {
            config.CloseAfterFlag = close;
            save();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Expansions shown in Browse and Search");
        foreach (var (id, name) in places.Expansions)
        {
            var shown = !config.HiddenExpansions.Contains(id);
            if (!ImGui.Checkbox(name, ref shown))
                continue;
            if (shown)
                config.HiddenExpansions.Remove(id);
            else
                config.HiddenExpansions.Add(id);
            save();
            browseDepth = 0;
            RefreshZones();
        }
    }

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
        draw();
        ImGui.EndTabItem();
    }

    private void DrawHere()
    {
        var list = places.In(client.TerritoryType);
        if (list.Count == 0)
        {
            ImGui.TextColored(Dim, "No named places on this zone's map.");
            return;
        }

        var showZone = list.Select(p => p.Zone).Distinct().Count() > 1;
        ImGui.TextUnformatted(showZone ? list[0].Zone.Split(" – ")[0] : list[0].Zone);
        hereIndex = Math.Clamp(hereIndex, 0, list.Count - 1);
        DrawList(list.Count, ref hereIndex, i => (list[i].Name, showZone ? list[i].Zone : null, Coords(list[i])), i => Flag(list[i]),
                 i => MarkerOn(list[i]));
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
            (null, not null) => zone.Name,
            _ => "",
        };
        ImGui.TextUnformatted(Fit(title, ImGui.GetContentRegionAvail().X));

        if (browseRegions.Count == 0)
        {
            ImGui.TextColored(Dim, "All expansions are hidden in Settings.");
            return;
        }

        if (config.BrowseByName)
        {
            DrawList(azRows.Count, ref browseIndex[0], AzRow, _ => Activate(), i => azRows[i].Place is { } p ? MarkerOn(p) : null);
        }
        else if (zone != null)
        {
            DrawList(zone.Places.Count, ref browseIndex[browseDepth], i => (zone.Places[i].Name, null, Coords(zone.Places[i])),
                     i => Flag(zone.Places[i]), i => MarkerOn(zone.Places[i]));
        }
        else if (browseDepth == 1)
        {
            var zones = BrowseZones();
            DrawList(zones.Count, ref browseIndex[browseDepth], i => (zones[i].Name, null, "›"), _ => Drill(),
                     i => (zones[i].Places[0].MapTexture, null));
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
        if (ImGui.InputTextWithHint("##query", "Part of a place or zone name", ref query, 64))
            Refresh();

        if (query.Trim().Length == 0)
            return;
        if (results.Count == 0)
        {
            ImGui.TextColored(Dim, "No matches.");
            return;
        }
        DrawList(results.Count, ref searchIndex, i => (results[i].Name, results[i].Zone, Coords(results[i])), i => Flag(results[i]),
                 i => MarkerOn(results[i]));
    }

    private void DrawList(int count, ref int selected, Func<int, (string Name, string? Detail, string Right)> row,
                          Action<int> activate, Func<int, (string Texture, Vector2? Pixel)?> preview)
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
            DrawMap(map.Texture, map.Pixel, previewSize);
        }
        if (clicked >= 0)
            activate(clicked);
    }

    private void DrawMap(string texture, Vector2? pixel, float size)
    {
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size);
        ImGui.Dummy(new Vector2(size));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddImage(textures.GetFromGame(texture).GetWrapOrEmpty().Handle, min, max);
        if (pixel is not { } at)
            return;

        var center = min + at / 2048f * size;
        var radius = MathF.Max(4f, size / 60f);
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.9f, 0.15f, 0.1f, 1f)));
        drawList.AddCircle(center, radius, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), 0, 2f);
        drawList.AddCircle(center, radius * 2.5f, ImGui.GetColorU32(new Vector4(0.9f, 0.15f, 0.1f, 0.8f)), 0, 2f);
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

    private static (string, Vector2?)? MarkerOn(Place place) => (place.MapTexture, place.Pixel);

    private void HandlePad()
    {
        var now = HeldButtons();
        var pressed = now & ~held;
        held = now;

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
            tab = Tab.Search;
            selectTab = focusSearch = true;
        }
        if (pressed.HasFlag(GamepadButtons.West) && tab == Tab.Browse)
            ToggleBrowseOrder();
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
        switch (tab)
        {
            case Tab.Here:
                hereIndex = Wrap(hereIndex + step, places.In(client.TerritoryType).Count);
                break;
            case Tab.Browse:
                browseIndex[browseDepth] = Wrap(browseIndex[browseDepth] + step, BrowseCount());
                break;
            case Tab.Search:
                searchIndex = Wrap(searchIndex + step, results.Count);
                break;
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
        switch (tab)
        {
            case Tab.Here when places.In(client.TerritoryType) is { Count: > 0 } list:
                Flag(list[Math.Clamp(hereIndex, 0, list.Count - 1)]);
                break;
            case Tab.Browse when config.BrowseByName:
                if (azRows.Count == 0)
                    break;
                var row = Pick(azRows, browseIndex[0]);
                if (row.Place != null)
                    Flag(row.Place);
                else
                    ToggleLetter(row.Letter);
                break;
            case Tab.Browse when BrowseZone() is { } zone:
                Flag(Pick(zone.Places, browseIndex[browseDepth]));
                break;
            case Tab.Browse:
                Drill();
                break;
            case Tab.Search when results.Count > 0:
                Flag(results[Math.Clamp(searchIndex, 0, results.Count - 1)]);
                break;
        }
    }

    private void Back()
    {
        if (tab == Tab.Browse && config.BrowseByName && azRows.Count > 0 &&
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

    // By region: regions, then zones, then places. A–Z: one list of letters and the places under the open ones.
    private Region? BrowseRegion() =>
        !config.BrowseByName && browseDepth > 0 && browseRegions.Count > 0 ? Pick(browseRegions, browseIndex[0]) : null;

    private List<Zone> BrowseZones() => BrowseRegion()?.Zones ?? [];

    private Zone? BrowseZone() =>
        browseDepth > 1 && BrowseZones() is { Count: > 0 } zones ? Pick(zones, browseIndex[1]) : null;

    private int BrowseCount() =>
        config.BrowseByName ? azRows.Count :
        BrowseZone() is { } zone ? zone.Places.Count :
        browseDepth == 1 ? BrowseZones().Count : browseRegions.Count;

    private (string Name, string? Detail, string Right) AzRow(int i)
    {
        var (letter, place) = azRows[i];
        if (place != null)
            return ("    " + place.Name, place.Zone, Coords(place));
        var count = letters.First(l => l.Letter == letter).Places.Count;
        return (letter, null, $"{count}  {(openLetters.Contains(letter) ? "⌄" : "›")}");
    }

    // Keeps the selection on the letter's header.
    private void ToggleLetter(string letter)
    {
        if (!openLetters.Remove(letter))
            openLetters.Add(letter);
        BuildAzRows();
        browseIndex[0] = azRows.FindIndex(r => r.Place == null && r.Letter == letter);
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
                azRows.AddRange(list.Select(p => (letter, (Place?)p)));
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
        browseZones = places.Zones.Where(z => !config.HiddenExpansions.Contains(z.Expansion)).ToList();
        browseRegions = places.Regions
                              .Select(r => r with { Zones = r.Zones.Where(z => !config.HiddenExpansions.Contains(z.Expansion)).ToList() })
                              .Where(r => r.Zones.Count > 0)
                              .ToList();
        letters = places.Search("", browseZones)
                        .GroupBy(p => Places.Letter(p.Name))
                        .Select(g => (g.Key, g.ToList()))
                        .ToList();
        BuildAzRows();
        Refresh();
    }

    private void Refresh()
    {
        results = query.Trim().Length == 0 ? new() : places.Search(query, browseZones);
        searchIndex = 0;
    }

    private void Flag(Place place)
    {
        gameGui.OpenMapWithMapLink(place.TerritoryId, place.MapId, new Vector3(place.World.X, 0, place.World.Y));
        if (config.CloseAfterFlag)
            IsOpen = false;
    }

    private static string Coords(Place place) => $"X {place.MapCoord.X:0.0}  Y {place.MapCoord.Y:0.0}";
}
