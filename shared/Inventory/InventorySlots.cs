using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;

namespace Inventory;

// The bag, saddlebag and retainer slots on screen, and the item each holds. Shared by Keep in Mind and GlamKit.
// Each window's sorter lists its items in the order the window shows them, page after page, as VanillaPlus
// reads them. A slot only counts if it also shows that item's icon, so a wrong guess at the page shown leaves
// a slot out rather than pairing it with the wrong item (many items share an icon, so icons alone can't say
// which item a slot holds).
internal sealed unsafe class InventorySlots(IGameGui gameGui, IDataManager data)
{
    // Min and Max are screen positions for ImGui; Scale is the slot's UI scale.
    public readonly record struct Slot(uint ItemId, Vector2 Min, Vector2 Max, float Scale);

    // Game windows draw under ImGui, so slots under these are left out rather than drawn over them.
    private static readonly string[] Covers = ["ItemDetail", "ItemDetailCompare", "ContextMenu", "Tooltip", "SelectYesno", "SelectOk", "InputNumeric"];

    private static readonly string[] Grids =
    [
        "InventoryGrid", "InventoryGrid0", "InventoryGrid1",
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
        "RetainerGrid", "RetainerGrid0", "RetainerGrid1", "RetainerGrid2", "RetainerGrid3", "RetainerGrid4",
    ];

    private const string Saddlebag = "InventoryBuddy";

    private readonly Dictionary<uint, uint> icons = [];
    private readonly List<Slot> slots = [];

    // Bag, retainer and saddlebag slots holding an item, reused from call to call.
    public List<Slot> BagSlots()
    {
        slots.Clear();
        var module = ItemOrderModule.Instance();
        var inventory = InventoryManager.Instance();
        if (module == null || inventory == null)
            return slots;

        foreach (var name in Grids)
        {
            var grid = Addon<AddonInventoryGrid>(name);
            if (grid == null || Page(grid, name) is not { } page)
                continue;
            var sorter = name.StartsWith("Retainer") ? module->GetActiveRetainerSorter() : module->InventorySorter;
            Add(slots, &grid->AtkUnitBase, grid->Slots, page * grid->Slots.Length, sorter);
        }

        // Both pages in one window, the premium saddlebag on its second tab.
        if (Addon<AddonInventoryBuddy>(Saddlebag) is var buddy && buddy != null)
        {
            var premium = buddy->Tabs.Length > 1 && buddy->Tabs[1].Value != null && buddy->Tabs[1].Value->IsSelected;
            Add(slots, &buddy->AtkUnitBase, buddy->Slots, 0, premium ? module->PremiumSaddleBagSorter : module->SaddleBagSorter);
        }
        return slots;
    }

    // Adds a window's shown, uncovered slots holding the item its sorter puts there, the first at sorter index first.
    public void Add(List<Slot> into, AtkUnitBase* addon, Span<Pointer<AtkComponentDragDrop>> window, int first,
                    ItemOrderModuleSorter* sorter)
    {
        var inventory = InventoryManager.Instance();
        if (sorter == null || inventory == null)
            return;

        var offset = ImGui.GetMainViewport().Pos;
        for (var i = 0; i < window.Length; i++)
        {
            var slot = window[i].Value;
            if (slot == null || slot->OwnerNode == null)
                continue;
            var itemId = ItemAt(inventory, sorter, first + i);
            if (itemId == 0 || IconOf(slot) != Icon(itemId))
                continue;

            var node = &slot->OwnerNode->AtkResNode;
            if (!Shown(node))
                continue;
            var scale = addon->Scale * node->ScaleX;
            var min = offset + new Vector2(node->ScreenX, node->ScreenY);
            var max = min + new Vector2(node->Width, node->Height) * scale;
            if (!Covered(min, max))
                into.Add(new Slot(itemId, min, max, scale));
        }
    }

    // HQ icons are 1,000,000 on.
    public static uint IconOf(AtkComponentDragDrop* slot) => (uint)Math.Max(0, slot->GetIconId()) % 1_000_000;

    public uint Icon(uint itemId)
    {
        if (!icons.TryGetValue(itemId, out var icon))
            icons[itemId] = icon = data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Icon ?? 0;
        return icon;
    }

    public static uint ItemAt(InventoryManager* inventory, ItemOrderModuleSorter* sorter, int index)
    {
        if (index < 0 || index >= sorter->Items.LongCount)
            return 0;
        var entry = sorter->Items[index].Value;
        if (entry == null)
            return 0;
        var container = inventory->GetInventoryContainer(sorter->InventoryType + entry->Page);
        var item = container == null ? null : container->GetInventorySlot(entry->Slot);
        return item == null ? 0 : item->GetBaseItemId();
    }

    public T* Addon<T>(string name) where T : unmanaged
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible && addon->Alpha > 0 ? (T*)addon : null;
    }

    public static bool Shown(AtkResNode* node)
    {
        for (; node != null; node = node->ParentNode)
        {
            if (!node->IsVisible() || node->Alpha_2 == 0)
                return false;
        }
        return true;
    }

    public bool Covered(Vector2 min, Vector2 max)
    {
        var offset = ImGui.GetMainViewport().Pos;
        foreach (var name in Covers)
        {
            var cover = Addon<AtkUnitBase>(name);
            if (cover == null || cover->RootNode == null)
                continue;
            var coverMin = offset + new Vector2(cover->X, cover->Y);
            var coverMax = coverMin + new Vector2(cover->RootNode->Width, cover->RootNode->Height) * cover->Scale;
            if (min.X < coverMax.X && max.X > coverMin.X && min.Y < coverMax.Y && max.Y > coverMin.Y)
                return true;
        }
        return false;
    }

    // For the plugins' debug commands: what each bag window shows, and the page it's taken to show.
    public void Dump(IPluginLog log)
    {
        foreach (var name in (string[])[.. Grids, Saddlebag])
        {
            var addon = (AtkUnitBase*)gameGui.GetAddonByName(name).Address;
            if (addon == null)
                continue;
            var window = name == Saddlebag ? ((AddonInventoryBuddy*)addon)->Slots : ((AddonInventoryGrid*)addon)->Slots;
            var page = name == Saddlebag ? 0 : Page((AddonInventoryGrid*)addon, name);
            var shown = new List<string>();
            foreach (var pointer in window)
            {
                if (pointer.Value != null && shown.Count < 6)
                    shown.Add(pointer.Value->GetIconId().ToString());
            }
            log.Information($"{name}: visible {addon->IsVisible}, at {addon->X},{addon->Y}, tab {Tab(addon)}, " +
                            $"page {page?.ToString() ?? "hidden"}, {window.Length} slots, first icons {string.Join(" ", shown)}");
        }
    }

    // The page a grid shows, or null when it's hidden behind another. The small windows show one page per
    // tab; the large bag window two per tab; the large retainer window keeps all five page grids open and
    // "visible", stacked two to a tab, and shows only the tab's pair.
    private static int? Page(AddonInventoryGrid* grid, string name)
    {
        var tab = Tab(&grid->AtkUnitBase);
        return name switch
        {
            "InventoryGrid" or "RetainerGrid" => tab,
            "InventoryGrid0" => tab * 2,
            "InventoryGrid1" => tab * 2 + 1,
            _ when name.StartsWith("RetainerGrid") && name[^1] - '0' is var page =>
                tab == null || tab == page / 2 ? page : null,
            // InventoryGrid0E–3E stay "visible" behind the expanded window's key items tab.
            _ => tab is null or 0 ? name[^2] - '0' : null,
        };
    }

    public static int? Tab(AtkUnitBase* grid)
    {
        var manager = RaptureAtkUnitManager.Instance();
        var parent = manager == null || grid->ParentId == 0 ? null : manager->GetAddonById(grid->ParentId);
        if (parent == null)
            return null;
        return parent->NameString switch
        {
            "Inventory" => ((AddonInventory*)parent)->TabIndex,
            "InventoryLarge" => ((AddonInventoryLarge*)parent)->TabIndex,
            "InventoryExpansion" => ((AddonInventoryExpansion*)parent)->TabIndex,
            "InventoryRetainer" => ((AddonInventoryRetainer*)parent)->TabIndex,
            "InventoryRetainerLarge" => ((AddonInventoryRetainerLarge*)parent)->TabIndex,
            _ => null,
        };
    }
}
