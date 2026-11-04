using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Icons;
using Item = Lumina.Excel.Sheets.Item;

namespace GlamKit;

internal sealed class KeeperWindow : Window
{
    private readonly Configuration config;
    private readonly Action save;
    private readonly Locations locations;
    private readonly IDataManager data;
    private readonly ITextureProvider textures;
    private readonly NativeTooltip tooltip;
    private readonly Action openSettings;

    private Vector2 windowMin;
    private Vector2 windowMax;
    private string search = "";
    private List<(uint Id, string Name)>? allItems;

    public KeeperWindow(Configuration config, Action save, Locations locations, IDataManager data,
                        ITextureProvider textures, NativeTooltip tooltip, Action openSettings)
        : base("Glam Keeper###GlamKitKeeper")
    {
        this.config = config;
        this.save = save;
        this.locations = locations;
        this.data = data;
        this.textures = textures;
        this.tooltip = tooltip;
        this.openSettings = openSettings;
        Size = new Vector2(340, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(240, 140) };
    }

    public override void Draw()
    {
        windowMin = ImGui.GetWindowPos();
        windowMax = windowMin + ImGui.GetWindowSize();

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        var off = (string?)null;
        using (var tabs = ImRaii.TabBar("##tabs"))
        {
            if (tabs)
            {
                if (DrawTab("For Glam", config.Items, Hint("Keep for Glam"), "Stop keeping for glam", footer, Where) &&
                    !config.GlamKeeperEnabled)
                    off = "Glam Keeper is off in settings.";
                if (DrawTab("For Later", config.LaterItems, Hint("Keep for Later"), "Stop keeping for later", footer, Where) &&
                    !config.KeepForLaterEnabled)
                    off = "Keep for Later is off in settings.";
                if (DrawTab("Wishlist", config.WishItems,
                            "Nothing wished for. Search above, or open the item menu in a shop, the Market Board or on an item link and choose Add to Wishlist.",
                            "Remove from wishlist", footer, Added, DrawSearch) &&
                    !config.WishlistEnabled)
                    off = "Wishlist is off in settings.";
            }
        }

        ImGui.Separator();
        if (off != null)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(off);
            ImGui.SameLine();
        }
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.GetFrameHeight());
        if (TablerIcons.Button(TablerIcon.Settings))
            openSettings();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Settings");
    }

    // Returns whether the tab is the one shown.
    private bool DrawTab(string label, List<KeptItem> items, string empty, string removeTip, float footer,
                         Func<KeptItem, string> detail, Action? header = null)
    {
        using var tab = ImRaii.TabItem($"{label} ({items.Count})###{label}");
        if (!tab)
            return false;
        header?.Invoke();
        using var list = ImRaii.Child("##list", new Vector2(0, -footer));
        if (list)
            DrawList(items, empty, removeTip, detail);
        return true;
    }

    private void DrawList(List<KeptItem> items, string empty, string removeTip, Func<KeptItem, string> detail)
    {
        if (items.Count == 0)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextDisabled(empty);
            ImGui.PopTextWrapPos();
            return;
        }

        KeptItem? remove = null;
        foreach (var kept in items.OrderBy(i => Name(i.ItemId)))
        {
            if (DrawRow(kept, removeTip, detail))
                remove = kept;
        }
        if (remove != null)
        {
            items.Remove(remove);
            save();
        }
    }

    private bool DrawRow(KeptItem kept, string removeTip, Func<KeptItem, string> detail)
    {
        using var id = ImRaii.PushId((int)kept.ItemId);
        var iconSize = new Vector2(ImGui.GetFrameHeight() * 1.6f);
        var row = data.GetExcelSheet<Item>().GetRowOrDefault(kept.ItemId);

        ImGui.Image(textures.GetFromGameIcon(new GameIconLookup(row?.Icon ?? 0)).GetWrapOrEmpty().Handle, iconSize);
        if (ImGui.IsItemHovered())
            tooltip.Hover(kept.ItemId, windowMin, windowMax);
        ImGui.SameLine();

        var removeWidth = ImGui.GetFrameHeight();
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted(Name(kept.ItemId));
            if (ImGui.IsItemHovered())
                tooltip.Hover(kept.ItemId, windowMin, windowMax);
            ImGui.TextDisabled(detail(kept));
        }

        ImGui.SameLine(ImGui.GetContentRegionMax().X - removeWidth);
        var removed = TablerIcons.Button(TablerIcon.X);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(removeTip);
        return removed;
    }

    private static string Hint(string menuEntry) => $"Nothing kept. Open the item menu on gear and choose {menuEntry}.";

    private string Where(KeptItem kept) => locations.Of(kept.ItemId);

    private static string Added(KeptItem kept) => $"Added {kept.Added.ToLocalTime():d MMM yyyy}";

    private void DrawSearch()
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Add an item by name", ref search, 100);
        var query = search.Trim();
        if (query.Length < 3)
            return;

        allItems ??= data.GetExcelSheet<Item>()
                         .Where(r => !r.Name.IsEmpty)
                         .Select(r => (r.RowId, r.Name.ExtractText()))
                         .OrderBy(r => r.Item2)
                         .ToList();
        foreach (var (id, name) in allItems.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
        {
            var wished = config.IsWished(id);
            if (ImGui.Selectable($"{name}{(wished ? "  (on your wishlist)" : "")}##{id}", false,
                                 wished ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
            {
                config.WishItems.Add(new KeptItem { ItemId = id });
                save();
                search = "";
            }
        }
        ImGui.Separator();
    }

    private string Name(uint itemId) => data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ExtractText() ?? $"Item {itemId}";
}
