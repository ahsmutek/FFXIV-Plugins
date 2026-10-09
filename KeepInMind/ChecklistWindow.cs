using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Item = Lumina.Excel.Sheets.Item;

namespace KeepInMind;

internal sealed class ChecklistWindow : Window
{
    private const string ItemMenu = "##item";
    private const string ClearAllPopup = "Clear all?##clearAll";
    private static readonly TimeSpan HighlightFor = TimeSpan.FromSeconds(2);

    private readonly Configuration config;
    private readonly Action save;
    private readonly ItemActions actions;
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

    private sealed record Section(string Id, string Label, Vector4? Color, List<KeptItem> Items, bool DefaultOpen = true);

    public ChecklistWindow(Configuration config, Action save, ItemActions actions, Stock stock, IDataManager data,
                           ITextureProvider textures, NativeTooltip tooltip, IGamepadState gamepad, Action openSettings)
        : base("Keep in Mind###KeepInMind")
    {
        this.gamepad = gamepad;
        this.config = config;
        this.save = save;
        this.actions = actions;
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
                if (!open)
                    continue;
            }

            foreach (var kept in section.Items)
            {
                if (DrawRow(kept))
                    remove = kept;
            }
        }

        if (remove != null)
        {
            config.Items.Remove(remove);
            save();
        }
    }

    private List<Section> Sections()
    {
        var shown = config.Completed == CompletedMode.InPlace ? config.Items : config.Items.Where(i => !i.Done).ToList();
        var sections = new List<Section>();

        if (!config.GroupBySource)
        {
            sections.Add(new Section("all", "", null, Sorted(shown)));
        }
        else
        {
            foreach (var group in config.Groups)
                sections.Add(new Section(group.Id.ToString(), group.Name, group.Color,
                                         Sorted(shown.Where(i => i.GroupId == group.Id))));

            var automatic = shown.Where(i => GroupOf(i) == null).ToList();
            foreach (var source in Sources.Order.Append(Source.None))
                sections.Add(new Section(source.ToString(), Sources.Name(source), SourceColor(source),
                                         Sorted(automatic.Where(i => Sources.Primary(actions.SourcesOf(i.ItemId)) == source))));
        }

        if (config.Completed == CompletedMode.Section)
            sections.Add(new Section("completed", "Completed", null, Sorted(config.Items.Where(i => i.Done)), false));

        sections.RemoveAll(s => s.Items.Count == 0);
        return sections;
    }

    private List<KeptItem> Sorted(IEnumerable<KeptItem> items) => config.Sort switch
    {
        SortOrder.Name => [.. items.OrderBy(i => ItemName(i.ItemId), StringComparer.CurrentCultureIgnoreCase)],
        SortOrder.Remaining => [.. items.OrderByDescending(i => i.Done ? int.MinValue : i.Needed - Stock.Have(i.ItemId))],
        _ => [.. items],
    };

    private CustomGroup? GroupOf(KeptItem kept) =>
        kept.GroupId is { } id ? config.Groups.Find(g => g.Id == id) : null;

    private Vector4? SourceColor(Source source) =>
        config.SourceColors.TryGetValue(source, out var color) ? color : null;

    private Vector4? NameColor(KeptItem kept) =>
        kept.Color ??
        GroupOf(kept)?.Color ??
        SourceColor(Sources.Primary(actions.SourcesOf(kept.ItemId)));

    private bool DrawRow(KeptItem kept)
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
        var countWidth = ImGui.CalcTextSize("9999").X + ImGui.GetStyle().FramePadding.X * 2 +
                         (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X) * 2;
        var removeWidth = ImGui.GetFrameHeight();
        var storageWidth = inStorage ? StorageIconWidth() + spacing : 0;
        var nameWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X - countWidth - removeWidth - storageWidth - spacing * 2);
        ImGui.AlignTextToFramePadding();
        var color = kept.Done ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled] : NameColor(kept);
        using (ImRaii.PushColor(ImGuiCol.Text, color ?? default, color.HasValue))
        {
            if (ImGui.Selectable(name, false, ImGuiSelectableFlags.None, new Vector2(nameWidth, 0)) &&
                actions.OpenLog(kept.ItemId))
            {
                leaveGamepadMode = gamepad.EnableGamepadNav;
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

        if (inStorage)
        {
            ImGui.SameLine();
            DrawStorageIcon(stored, stillShort, storageColor!.Value);
        }

        ImGui.SameLine();
        DrawNeeded(kept);

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            removed = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Remove from the list");

        if (ImGui.BeginPopup(ItemMenu))
        {
            if (DrawItemMenu(kept, name))
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

    private static float StorageIconWidth()
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(FontAwesomeIcon.Box.ToIconString()).X;
    }

    private static void DrawStorageIcon((int Retainers, int Saddlebag) stored, int stillShort, Vector4 color)
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(FontAwesomeIcon.Box.ToIconString());
        if (!ImGui.IsItemHovered())
            return;

        var places = new List<string>(2);
        if (stored.Retainers > 0)
            places.Add($"{stored.Retainers} with retainers");
        if (stored.Saddlebag > 0)
            places.Add($"{stored.Saddlebag} in the saddlebag");
        ImGui.SetTooltip(string.Join(", ", places) +
                         (stillShort <= 0 ? "\nEnough, with your bags" : $"\n{stillShort} short, even with your bags"));
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
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("9999").X + ImGui.GetStyle().FramePadding.X * 2);
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

    private bool DrawItemMenu(KeptItem kept, string name)
    {
        ImGui.TextDisabled(name);
        var sources = actions.SourcesOf(kept.ItemId);
        ImGui.TextDisabled(sources == Source.None
                               ? "No known source"
                               : string.Join(" · ", Sources.Order.Where(s => sources.HasFlag(s)).Select(Sources.Name)));
        ImGui.Separator();

        if (!actions.VendorPluginLoaded)
        {
            ImGui.BeginDisabled();
            ImGui.Selectable("Vendor location (Item Vendor Location isn't loaded)");
            ImGui.EndDisabled();
        }
        else if (sources.HasFlag(Source.Vendor))
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
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
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

    private uint ItemIcon(uint itemId) => data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Icon ?? 0;
}
