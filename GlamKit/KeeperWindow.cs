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
                if (DrawTab("For Glam", config.Items, "Keep for Glam", "Stop keeping for glam", footer) &&
                    !config.GlamKeeperEnabled)
                    off = "Glam Keeper is off in settings.";
                if (DrawTab("For Later", config.LaterItems, "Keep for Later", "Stop keeping for later", footer) &&
                    !config.KeepForLaterEnabled)
                    off = "Keep for Later is off in settings.";
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
    private bool DrawTab(string label, List<KeptItem> items, string menuEntry, string removeTip, float footer)
    {
        using var tab = ImRaii.TabItem($"{label} ({items.Count})###{label}");
        if (!tab)
            return false;
        using var list = ImRaii.Child("##list", new Vector2(0, -footer));
        if (list)
            DrawList(items, menuEntry, removeTip);
        return true;
    }

    private void DrawList(List<KeptItem> items, string menuEntry, string removeTip)
    {
        if (items.Count == 0)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextDisabled($"Nothing kept. Open the item menu on gear and choose {menuEntry}.");
            ImGui.PopTextWrapPos();
            return;
        }

        KeptItem? remove = null;
        foreach (var kept in items.OrderBy(i => Name(i.ItemId)))
        {
            if (DrawRow(kept, removeTip))
                remove = kept;
        }
        if (remove != null)
        {
            items.Remove(remove);
            save();
        }
    }

    private bool DrawRow(KeptItem kept, string removeTip)
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
            ImGui.TextDisabled(locations.Of(kept.ItemId));
        }

        ImGui.SameLine(ImGui.GetContentRegionMax().X - removeWidth);
        var removed = TablerIcons.Button(TablerIcon.X);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(removeTip);
        return removed;
    }

    private string Name(uint itemId) => data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ExtractText() ?? $"Item {itemId}";
}
