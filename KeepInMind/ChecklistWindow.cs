using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Icons;
using Item = Lumina.Excel.Sheets.Item;

namespace KeepInMind;

internal sealed class ChecklistWindow : Window
{
    private const string ItemMenu = "##item";
    private const string ClearAllPopup = "Clear all?##clearAll";
    // Sized for two digits, the usual amount; more still fit, just cramped.
    private const string AmountWidthSample = "99";
    private static readonly TimeSpan HighlightFor = TimeSpan.FromSeconds(2);

    private readonly Configuration config;
    private readonly Action save;
    private readonly ItemActions actions;
    private readonly Sources sources;
    private readonly Stock stock;
    private readonly IDataManager data;
    private readonly ITextureProvider textures;
    private readonly NativeTooltip tooltip;
    private readonly IGamepadState gamepad;
    private readonly Action openSettings;

    private bool leaveGamepadMode;
    private Vector2 windowMin;
    private Vector2 windowMax;

    private uint highlighted;
    private DateTime highlightUntil;
    private bool scrollToHighlight;

    private string newGroupName = "";

    private (KeptItem, KeptItem)? swap;
    private bool reordering;
    private List<Source> sourcePriority = [.. Sources.Order];

    private sealed record Section(string Id, string Label, Vector4? Color, List<KeptItem> Items, bool DefaultOpen = true,
                                  bool Movable = false);

    public ChecklistWindow(Configuration config, Action save, ItemActions actions, Sources sources, Stock stock,
                           IDataManager data, ITextureProvider textures, NativeTooltip tooltip, IGamepadState gamepad,
                           Action openSettings)
        : base("Keep in Mind###KeepInMind")
    {
        this.gamepad = gamepad;
        this.config = config;
        this.save = save;
        this.actions = actions;
        this.sources = sources;
        this.stock = stock;
        this.data = data;
        this.textures = textures;
        this.tooltip = tooltip;
        this.openSettings = openSettings;
        Size = new Vector2(300, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(220, 140) };
    }

    public void Highlight(uint itemId)
    {
        highlighted = itemId;
        highlightUntil = DateTime.UtcNow + HighlightFor;
        scrollToHighlight = true;
    }

    public override void Draw()
    {
        // Once A is let go, so the game doesn't take the held press as its own.
        if (leaveGamepadMode && gamepad.Raw(GamepadButtons.South) == 0)
        {
            gamepad.EnableGamepadNav = false;
            ImGui.ClearWindowFocus();
            leaveGamepadMode = false;
        }

        windowMin = ImGui.GetWindowPos();
        windowMax = windowMin + ImGui.GetWindowSize();

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        using (var list = ImRaii.Child("##list", new Vector2(0, -footer)))
        {
            if (list)
                DrawList();
        }

        ImGui.Separator();
        DrawFooter();

        // Reset after the rows, so letting go of a drag doesn't also count as a click that opens the log.
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            reordering = false;
    }

    private void DrawList()
    {
        if (config.Items.Count == 0)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextDisabled("Nothing yet. Right-click an item in the Crafting Log or Gathering Log and choose Keep in mind.");
            ImGui.PopTextWrapPos();
            return;
        }

        sourcePriority = GroupOrder.SourcePriority(config);
        KeptItem? remove = null;
        foreach (var section in Sections())
        {
            if (section.Label.Length > 0)
            {
                if (scrollToHighlight && section.Items.Any(i => i.ItemId == highlighted))
                    ImGui.SetNextItemOpen(true);
                var done = section.Items.Count(i => i.Done);
                bool open;
                using (ImRaii.PushColor(ImGuiCol.Text, section.Color ?? default, section.Color.HasValue))
                {
                    open = ImGui.CollapsingHeader($"{section.Label} ({done}/{section.Items.Count})###{section.Id}",
                                                  section.DefaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
                }
                if (section.Movable)
                    DrawGroupHeaderMenu(section.Id);
                if (!open)
                    continue;
            }

            DrawRows(section.Items, ref remove);
        }

        if (remove != null)
        {
            config.Items.Remove(remove);
            save();
        }
        if (swap is var (a, b))
        {
            int ia = config.Items.IndexOf(a), ib = config.Items.IndexOf(b);
            (config.Items[ia], config.Items[ib]) = (b, a);
            swap = null;
            save();
        }
    }

    private void DrawRows(List<KeptItem> items, ref KeptItem? remove)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (DrawRow(items[i], i > 0 ? items[i - 1] : null, i < items.Count - 1 ? items[i + 1] : null))
                remove = items[i];
        }
    }

    // Headers toggle on click, so they can't be dragged like rows; right-click or Y moves them instead.
    private void DrawGroupHeaderMenu(string groupId)
    {
        const string popup = "##groupMenu";
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) ||
            (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false)))
        {
            ImGui.OpenPopup($"{popup}{groupId}");
        }
        if (!ImGui.BeginPopup($"{popup}{groupId}"))
            return;

        var order = GroupOrder.Of(config);
        var index = order.IndexOf(groupId);
        if (ImGui.Selectable("Move group up", false, index <= 0 ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None) &&
            GroupOrder.Move(config, groupId, -1))
        {
            save();
        }
        if (ImGui.Selectable("Move group down", false,
                             index >= order.Count - 1 ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None) &&
            GroupOrder.Move(config, groupId, 1))
        {
            save();
        }
        ImGui.EndPopup();
    }

    private List<Section> Sections()
    {
        var shown = config.Completed == CompletedMode.InPlace ? config.Items : config.Items.Where(i => !i.Done).ToList();
        var sections = new List<Section>();

        if (config.Grouping == Grouping.None)
        {
            sections.Add(new Section("all", "", null, Sorted(shown)));
        }
        else
        {
            if (config.Grouping == Grouping.Storage)
            {
                sections.AddRange(StorageSections(shown));
                var fetched = sections.SelectMany(s => s.Items).ToHashSet();
                shown = [.. shown.Where(i => !fetched.Contains(i))];
            }

            var order = GroupOrder.Of(config);
            var groups = new List<(int At, Section Section)>();
            foreach (var group in config.Groups)
                groups.Add((order.IndexOf(GroupOrder.Id(group)),
                            new Section(GroupOrder.Id(group), group.Name, group.Color,
                                        Sorted(shown.Where(i => i.GroupId == group.Id)), Movable: true)));

            var automatic = shown.Where(i => GroupOf(i) == null).ToList();
            foreach (var source in Sources.Order.Append(Source.None).Where(s => !Sources.IsGathering(s)))
                groups.Add((order.IndexOf(GroupOrder.Id(source)),
                            new Section(GroupOrder.Id(source), Sources.Name(source), SourceColor(source),
                                        Sorted(automatic.Where(i => PrimarySource(i) == source)), Movable: true)));

            // Zones take the place of the first gathering class in the group order, in game order among themselves.
            var zonesAt = Sources.Gathering.Min(s => order.IndexOf(GroupOrder.Id(s)));
            var zones = automatic.Where(i => Sources.IsGathering(PrimarySource(i)))
                                 .GroupBy(i => sources.ZoneOf(PrimarySource(i), i.ItemId))
                                 .OrderBy(z => z.Key == null)
                                 .ThenBy(z => z.Key?.Expansion)
                                 .ThenBy(z => z.Key?.Region)
                                 .ThenBy(z => z.Key?.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (var zone in zones)
                groups.Add((zonesAt, new Section($"zone{zone.Key?.Id ?? 0}", zone.Key?.Name ?? "Unknown area", null,
                                                 Sorted(zone))));

            sections.AddRange(groups.OrderBy(g => g.At).Select(g => g.Section));
        }

        if (config.Completed == CompletedMode.Section)
            sections.Add(new Section("completed", "Completed", null, Sorted(config.Items.Where(i => i.Done)), false));

        sections.RemoveAll(s => s.Items.Count == 0);
        return sections;
    }

    // Only items still short; each goes to the retainer holding the most, else the saddlebag.
    private IEnumerable<Section> StorageSections(List<KeptItem> shown)
    {
        var pending = shown.Where(i => !i.Done).ToList();
        var byRetainer = pending.Where(i => stock.Holders(i.ItemId).Count > 0)
                                .GroupBy(i => stock.Holders(i.ItemId)[0].Retainer)
                                .OrderBy(g => config.Retainers.FindIndex(r => r.Id == g.Key) is var at and >= 0
                                                  ? at
                                                  : int.MaxValue);
        foreach (var retainer in byRetainer)
            yield return new Section($"retainer{retainer.Key}", stock.RetainerName(retainer.Key), config.StorageColor,
                                     Sorted(retainer));

        yield return new Section("saddlebag", "Saddlebag", config.StorageColor,
                                 Sorted(pending.Where(i => stock.Holders(i.ItemId).Count == 0 &&
                                                           stock.Stored(i.ItemId).Saddlebag > 0)));
    }

    private List<KeptItem> Sorted(IEnumerable<KeptItem> items) => config.Sort switch
    {
        SortOrder.Name => [.. items.OrderBy(i => ItemName(i.ItemId), StringComparer.CurrentCultureIgnoreCase)],
        SortOrder.Remaining => [.. items.OrderByDescending(i => i.Done ? int.MinValue : i.Needed - Stock.Have(i.ItemId))],
        SortOrder.Inventory => [.. items.OrderBy(i => InventoryKey(i.ItemId))],
        _ => [.. items],
    };

    private Source PrimarySource(KeptItem kept) => Sources.Primary(actions.SourcesOf(kept.ItemId), sourcePriority);

    private CustomGroup? GroupOf(KeptItem kept) =>
        kept.GroupId is { } id ? config.Groups.Find(g => g.Id == id) : null;

    private Vector4? SourceColor(Source source) =>
        config.SourceColors.TryGetValue(source, out var color) ? color : null;

    private Vector4? NameColor(KeptItem kept) =>
        kept.Color ??
        GroupOf(kept)?.Color ??
        SourceColor(PrimarySource(kept));

    private bool DrawRow(KeptItem kept, KeptItem? above, KeptItem? below)
    {
        using var id = ImRaii.PushId((int)kept.ItemId);
        var name = ItemName(kept.ItemId);
        var iconSize = new Vector2(ImGui.GetFrameHeight());
        var removed = false;

        if (kept.ItemId == highlighted && DateTime.UtcNow < highlightUntil)
            DrawHighlight(iconSize.Y);

        var done = kept.Done;
        if (ImGui.Checkbox("##done", ref done))
        {
            kept.Done = done;
            save();
        }

        ImGui.SameLine();
        ImGui.Image(textures.GetFromGameIcon(new GameIconLookup(ItemIcon(kept.ItemId))).GetWrapOrEmpty().Handle,
                    iconSize);
        if (ImGui.IsItemHovered())
            tooltip.Hover(kept.ItemId, windowMin, windowMax);
        ImGui.SameLine();

        var stored = stock.Stored(kept.ItemId);
        var inStorage = stored.Retainers > 0 || stored.Saddlebag > 0;
        var stillShort = kept.Needed - Stock.Have(kept.ItemId) - stored.Retainers - stored.Saddlebag;
        Vector4? storageColor = !inStorage ? null : stillShort <= 0 ? config.StorageEnoughColor : config.StorageColor;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var countWidth = ImGui.CalcTextSize(AmountWidthSample).X + ImGui.GetStyle().FramePadding.X * 2 +
                         (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X) * 2;
        var removeWidth = ImGui.GetFrameHeight();
        var storageWidth = inStorage ? TablerIcons.Width + spacing : 0;
        var gatherer = Sources.Primary(actions.SourcesOf(kept.ItemId), sourcePriority.Where(Sources.IsGathering));
        var gathererWidth = gatherer != Source.None ? ImGui.GetFontSize() + spacing : 0;
        var nameWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X - countWidth - removeWidth - storageWidth -
                                     gathererWidth - spacing * 2);
        ImGui.AlignTextToFramePadding();
        var color = kept.Done ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled] : NameColor(kept);
        using (ImRaii.PushColor(ImGuiCol.Text, color ?? default, color.HasValue))
        {
            if (ImGui.Selectable(name, false, ImGuiSelectableFlags.None, new Vector2(nameWidth, 0)) && !reordering &&
                actions.OpenLog(kept.ItemId))
            {
                leaveGamepadMode = gamepad.EnableGamepadNav || PadNav.Navigating;
            }
        }
        // Dragging a name past its neighbour swaps them, as in ImGui's own reorder demo.
        if (config.Sort == SortOrder.Manual && ImGui.IsItemActive() && !ImGui.IsItemHovered() &&
            ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            if ((ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y < 0 ? above : below) is { } neighbour)
            {
                swap = (kept, neighbour);
                reordering = true;
                ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
            }
        }
        if (ImGui.IsItemHovered())
            tooltip.Hover(kept.ItemId, windowMin, windowMax);
        // Y is free on a row; X is ImGui's own menu button.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) ||
            (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false)))
        {
            newGroupName = "";
            ImGui.OpenPopup(ItemMenu);
        }
        if (kept.Done)
            StrikeThrough(name);

        if (gatherer != Source.None)
        {
            ImGui.SameLine();
            DrawGathererIcon(gatherer);
        }

        if (inStorage)
        {
            ImGui.SameLine();
            DrawStorageIcon(kept.ItemId, stored, stillShort, storageColor!.Value);
        }

        ImGui.SameLine();
        DrawNeeded(kept);

        ImGui.SameLine();
        if (TablerIcons.Button(TablerIcon.X))
            removed = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Remove from the list");

        if (ImGui.BeginPopup(ItemMenu))
        {
            if (DrawItemMenu(kept, name, above, below))
                removed = true;
            ImGui.EndPopup();
        }

        if (scrollToHighlight && kept.ItemId == highlighted)
        {
            ImGui.SetScrollHereY();
            scrollToHighlight = false;
        }
        return removed;
    }

    private void DrawGathererIcon(Source gatherer)
    {
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().FramePadding.Y);
        ImGui.Image(textures.GetFromGameIcon(new GameIconLookup(Sources.Icon(gatherer))).GetWrapOrEmpty().Handle,
                    new Vector2(ImGui.GetFontSize()));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Sources.Name(gatherer));
    }

    private void DrawStorageIcon(uint itemId, (int Retainers, int Saddlebag) stored, int stillShort, Vector4 color)
    {
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().FramePadding.Y);
        TablerIcons.Draw(stillShort <= 0 ? TablerIcon.MoneybagPlus : TablerIcon.MoneybagMinus, color);
        if (!ImGui.IsItemHovered())
            return;

        var places = stock.Holders(itemId).Select(h => $"{h.Count} with {stock.RetainerName(h.Retainer)}").ToList();
        if (stored.Saddlebag > 0)
            places.Add($"{stored.Saddlebag} in the saddlebag");
        ImGui.SetTooltip(string.Join(", ", places));
    }

    private void DrawNeeded(KeptItem kept)
    {
        var button = new Vector2(ImGui.GetFrameHeight());
        var inner = ImGui.GetStyle().ItemInnerSpacing.X;
        var needed = kept.Needed;

        ImGui.PushButtonRepeat(true);
        if (ImGui.Button("-", button))
            needed--;
        ImGui.SameLine(0, inner);
        ImGui.SetNextItemWidth(ImGui.CalcTextSize(AmountWidthSample).X + ImGui.GetStyle().FramePadding.X * 2);
        ImGui.InputInt("##needed", ref needed, 0, 0);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"How many you need (you have {Stock.Have(kept.ItemId)})");
        ImGui.SameLine(0, inner);
        if (ImGui.Button("+", button))
            needed++;
        ImGui.PopButtonRepeat();

        needed = Math.Clamp(needed, 1, 9999);
        if (needed != kept.Needed)
        {
            kept.Needed = needed;
            save();
        }
    }

    private bool DrawItemMenu(KeptItem kept, string name, KeptItem? above, KeptItem? below)
    {
        ImGui.TextDisabled(name);
        var itemSources = actions.SourcesOf(kept.ItemId);
        ImGui.TextDisabled(itemSources == Source.None
                               ? "No known source"
                               : string.Join(" · ", Sources.Order.Where(s => itemSources.HasFlag(s)).Select(Sources.Name)));
        ImGui.Separator();

        if (!actions.VendorPluginLoaded)
        {
            ImGui.BeginDisabled();
            ImGui.Selectable("Vendor location (Item Vendor Location isn't loaded)");
            ImGui.EndDisabled();
        }
        else if (itemSources.HasFlag(Source.Vendor))
        {
            if (ImGui.Selectable("Vendor location"))
                actions.ShowVendors(kept.ItemId);
        }
        else
        {
            ImGui.TextDisabled("Not sold by vendors.");
        }

        ImGui.Separator();
        DrawColorMenu(kept);
        DrawGroupMenu(kept);
        // For controllers, which can't drag; only your own order can be changed.
        if (config.Sort == SortOrder.Manual)
        {
            if (ImGui.Selectable("Move up", false, above == null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
                swap = (kept, above!);
            if (ImGui.Selectable("Move down", false, below == null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
                swap = (kept, below!);
        }

        ImGui.Separator();
        if (ImGui.Selectable(kept.Done ? "Mark as not done" : "Mark as done"))
        {
            kept.Done = !kept.Done;
            save();
        }
        return ImGui.Selectable("Remove from the list");
    }

    private void DrawColorMenu(KeptItem kept)
    {
        if (!ImGui.BeginMenu("Colour"))
            return;

        var color = kept.Color ?? NameColor(kept) ?? ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        if (ImGui.ColorPicker4("##color", ref color, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoSidePreview))
        {
            kept.Color = color;
            save();
        }
        using (ImRaii.Disabled(kept.Color == null))
        {
            if (ImGui.Selectable("Automatic colour"))
            {
                kept.Color = null;
                save();
            }
        }
        ImGui.EndMenu();
    }

    private void DrawGroupMenu(KeptItem kept)
    {
        if (!ImGui.BeginMenu("Group"))
            return;

        var current = GroupOf(kept);
        if (ImGui.Selectable("Automatic (by source)", current == null))
        {
            kept.GroupId = null;
            save();
        }
        foreach (var group in config.Groups)
        {
            using var id = ImRaii.PushId(group.Id.ToString());
            if (ImGui.Selectable(group.Name, current == group))
            {
                kept.GroupId = group.Id;
                save();
            }
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        var entered = ImGui.InputTextWithHint("##newGroup", "New group", ref newGroupName, 40,
                                              ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        using (ImRaii.Disabled(newGroupName.Trim().Length == 0))
        {
            if ((ImGui.Button("Add") || entered) && newGroupName.Trim().Length > 0)
            {
                var group = new CustomGroup { Name = newGroupName.Trim() };
                config.Groups.Add(group);
                kept.GroupId = group.Id;
                newGroupName = "";
                save();
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.EndMenu();
    }

    private void DrawFooter()
    {
        var anyDone = config.Items.Any(i => i.Done);
        using (ImRaii.Disabled(!anyDone))
        {
            if (ImGui.Button("Clear completed"))
            {
                config.Items.RemoveAll(i => i.Done);
                save();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(config.Items.Count == 0))
        {
            if (ImGui.Button("Clear all"))
                ImGui.OpenPopup(ClearAllPopup);
        }

        if (config.Completed == CompletedMode.Hidden && config.Items.Count(i => i.Done) is > 0 and var hidden)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled($"{hidden} hidden");
        }

        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
        if (TablerIcons.Button(TablerIcon.Settings))
            openSettings();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Settings");

        if (ImGui.BeginPopup(ClearAllPopup))
        {
            ImGui.TextUnformatted($"Remove all {config.Items.Count} items from the list?");
            if (ImGui.Button("Clear all"))
            {
                config.Items.Clear();
                save();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawHighlight(float height)
    {
        var left = (float)(highlightUntil - DateTime.UtcNow).TotalSeconds / (float)HighlightFor.TotalSeconds;
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive];
        color.W *= Math.Clamp(left, 0f, 1f);
        var start = ImGui.GetCursorScreenPos();
        var end = new Vector2(start.X + ImGui.GetContentRegionAvail().X, start.Y + height);
        ImGui.GetWindowDrawList().AddRectFilled(start, end, ImGui.GetColorU32(color), ImGui.GetStyle().FrameRounding);
    }

    private static void StrikeThrough(string text)
    {
        var min = ImGui.GetItemRectMin();
        var textWidth = Math.Min(ImGui.CalcTextSize(text).X, ImGui.GetItemRectSize().X);
        var y = min.Y + ImGui.GetItemRectSize().Y / 2;
        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, y), new Vector2(min.X + textWidth, y),
                                          ImGui.GetColorU32(ImGuiCol.TextDisabled), ImGuiHelpers.GlobalScale);
    }

    private string ItemName(uint itemId) =>
        data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ExtractText() is { Length: > 0 } name
            ? name
            : $"Item #{itemId}";

    // As the game sorts bags: item category, then sort category, then id.
    private (int, int, int, uint) InventoryKey(uint itemId) =>
        data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } item
            ? (item.ItemUICategory.ValueNullable?.OrderMajor ?? int.MaxValue,
               item.ItemUICategory.ValueNullable?.OrderMinor ?? int.MaxValue,
               item.ItemSortCategory.ValueNullable?.Param ?? int.MaxValue, itemId)
            : (int.MaxValue, int.MaxValue, int.MaxValue, itemId);

    private uint ItemIcon(uint itemId) => data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Icon ?? 0;
}
