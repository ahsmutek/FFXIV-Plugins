using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using IconAtlasClient;

namespace IconAtlas;

internal sealed class AtlasWindow : Window, IDisposable
{
    private enum View { Range, Group, Favorites, Plugins }

    private sealed record PluginIcon(string InternalName, string Name, string Path);

    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Gold = new(0.85f, 0.7f, 0.37f, 1f);
    private const float ListWidth = 260f;
    private const float PreviewSize = 128f;

    private readonly Configuration config;
    private readonly Action save;
    private readonly ITextureProvider textures;
    private readonly IDalamudPluginInterface pluginInterface;

    // Range index → the IDs in it that have an icon file, filled in the background.
    private readonly ConcurrentDictionary<int, uint[]> existing = new();
    private readonly HashSet<int> scanning = new();
    private readonly CancellationTokenSource cancel = new();

    private string query = string.Empty;
    private List<IconRange> shown = Ranges.All.ToList();
    private View view = View.Range;
    private IconRange range;
    private string group;
    private uint? toggleLater;
    private uint selected;
    private bool hq;
    private string goTo = string.Empty;
    private bool scrollToSelected = true;
    private bool scrollToRange = true;
    // Set while another plugin asked for an icon, through the IconAtlas.Pick IPC; Plugins when it takes plugin icons too.
    private (string Token, string Purpose, bool Plugins)? picking;
    private int columns = 1;
    private List<PluginIcon> pluginIcons = [];
    private string? selectedPlugin;

    private string idsKey = string.Empty;
    private uint[] ids = [];
    private int favoritesVersion;

    public AtlasWindow(Configuration config, Action save, ITextureProvider textures, IDalamudPluginInterface pluginInterface)
        : base("Icon Atlas###IconAtlas")
    {
        this.config = config;
        this.save = save;
        this.textures = textures;
        this.pluginInterface = pluginInterface;
        Size = new Vector2(1000, 660);
        SizeCondition = ImGuiCond.FirstUseEver;
        range = Ranges.Find(config.LastId) ?? Ranges.All[0];
        group = range.Group;
        selected = range.Contains(config.LastId) ? config.LastId : range.Start;
    }

    public void Show(uint id)
    {
        IsOpen = true;
        selected = id;
        if (Ranges.Find(id) is { } found)
        {
            view = View.Range;
            range = found;
            group = found.Group;
            Expand(found.Group);
        }
        else if (view == View.Plugins)
            view = View.Range;
        scrollToSelected = scrollToRange = true;
    }

    public event Action<string, uint>? Picked;

    public event Action<string, string>? PickedPlugin;

    public void Pick(string token, string purpose, uint current) => Pick(token, purpose, current, string.Empty, false);

    public void PickWithPlugins(string token, string purpose, uint current, string currentPlugin) =>
        Pick(token, purpose, current, currentPlugin, true);

    private void Pick(string token, string purpose, uint current, string currentPlugin, bool plugins)
    {
        picking = (token, purpose, plugins);
        IsOpen = true;
        if (plugins && currentPlugin.Length > 0 && LoadPluginIcons().Any(p => p.InternalName == currentPlugin))
        {
            view = View.Plugins;
            selectedPlugin = currentPlugin;
            return;
        }
        if (!plugins && view == View.Plugins)
            view = View.Range;
        if (current > 0)
            Show(current);
    }

    public void Search(string text)
    {
        IsOpen = true;
        query = text;
        Filter();
    }

    public override void OnClose()
    {
        picking = null;
        config.LastId = selected;
        save();
    }

    public override void Draw()
    {
        if (view == View.Range)
            Scan(range);
        else if (view == View.Group)
            foreach (var r in InGroup(group))
                Scan(r);
        HandleKeys();

        var scale = ImGuiHelpers.GlobalScale;
        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##ranges", new Vector2(ListWidth * scale, avail.Y), true, ImGuiWindowFlags.NavFlattened))
            DrawSidebar();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginGroup();
        DrawPicking();
        DrawHeader();
        var detailHeight = PreviewSize * scale + ImGui.GetStyle().WindowPadding.Y * 2;
        var gridHeight = ImGui.GetContentRegionAvail().Y - detailHeight - ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("##grid", new Vector2(0, gridHeight), true, ImGuiWindowFlags.NavFlattened))
        {
            if (view == View.Plugins)
                DrawPluginGrid();
            else
                DrawGrid();
        }
        ImGui.EndChild();
        if (ImGui.BeginChild("##detail", Vector2.Zero, true, ImGuiWindowFlags.NavFlattened))
        {
            if (view == View.Plugins)
                DrawPluginDetail();
            else
                DrawDetail();
        }
        ImGui.EndChild();
        ImGui.EndGroup();

        // Toggled after drawing, since removing a favorite shrinks the list the Favorites grid is drawing.
        if (toggleLater is { } id)
        {
            ToggleFavorite(id);
            toggleLater = null;
        }
    }

    private void DrawSidebar()
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##query", "Search, e.g. dancer, waymarks, 61241", ref query, 64))
            Filter();

        // Without its own padding the list's highlight would start at the clipped edge of the child.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0));
        var open = ImGui.BeginChild("##list", Vector2.Zero, false, ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NavFlattened);
        ImGui.PopStyleVar();
        if (!open)
        {
            ImGui.EndChild();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, Gold);
        if (ImGui.Selectable("Favorites##favorites", view == View.Favorites))
            ShowFavorites();
        if (ImGui.Selectable("Plugins##plugins", view == View.Plugins))
            ShowPlugins();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Plugins' own icons, for the plugins on this PC that have one");

        var searching = query.Trim().Length > 0;
        if (!searching && (config.PinnedGroups.Count > 0 || config.PinnedRanges.Count > 0))
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "Pinned");
            foreach (var g in config.PinnedGroups.ToList())
                GroupEntry(g, $"##pg{g}");
            foreach (var start in config.PinnedRanges.ToList())
                if (Ranges.Find(start) is { } r)
                    RangeEntry(r, $"##pr{r.Index}", true);
            ImGui.Separator();
        }

        if (shown.Count == 0)
            ImGui.TextColored(Dim, "No range matches.");

        foreach (var g in Ranges.Groups)
        {
            var matches = shown.Where(r => r.Group == g).ToList();
            if (matches.Count == 0)
                continue;

            var expanded = searching || config.ExpandedGroups.Contains(g);
            ImGui.SetNextItemOpen(expanded);
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick |
                        ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.NoTreePushOnOpen;
            if (view == View.Group && group == g)
                flags |= ImGuiTreeNodeFlags.Selected;
            ImGui.PushStyleColor(ImGuiCol.Text, Gold);
            var nowOpen = ImGui.TreeNodeEx($"{g}##g{g}", flags);
            ImGui.PopStyleColor();
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                ShowGroup(g);
            if (!searching && nowOpen != expanded)
            {
                if (nowOpen)
                    config.ExpandedGroups.Add(g);
                else
                    config.ExpandedGroups.Remove(g);
                save();
            }
            GroupMenu(g, $"##gm{g}");
            if (!nowOpen)
                continue;

            ImGui.Indent();
            foreach (var r in matches)
                RangeEntry(r, $"##r{r.Index}", false);
            ImGui.Unindent();
        }
        ImGui.EndChild();
    }

    private void GroupEntry(string g, string id)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Gold);
        if (ImGui.Selectable($"{g}{id}", view == View.Group && group == g))
            ShowGroup(g);
        ImGui.PopStyleColor();
        GroupMenu(g, $"{id}m");
    }

    private void GroupMenu(string g, string id)
    {
        if (!PadNav.ContextMenu(id))
            return;
        if (ImGui.MenuItem("Show all icons"))
            ShowGroup(g);
        var pinned = config.PinnedGroups.Contains(g);
        if (ImGui.MenuItem(pinned ? "Unpin" : "Pin to top"))
        {
            if (pinned)
                config.PinnedGroups.Remove(g);
            else
                config.PinnedGroups.Add(g);
            save();
        }
        ImGui.EndPopup();
    }

    private void RangeEntry(IconRange r, string id, bool inPinned)
    {
        var current = view == View.Range && r == range;
        if (ImGui.Selectable($"{r.Description}{id}", current))
            Select(r);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(inPinned ? $"{r.Group} · {r.Ids}" : r.Ids);
        if (current && scrollToRange && !inPinned)
        {
            ImGui.SetScrollHereY();
            scrollToRange = false;
        }

        if (!PadNav.ContextMenu($"{id}m"))
            return;
        var pinned = config.PinnedRanges.Contains(r.Start);
        if (ImGui.MenuItem(pinned ? "Unpin" : "Pin to top"))
        {
            if (pinned)
                config.PinnedRanges.Remove(r.Start);
            else
                config.PinnedRanges.Add(r.Start);
            save();
        }
        ImGui.EndPopup();
    }

    private bool IsFavorite(uint id) => config.Favorites.BinarySearch(id) >= 0;

    private void ToggleFavorite(uint id)
    {
        var index = config.Favorites.BinarySearch(id);
        if (index >= 0)
            config.Favorites.RemoveAt(index);
        else
            config.Favorites.Insert(~index, id);
        favoritesVersion++;
        save();
    }

    private void DrawPicking()
    {
        if (picking is not { } pick)
            return;
        ImGui.TextColored(Gold, $"Choosing an icon for {pick.Purpose}");
        ImGui.SameLine();
        var plugins = view == View.Plugins;
        ImGui.BeginDisabled(plugins
                                ? !pick.Plugins || SelectedPluginIcon() == null
                                : !textures.TryGetIconPath(new GameIconLookup(selected), out _));
        if (ImGui.Button("Use this icon"))
        {
            if (plugins)
                UsePlugin(selectedPlugin!);
            else
                Use(selected);
        }
        ImGui.EndDisabled();
        if (plugins && !pick.Plugins && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Only game icons can be used for {pick.Purpose}.");
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            IsOpen = false;
        ImGui.Separator();
    }

    private void Use(uint id)
    {
        if (picking is not { } pick || !textures.TryGetIconPath(new GameIconLookup(id), out _))
            return;
        Picked?.Invoke(pick.Token, id);
        IsOpen = false;
    }

    private void UsePlugin(string internalName)
    {
        if (picking is not { Plugins: true } pick)
            return;
        PickedPlugin?.Invoke(pick.Token, internalName);
        IsOpen = false;
    }

    private void DrawHeader()
    {
        switch (view)
        {
            case View.Favorites:
                ImGui.TextUnformatted("Favorites");
                ImGui.TextColored(Dim, config.Favorites.Count == 0
                                           ? "Right-click an icon, or use Favorite below it, to add it here."
                                           : $"{config.Favorites.Count:N0} icons · right-click one to remove it");
                break;
            case View.Plugins:
                ImGui.TextUnformatted("Plugins");
                ImGui.TextColored(Dim, $"{pluginIcons.Count} with an icon on this PC");
                break;
            case View.Group:
                var inGroup = InGroup(group);
                var checking = config.OnlyExisting && inGroup.Any(r => !existing.ContainsKey(r.Index));
                ImGui.TextUnformatted(group);
                ImGui.TextColored(Dim, $"{inGroup.Count} ranges · {Ids().Length:N0} {(config.OnlyExisting ? "icons" : "IDs")}" +
                                       (checking ? ", checking which exist…" : string.Empty));
                break;
            default:
                ImGui.TextUnformatted(range.Description);
                var count = existing.TryGetValue(range.Index, out var found)
                                ? $"{found.Length:N0} icons in {range.Count:N0} IDs"
                                : $"{range.Count:N0} IDs, checking which exist…";
                ImGui.TextColored(Dim, $"{range.Group} · {range.Ids} · {count}");
                break;
        }

        if (view != View.Plugins)
        {
            var onlyExisting = config.OnlyExisting;
            if (ImGui.Checkbox("Only IDs with an icon", ref onlyExisting))
            {
                config.OnlyExisting = onlyExisting;
                scrollToSelected = true;
                save();
            }
            ImGui.SameLine();
        }

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(120 * scale);
        var size = config.TileSize;
        if (ImGui.SliderInt("Size", ref size, 32, 96))
        {
            config.TileSize = size;
            scrollToSelected = true;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
            save();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110 * scale);
        if (ImGui.InputTextWithHint("##goto", "Go to ID", ref goTo, 7,
                                    ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue) &&
            uint.TryParse(goTo, out var id))
            Show(id);
    }

    private void DrawGrid()
    {
        var visible = Ids();
        if (visible.Length == 0)
        {
            ImGui.TextColored(Dim, view == View.Favorites ? "No favorites yet." : "No icons here.");
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var tile = config.TileSize * scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var step = tile + spacing;
        columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing) / step));
        var rows = (visible.Length + columns - 1) / columns;

        if (scrollToSelected)
        {
            var index = Array.BinarySearch(visible, selected);
            if (index >= 0)
                ImGui.SetScrollY(index / columns * step - (ImGui.GetWindowHeight() - step) / 2);
            scrollToSelected = false;
        }

        // Only the rows in view are drawn, plus one either side for the pad to move onto; a view can hold tens of
        // thousands of IDs.
        var origin = ImGui.GetCursorPos();
        var firstRow = Math.Max(0, (int)((ImGui.GetScrollY() - origin.Y) / step) - 1);
        var lastRow = Math.Min(rows, firstRow + (int)(ImGui.GetWindowHeight() / step) + 3);
        var drawList = ImGui.GetWindowDrawList();
        for (var row = firstRow; row < lastRow; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var k = row * columns + col;
                if (k >= visible.Length)
                    break;
                ImGui.SetCursorPos(origin + new Vector2(col * step, row * step));
                DrawTile(drawList, visible[k], tile);
            }
        }
        ImGui.SetCursorPos(origin + new Vector2(0, rows * step));
        ImGui.Dummy(Vector2.Zero);
    }

    private void DrawTile(ImDrawListPtr drawList, uint id, float size)
    {
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size);
        var tileId = ImGui.GetID($"##t{id}");
        if (ImGui.InvisibleButton($"##t{id}", new Vector2(size)) || ImGui.GetCurrentContext().NavJustMovedToId == tileId)
            selected = id;
        var focused = ImGui.IsItemFocused();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) || (focused && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false)))
            toggleLater = id;
        if (picking != null && ((ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) ||
                                (focused && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceLeft, false))))
            Use(id);
        var hovered = ImGui.IsItemHovered();

        var rounding = ImGui.GetStyle().FrameRounding;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg),
                               rounding);
        var wrap = Wrap(id, false);
        if (wrap != null)
            DrawFitted(drawList, wrap, min + new Vector2(3), max - new Vector2(3));
        else
        {
            var label = $"{id % 1000:D3}";
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(min + (new Vector2(size) - textSize) / 2, ImGui.GetColorU32(Dim), label);
        }
        var favorite = IsFavorite(id);
        if (favorite)
        {
            var radius = Math.Max(3f, size / 12);
            drawList.AddCircleFilled(new Vector2(max.X - radius - 2, min.Y + radius + 2), radius, ImGui.GetColorU32(Gold));
        }
        if (id == selected)
            drawList.AddRect(min, max, ImGui.GetColorU32(Gold), rounding, ImDrawFlags.None, 2f);

        if (!hovered)
            return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted($"{id:D6}");
        if (view != View.Range && Ranges.Find(id) is { } described)
            ImGui.TextColored(Dim, described.Description);
        if (wrap != null)
            TooltipPreview(wrap);
        else
            ImGui.TextColored(Dim, "No icon");
        var pad = PadNav.Navigating;
        if (picking != null)
            ImGui.TextColored(Dim, pad ? "X to use" : "Double-click to use");
        ImGui.TextColored(Dim, (pad ? "Y to " : "Right-click to ") + (favorite ? "unfavorite" : "favorite"));
        ImGui.EndTooltip();
    }

    private void DrawPluginGrid()
    {
        if (pluginIcons.Count == 0)
        {
            ImGui.TextColored(Dim, "No plugin has its icon on this PC. Dev plugins keep theirs at images/icon.png beside the DLL.");
            return;
        }

        var tile = config.TileSize * ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing) / (tile + spacing)));
        var drawList = ImGui.GetWindowDrawList();
        for (var i = 0; i < pluginIcons.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine();
            DrawPluginTile(drawList, pluginIcons[i], tile);
        }
    }

    private void DrawPluginTile(ImDrawListPtr drawList, PluginIcon plugin, float size)
    {
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size);
        var tileId = ImGui.GetID($"##p{plugin.InternalName}");
        if (ImGui.InvisibleButton($"##p{plugin.InternalName}", new Vector2(size)) ||
            ImGui.GetCurrentContext().NavJustMovedToId == tileId)
            selectedPlugin = plugin.InternalName;
        var usable = picking is { Plugins: true };
        if (usable && ((ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) ||
                       (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceLeft, false))))
            UsePlugin(plugin.InternalName);
        var hovered = ImGui.IsItemHovered();

        var rounding = ImGui.GetStyle().FrameRounding;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg),
                               rounding);
        var wrap = PluginWrap(plugin);
        if (wrap != null)
            DrawFitted(drawList, wrap, min + new Vector2(3), max - new Vector2(3));
        if (plugin.InternalName == selectedPlugin)
            drawList.AddRect(min, max, ImGui.GetColorU32(Gold), rounding, ImDrawFlags.None, 2f);

        if (!hovered)
            return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(plugin.Name);
        if (wrap != null)
            TooltipPreview(wrap);
        if (usable)
            ImGui.TextColored(Dim, PadNav.Navigating ? "X to use" : "Double-click to use");
        ImGui.EndTooltip();
    }

    private void DrawPluginDetail()
    {
        var plugin = SelectedPluginIcon();
        var wrap = plugin != null ? PluginWrap(plugin) : null;
        DrawPreview(wrap);
        if (plugin == null)
            return;

        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(plugin.Name);
        ImGui.SameLine();
        ImGui.TextColored(Dim, plugin.InternalName);
        ImGui.TextColored(Dim, "Path");
        ImGui.SameLine();
        ImGui.TextUnformatted(plugin.Path);
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy path"))
            ImGui.SetClipboardText(plugin.Path);
        ImGui.TextColored(Dim, "Size");
        ImGui.SameLine();
        ImGui.TextUnformatted(wrap != null ? $"{wrap.Width}×{wrap.Height}" : "–");
        ImGui.EndGroup();
    }

    private void DrawDetail()
    {
        var wrap = Wrap(selected, hq);
        DrawPreview(wrap);

        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted($"{selected:D6}");
        ImGui.SameLine();
        var described = Ranges.Find(selected);
        ImGui.TextColored(Dim, described == null ? "Not in any described range" : $"{described.Description} · {described.Group}");

        var path = textures.TryGetIconPath(new GameIconLookup(selected, hq), out var resolved) ? resolved : null;
        ImGui.TextColored(Dim, "Path");
        ImGui.SameLine();
        ImGui.TextUnformatted(path ?? $"{GamePath(selected)} (no such file)");
        if (path != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy path"))
                ImGui.SetClipboardText(path);
        }
        ImGui.TextColored(Dim, "Size");
        ImGui.SameLine();
        ImGui.TextUnformatted(wrap != null ? $"{wrap.Width}×{wrap.Height}" : "–");

        if (ImGui.Button(IsFavorite(selected) ? "Unfavorite" : "Favorite"))
            ToggleFavorite(selected);
        ImGui.SameLine();
        if (ImGui.Button("Copy ID"))
            ImGui.SetClipboardText(selected.ToString());
        ImGui.SameLine();
        if (ImGui.Button("← Prev"))
            Step(-1);
        ImGui.SameLine();
        if (ImGui.Button("Next →"))
            Step(1);
        ImGui.SameLine();
        ImGui.Checkbox("HQ", ref hq);
        ImGui.EndGroup();
    }

    private void HandleKeys()
    {
        // While navigating, the arrows move between tiles and the other controls instead.
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.GetIO().WantTextInput ||
            PadNav.Navigating)
            return;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
            Step(-1);
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
            Step(1);
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            Step(-columns);
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            Step(columns);
    }

    private void Step(int by)
    {
        if (view == View.Plugins)
        {
            StepPlugin(by);
            return;
        }

        var visible = Ids();
        if (visible.Length == 0)
            return;
        var index = Array.BinarySearch(visible, selected);
        selected = visible[Math.Clamp(index < 0 ? 0 : index + by, 0, visible.Length - 1)];
        scrollToSelected = true;
    }

    private void StepPlugin(int by)
    {
        if (pluginIcons.Count == 0)
            return;
        var index = pluginIcons.FindIndex(p => p.InternalName == selectedPlugin);
        selectedPlugin = pluginIcons[Math.Clamp(index < 0 ? 0 : index + by, 0, pluginIcons.Count - 1)].InternalName;
    }

    private void Select(IconRange r)
    {
        view = View.Range;
        range = r;
        group = r.Group;
        selected = uint.TryParse(query.Trim(), out var id) && r.Contains(id) ? id : FirstOf(r);
        scrollToSelected = true;
    }

    private void ShowGroup(string g)
    {
        view = View.Group;
        group = g;
        var visible = Ids();
        if (visible.Length > 0 && Array.BinarySearch(visible, selected) < 0)
            selected = visible[0];
        scrollToSelected = true;
    }

    private void ShowFavorites()
    {
        view = View.Favorites;
        if (config.Favorites.Count > 0 && !IsFavorite(selected))
            selected = config.Favorites[0];
        scrollToSelected = true;
    }

    private void ShowPlugins()
    {
        view = View.Plugins;
        LoadPluginIcons();
        if (SelectedPluginIcon() == null)
            selectedPlugin = pluginIcons.FirstOrDefault()?.InternalName;
    }

    // Read again whenever it's shown, since plugins come and go; finding their files is too slow for every frame.
    private List<PluginIcon> LoadPluginIcons() =>
        pluginIcons = pluginInterface.InstalledPlugins
                                     .Select(p => PluginIconFile.Of(p) is { } path ? new PluginIcon(p.InternalName, p.Name, path) : null)
                                     .OfType<PluginIcon>()
                                     .DistinctBy(p => p.InternalName)
                                     .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

    private PluginIcon? SelectedPluginIcon() => pluginIcons.FirstOrDefault(p => p.InternalName == selectedPlugin);

    private void Expand(string g)
    {
        if (config.ExpandedGroups.Add(g))
            save();
    }

    private uint FirstOf(IconRange r) =>
        config.OnlyExisting && existing.TryGetValue(r.Index, out var found) && found.Length > 0 ? found[0] : r.Start;

    private void Filter()
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0)
            shown = Ranges.All.ToList();
        else if (uint.TryParse(q, out var id))
            shown = Ranges.All.Where(r => r.Contains(id)).ToList();
        else
        {
            var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = Ranges.All.Where(r => terms.All(t => $"{r.Description} {r.Group}".Contains(t, StringComparison.OrdinalIgnoreCase)))
                          .ToList();
        }
        scrollToRange = true;
    }

    private static List<IconRange> InGroup(string g) => Ranges.All.Where(r => r.Group == g).OrderBy(r => r.Start).ToList();

    // Sorted by ID; rebuilt only when the view, the filter or a background scan changes it.
    private uint[] Ids()
    {
        var key = view switch
        {
            View.Favorites => $"f{favoritesVersion}",
            View.Group => $"g{group}|{config.OnlyExisting}|{existing.Count}",
            _ => $"r{range.Index}|{config.OnlyExisting}|{existing.ContainsKey(range.Index)}",
        };
        if (key == idsKey)
            return ids;

        idsKey = key;
        ids = view switch
        {
            View.Favorites => config.Favorites.ToArray(),
            View.Group => InGroup(group).SelectMany(IdsIn).ToArray(),
            _ => IdsIn(range).ToArray(),
        };
        return ids;
    }

    private IEnumerable<uint> IdsIn(IconRange r) =>
        config.OnlyExisting && existing.TryGetValue(r.Index, out var found)
            ? found
            : Enumerable.Range(0, r.Count).Select(k => r.Start + (uint)k);

    private void Scan(IconRange r)
    {
        if (existing.ContainsKey(r.Index) || !scanning.Add(r.Index))
            return;
        var token = cancel.Token;
        Task.Run(() =>
        {
            var found = new List<uint>();
            for (var id = r.Start; id <= r.End; id++)
            {
                if (token.IsCancellationRequested)
                    return;
                if (textures.TryGetIconPath(new GameIconLookup(id), out _))
                    found.Add(id);
            }
            existing[r.Index] = found.ToArray();
        }, token);
    }

    private IDalamudTextureWrap? Wrap(uint id, bool itemHq) =>
        textures.TryGetFromGameIcon(new GameIconLookup(id, itemHq), out var texture) ? texture.GetWrapOrDefault() : null;

    private IDalamudTextureWrap? PluginWrap(PluginIcon plugin) => textures.GetFromFile(plugin.Path).GetWrapOrDefault();

    private static void DrawPreview(IDalamudTextureWrap? wrap)
    {
        var preview = PreviewSize * ImGuiHelpers.GlobalScale - ImGui.GetStyle().WindowPadding.Y * 2;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(preview);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), ImGui.GetStyle().FrameRounding);
        if (wrap != null)
            DrawFitted(drawList, wrap, min + new Vector2(4), max - new Vector2(4));
        ImGui.Dummy(new Vector2(preview));
    }

    private static void TooltipPreview(IDalamudTextureWrap wrap)
    {
        var preview = PreviewSize * ImGuiHelpers.GlobalScale;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(preview));
        DrawFitted(ImGui.GetWindowDrawList(), wrap, pos, pos + new Vector2(preview));
        ImGui.TextColored(Dim, $"{wrap.Width}×{wrap.Height}");
    }

    private static void DrawFitted(ImDrawListPtr drawList, IDalamudTextureWrap wrap, Vector2 min, Vector2 max)
    {
        var box = max - min;
        var fit = Math.Min(box.X / wrap.Width, box.Y / wrap.Height);
        var size = new Vector2(wrap.Width, wrap.Height) * fit;
        var at = min + (box - size) / 2;
        drawList.AddImage(wrap.Handle, at, at + size);
    }

    private static string GamePath(uint id) => $"ui/icon/{id / 1000 * 1000:D6}/{id:D6}.tex";

    public void Dispose() => cancel.Cancel();
}
