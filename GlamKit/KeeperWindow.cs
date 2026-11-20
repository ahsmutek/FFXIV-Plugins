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
using ClassJob = Lumina.Excel.Sheets.ClassJob;
using Item = Lumina.Excel.Sheets.Item;

namespace GlamKit;

internal sealed class KeeperWindow : Window
{
    private static readonly (UpgradeGrouping Grouping, string Name)[] Groupings =
    [
        (UpgradeGrouping.Job, "Job"), (UpgradeGrouping.Slot, "Slot"), (UpgradeGrouping.Place, "Dresser or armoire"),
        (UpgradeGrouping.None, "Nothing"),
    ];

    private readonly Configuration config;
    private readonly Action save;
    private readonly Locations locations;
    private readonly Wardrobe wardrobe;
    private readonly ItemMenu itemMenu;
    private readonly IDataManager data;
    private readonly ITextureProvider textures;
    private readonly NativeTooltip tooltip;
    private readonly Action openSettings;

    private Vector2 windowMin;
    private Vector2 windowMax;
    private bool showHidden;
    private string search = "";
    private List<(uint Id, string Name)>? allItems;

    public KeeperWindow(Configuration config, Action save, Locations locations, Wardrobe wardrobe, ItemMenu itemMenu,
                        IDataManager data, ITextureProvider textures, NativeTooltip tooltip, Action openSettings)
        : base("Glam Keeper###GlamKitKeeper")
    {
        this.config = config;
        this.save = save;
        this.locations = locations;
        this.wardrobe = wardrobe;
        this.itemMenu = itemMenu;
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
        if (itemMenu.TakeFocusBack())
            ImGui.SetWindowFocus();
        windowMin = ImGui.GetWindowPos();
        windowMax = windowMin + ImGui.GetWindowSize();

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        var off = (string?)null;
        using (var tabs = ImRaii.TabBar("##tabs"))
        {
            if (tabs)
            {
                if (DrawTab("For Glam", config.Items.Count, footer,
                            () => DrawList(config.Items, Hint("Keep for Glam"), "Stop keeping for glam", Where)) &&
                    !config.GlamKeeperEnabled)
                    off = "Glam Keeper is off in settings.";
                if (DrawTab("For Later", config.LaterItems.Count, footer,
                            () => DrawList(config.LaterItems, Hint("Keep for Later"), "Stop keeping for later", Where)) &&
                    !config.KeepForLaterEnabled)
                    off = "Keep for Later is off in settings.";
                if (DrawTab("Wishlist", config.WishItems.Count, footer,
                            () => DrawList(config.WishItems,
                                           "Nothing wished for. Search above, or open the item menu in a shop, the Market Board or on an item link and choose Add to Wishlist.",
                                           "Remove from wishlist", Added),
                            DrawSearch) &&
                    !config.WishlistEnabled)
                    off = "Wishlist is off in settings.";
                var all = wardrobe.Upgrades();
                var upgrades = Shown(all);
                if (DrawTab("Upgrades", upgrades.Count, footer, () => DrawUpgrades(upgrades, all.Count),
                            all.Count > 0 ? () => DrawUpgradesHeader(all) : null) &&
                    !config.MarkUpgrades)
                    off = "Marking upgrades is off in settings.";
            }
        }
        itemMenu.DrawFallback();

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
    private static bool DrawTab(string label, int count, float footer, Action body, Action? header = null)
    {
        using var tab = ImRaii.TabItem($"{label} ({count})###{label}");
        if (!tab)
            return false;
        header?.Invoke();
        using var list = ImRaii.Child("##list", new Vector2(0, -footer));
        if (list)
            body();
        return true;
    }

    private void DrawList(List<KeptItem> items, string empty, string removeTip, Func<KeptItem, string> detail)
    {
        if (items.Count == 0)
        {
            DrawEmpty(empty);
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
        DrawItem(kept.ItemId, detail(kept));

        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
        var removed = TablerIcons.Button(TablerIcon.X);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(removeTip);
        return removed;
    }

    // Each upgrade with only the jobs to show, the current job first.
    private List<Wardrobe.Upgrade> Shown(List<Wardrobe.Upgrade> upgrades)
    {
        var current = wardrobe.CurrentJob;
        return upgrades.Select(u => u with
                       {
                           Jobs = u.Jobs.Where(j => showHidden || !Hidden(u, j)).OrderBy(j => !IsJob(j, current)).ToList(),
                       })
                       .Where(u => u.Jobs.Count > 0)
                       .ToList();
    }

    private void DrawUpgrades(List<Wardrobe.Upgrade> upgrades, int total)
    {
        var missing = (wardrobe.DresserKnown, wardrobe.ArmoireKnown) switch
        {
            (false, false) => "glamour dresser and armoire",
            (false, true) => "glamour dresser",
            (true, false) => "armoire",
            _ => null,
        };
        if (upgrades.Count == 0)
        {
            DrawEmpty((wardrobe.DresserKnown, wardrobe.ArmoireKnown) switch
            {
                _ when total > 0 => "Everything here is marked as not an upgrade. Tick Show hidden to see it.",
                (false, false) => "Open the glamour dresser and armoire once and GlamKit will remember what's in them.",
                (true, true) => "Nothing in your glamour dresser or armoire is an upgrade for your gear sets.",
                _ => $"Nothing in your {(wardrobe.DresserKnown ? "glamour dresser" : "armoire")} is an upgrade for your " +
                     $"gear sets. Open the {missing} once to include it.",
            });
            return;
        }
        if (missing != null)
            DrawEmpty($"Open the {missing} once to include it.");

        switch (config.UpgradeGrouping)
        {
            case UpgradeGrouping.Job:
                var current = wardrobe.CurrentJob;
                foreach (var group in upgrades.SelectMany(u => u.Jobs, (u, j) => (Upgrade: u, Job: j))
                                              .GroupBy(p => p.Job.Id)
                                              .OrderBy(g => IsJob(g.First().Job, current) ? -1 : JobOrder(g.Key)))
                {
                    var job = group.First().Job;
                    DrawUpgradeGroup($"{job.Abbreviation} {job.Level}", group.Select(p => p.Upgrade),
                                     u => $"{Places(u)} · {SettingsWindow.SlotName(u.Slot)}{(Hidden(u, job) ? " · hidden" : "")}",
                                     u => [NotUpgradeEntry(u.ItemId, job)]);
                }
                break;
            case UpgradeGrouping.Slot:
                foreach (var group in upgrades.GroupBy(u => u.Slot).OrderBy(g => g.Key))
                    DrawUpgradeGroup(SettingsWindow.SlotName(group.Key), group, u => $"{Places(u)} · {Jobs(u)}", NotUpgradeEntries);
                break;
            case UpgradeGrouping.Place:
                foreach (var group in upgrades.SelectMany(u => u.Places, (u, p) => (Upgrade: u, Place: p))
                                              .GroupBy(p => p.Place)
                                              .OrderBy(g => g.Key == Wardrobe.Armoire))
                    DrawUpgradeGroup(group.Key, group.Select(p => p.Upgrade), Jobs, NotUpgradeEntries);
                break;
            default:
                DrawUpgradeRows(upgrades, u => $"{Places(u)} · {Jobs(u)}", NotUpgradeEntries);
                break;
        }
    }

    private void DrawUpgradeGroup(string header, IEnumerable<Wardrobe.Upgrade> upgrades, Func<Wardrobe.Upgrade, string> detail,
                                  Func<Wardrobe.Upgrade, List<(string, Action)>> menuEntries)
    {
        using var id = ImRaii.PushId(header);
        if (ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
            DrawUpgradeRows(upgrades, detail, menuEntries);
    }

    private void DrawUpgradeRows(IEnumerable<Wardrobe.Upgrade> upgrades, Func<Wardrobe.Upgrade, string> detail,
                                 Func<Wardrobe.Upgrade, List<(string, Action)>> menuEntries)
    {
        foreach (var upgrade in upgrades.OrderBy(u => u.Slot).ThenBy(u => Name(u.ItemId)))
        {
            using var id = ImRaii.PushId((int)upgrade.ItemId);
            DrawItem(upgrade.ItemId, detail(upgrade), () => menuEntries(upgrade));
        }
    }

    private List<(string, Action)> NotUpgradeEntries(Wardrobe.Upgrade upgrade) =>
        upgrade.Jobs.Select(j => NotUpgradeEntry(upgrade.ItemId, j)).ToList();

    private (string, Action) NotUpgradeEntry(uint itemId, JobLevels.CompareJob job)
    {
        var hidden = config.IsNotUpgrade(itemId, job.Id);
        return (hidden ? $"Show as Upgrade for {job.Abbreviation}" : $"Not an Upgrade for {job.Abbreviation}", () =>
        {
            config.SetNotUpgrade(itemId, job.Id, !hidden);
            save();
        });
    }

    private bool Hidden(Wardrobe.Upgrade upgrade, JobLevels.CompareJob job) => config.IsNotUpgrade(upgrade.ItemId, job.Id);

    // On a class, gear its job can also wear is listed under the job.
    private static bool IsJob(JobLevels.CompareJob job, uint classJobId) =>
        classJobId != 0 && (job.Id == classJobId || job.ClassId == classJobId);

    private string Jobs(Wardrobe.Upgrade upgrade) =>
        string.Join("  ", upgrade.Jobs.Select(j => Hidden(upgrade, j) ? $"{j.Abbreviation} {j.Level} (hidden)"
                                                                       : $"{j.Abbreviation} {j.Level}▲"));

    private static string Places(Wardrobe.Upgrade upgrade) => string.Join(", ", upgrade.Places);

    private int JobOrder(uint jobId) =>
        data.GetExcelSheet<ClassJob>().GetRowOrDefault(jobId)?.UIPriority ?? byte.MaxValue;

    private void DrawUpgradesHeader(List<Wardrobe.Upgrade> all)
    {
        DrawGrouping();
        var hidden = all.Sum(u => u.Jobs.Count(j => Hidden(u, j)));
        if (hidden == 0 && !showHidden)
            return;
        ImGui.SameLine();
        ImGui.Checkbox($"Show hidden ({hidden})", ref showHidden);
    }

    private void DrawGrouping()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Group by");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Groupings.Max(g => ImGui.CalcTextSize(g.Name).X) + ImGui.GetFrameHeight() +
                               ImGui.GetStyle().FramePadding.X * 2);
        var current = Groupings.FirstOrDefault(g => g.Grouping == config.UpgradeGrouping).Name ?? Groupings[0].Name;
        using var combo = ImRaii.Combo("##grouping", current);
        if (!combo)
            return;
        foreach (var (grouping, name) in Groupings)
        {
            if (!ImGui.Selectable(name, grouping == config.UpgradeGrouping) || grouping == config.UpgradeGrouping)
                continue;
            config.UpgradeGrouping = grouping;
            save();
        }
    }

    private void DrawItem(uint itemId, string detail, Func<List<(string, Action)>>? menuEntries = null)
    {
        var iconSize = new Vector2(ImGui.GetFrameHeight() * 1.6f);
        var row = data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        var name = Name(itemId);
        var top = ImGui.GetCursorScreenPos().Y;

        ImGui.Image(textures.GetFromGameIcon(new GameIconLookup(row?.Icon ?? 0)).GetWrapOrEmpty().Handle, iconSize);
        HoverTooltip(itemId);
        var open = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        ImGui.SameLine();

        using (ImRaii.Group())
        {
            // A selectable so the controller can reach the row. X opens item menus in the game, and A here too.
            open |= ImGui.Selectable(name, false, ImGuiSelectableFlags.None, new Vector2(ImGui.CalcTextSize(name).X, 0)) ||
                    ImGui.IsItemClicked(ImGuiMouseButton.Right) ||
                    (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceLeft, false));
            HoverTooltip(itemId);
            ImGui.TextDisabled(detail);
        }
        if (!open)
            return;
        itemMenu.Open(itemId, name, top, windowMin, windowMax, menuEntries?.Invoke());
        // Both go beside the window, and the game closes an item's tooltip for its menu too.
        tooltip.Hide();
    }

    private void HoverTooltip(uint itemId)
    {
        if (ImGui.IsItemHovered() && !itemMenu.IsOpen)
            tooltip.Hover(itemId, windowMin, windowMax);
    }

    private static void DrawEmpty(string text)
    {
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
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
