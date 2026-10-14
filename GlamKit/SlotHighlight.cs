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

namespace GlamKit;

// Borders the bag, saddlebag, retainer and Armoury Chest slots holding kept gear, as Keep in Mind does.
// Each window's sorter lists its items in the order the window shows them, page after page. A slot is only
// bordered if it also shows that item's icon, so a wrong guess at the page shown leaves a border out rather
// than putting one on the wrong item (many items share an icon, so icons alone can't say which item a slot holds).
internal sealed unsafe class SlotHighlight
{
    private const float Thickness = 2f;
    private const float Rounding = 6f;

    // Game windows draw under ImGui, so slots under these are left alone rather than drawn over them.
    private static readonly string[] Covers = ["ItemDetail", "ContextMenu", "Tooltip"];

    private static readonly string[] Grids =
    [
        "InventoryGrid", "InventoryGrid0", "InventoryGrid1",
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
        "RetainerGrid", "RetainerGrid0", "RetainerGrid1", "RetainerGrid2", "RetainerGrid3", "RetainerGrid4",
    ];

    private const string Armoury = "ArmouryBoard";

    private readonly IGameGui gameGui;
    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly Dictionary<uint, uint> icons = [];

    public SlotHighlight(IGameGui gameGui, IDataManager data, Configuration config)
    {
        this.gameGui = gameGui;
        this.data = data;
        this.config = config;
    }

    public void Draw()
    {
        var module = ItemOrderModule.Instance();
        var inventory = InventoryManager.Instance();
        if (!config.GlamKeeperEnabled || module == null || inventory == null || config.Items.Count == 0)
            return;

        foreach (var name in Grids)
        {
            var grid = Addon<AddonInventoryGrid>(name);
            if (grid == null || Page(grid, name) is not { } page)
                continue;
            var sorter = name.StartsWith("Retainer") ? module->GetActiveRetainerSorter() : module->InventorySorter;
            DrawSlots(&grid->AtkUnitBase, grid->Slots, page * grid->Slots.Length, sorter, inventory);
        }

        // Saddlebag: both pages in one window, the premium saddlebag on its second tab.
        if (Addon<AddonInventoryBuddy>("InventoryBuddy") is var buddy && buddy != null)
        {
            var premium = buddy->Tabs.Length > 1 && buddy->Tabs[1].Value != null && buddy->Tabs[1].Value->IsSelected;
            DrawSlots(&buddy->AtkUnitBase, buddy->Slots, 0,
                      premium ? module->PremiumSaddleBagSorter : module->SaddleBagSorter, inventory);
        }

        if (Addon<AddonArmouryBoard>(Armoury) is var armoury && armoury != null &&
            ArmourySorter(armoury, module, inventory) is var armourySorter && armourySorter != null)
        {
            DrawSlots(&armoury->AtkUnitBase, armoury->Slots, 0, armourySorter, inventory);
        }
    }

    // The Armoury Chest shows one category at a time. Rather than trust a tab-to-category table, the sorter
    // is the one whose items' icons match what the slots show.
    private ItemOrderModuleSorter* ArmourySorter(AddonArmouryBoard* armoury, ItemOrderModule* module, InventoryManager* inventory)
    {
        ItemOrderModuleSorter* best = null;
        var bestScore = 0;
        foreach (var pointer in module->ArmourySorter)
        {
            var sorter = pointer.Value;
            if (sorter == null)
                continue;
            var score = 0;
            for (var i = 0; i < armoury->Slots.Length; i++)
            {
                var slot = armoury->Slots[i].Value;
                var itemId = ItemAt(inventory, sorter, i);
                if (slot != null && itemId != 0 && (uint)Math.Max(0, slot->GetIconId()) % 1_000_000 == Icon(itemId))
                    score++;
            }
            if (score > bestScore)
            {
                best = sorter;
                bestScore = score;
            }
        }
        return best;
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
            _ => name[^2] - '0', // InventoryGrid0E–3E
        };
    }

    private static int? Tab(AtkUnitBase* grid)
    {
        var manager = RaptureAtkUnitManager.Instance();
        var parent = manager == null || grid->ParentId == 0 ? null : manager->GetAddonById(grid->ParentId);
        if (parent == null)
            return null;
        return parent->NameString switch
        {
            "Inventory" => ((AddonInventory*)parent)->TabIndex,
            "InventoryLarge" => ((AddonInventoryLarge*)parent)->TabIndex,
            "InventoryRetainer" => ((AddonInventoryRetainer*)parent)->TabIndex,
            "InventoryRetainerLarge" => ((AddonInventoryRetainerLarge*)parent)->TabIndex,
            _ => null,
        };
    }

    private void DrawSlots(AtkUnitBase* addon, Span<Pointer<AtkComponentDragDrop>> slots, int first,
                           ItemOrderModuleSorter* sorter, InventoryManager* inventory)
    {
        if (sorter == null)
            return;

        var offset = ImGui.GetMainViewport().Pos;
        var drawList = ImGui.GetBackgroundDrawList();
        var color = ImGui.GetColorU32(config.KeeperColor);
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i].Value;
            if (slot == null || slot->OwnerNode == null)
                continue;
            var itemId = ItemAt(inventory, sorter, first + i);
            if (!config.IsKept(itemId))
                continue;
            // HQ icons are 1,000,000 on.
            if ((uint)Math.Max(0, slot->GetIconId()) % 1_000_000 != Icon(itemId))
                continue;

            var node = &slot->OwnerNode->AtkResNode;
            if (!Shown(node))
                continue;
            var scale = addon->Scale * node->ScaleX;
            var min = offset + new Vector2(node->ScreenX, node->ScreenY);
            var max = min + new Vector2(node->Width, node->Height) * scale;
            if (Covered(min, max))
                continue;
            drawList.AddRect(min, max, color, Rounding * scale, ImDrawFlags.None, Thickness * scale);
        }
    }

    // For /glamkit debug: what each inventory window the highlight looks at shows.
    public void Dump(IPluginLog log)
    {
        foreach (var name in (string[])[.. Grids, "InventoryBuddy", Armoury])
        {
            var addon = (AtkUnitBase*)gameGui.GetAddonByName(name).Address;
            if (addon == null)
                continue;
            var slots = name switch
            {
                "InventoryBuddy" => ((AddonInventoryBuddy*)addon)->Slots,
                Armoury => ((AddonArmouryBoard*)addon)->Slots,
                _ => ((AddonInventoryGrid*)addon)->Slots,
            };
            var page = name is "InventoryBuddy" or Armoury ? 0 : Page((AddonInventoryGrid*)addon, name);
            var shown = new List<string>();
            foreach (var pointer in slots)
            {
                if (pointer.Value != null && shown.Count < 6)
                    shown.Add(pointer.Value->GetIconId().ToString());
            }
            var extra = "";
            if (name == Armoury)
            {
                var module = ItemOrderModule.Instance();
                var inventory = InventoryManager.Instance();
                var sorter = module == null || inventory == null ? null :
                             ArmourySorter((AddonArmouryBoard*)addon, module, inventory);
                extra = $", tab {((AddonArmouryBoard*)addon)->TabIndex}, sorter " +
                        (sorter == null ? "none" : sorter->InventoryType.ToString());
            }
            log.Information($"{name}: visible {addon->IsVisible}, at {addon->X},{addon->Y}, tab {Tab(addon)}, " +
                            $"page {page?.ToString() ?? "hidden"}, {slots.Length} slots, first icons {string.Join(" ", shown)}{extra}");
        }
    }

    private T* Addon<T>(string name) where T : unmanaged
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible && addon->Alpha > 0 ? (T*)addon : null;
    }

    private static bool Shown(AtkResNode* node)
    {
        for (; node != null; node = node->ParentNode)
        {
            if (!node->IsVisible() || node->Alpha_2 == 0)
                return false;
        }
        return true;
    }

    private static uint ItemAt(InventoryManager* inventory, ItemOrderModuleSorter* sorter, int index)
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

    private uint Icon(uint itemId)
    {
        if (!icons.TryGetValue(itemId, out var icon))
            icons[itemId] = icon = data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Icon ?? 0;
        return icon;
    }

    private bool Covered(Vector2 min, Vector2 max)
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
}
