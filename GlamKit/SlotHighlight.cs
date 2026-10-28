using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Icons;
using Lumina.Excel.Sheets;

namespace GlamKit;

// Borders the bag, saddlebag, retainer and Armoury Chest slots holding gear kept for glam, as Keep in Mind does,
// and badges those holding gear kept for later.
// Each window's sorter lists its items in the order the window shows them, page after page. A slot is only
// bordered if it also shows that item's icon, so a wrong guess at the page shown leaves a border out rather
// than putting one on the wrong item (many items share an icon, so icons alone can't say which item a slot holds).
internal sealed unsafe class SlotHighlight
{
    private const float Thickness = 2f;
    private const float Rounding = 6f;
    private const float IconGap = 8f;
    private const float BadgeSize = 0.35f;
    private const uint BadgeBackground = 0xC0000000;

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
    private readonly Dictionary<uint, string> names = [];

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
        var anyGlam = config.GlamKeeperEnabled && config.Items.Count > 0;
        var anyLater = config.KeepForLaterEnabled && config.LaterItems.Count > 0;
        if (module == null || inventory == null || (!anyGlam && !anyLater))
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

        foreach (var name in ItemLists.Addons)
            DrawItemList(name);
        DrawVentureReport();
    }

    // A row of a window listing gear by name is marked when it shows a kept item's name: a hanger after it for
    // glam, a bookmark for later.
    private void DrawItemList(string addonName)
    {
        var addon = Addon<AtkUnitBase>(addonName);
        var list = addon == null ? null : List(addon);
        if (list == null)
            return;
        var glamNames = config.GlamKeeperEnabled ? Names(config.Items) : [];
        var laterNames = config.KeepForLaterEnabled ? Names(config.LaterItems) : [];
        if (glamNames.Count == 0 && laterNames.Count == 0)
            return;
        var kept = glamNames.Union(laterNames).ToHashSet();

        var offset = ImGui.GetMainViewport().Pos;
        var drawList = ImGui.GetBackgroundDrawList();
        var listNode = &list->OwnerNode->AtkResNode;
        var clipMin = offset + new Vector2(listNode->ScreenX, listNode->ScreenY);
        var clipMax = clipMin + new Vector2(listNode->Width, listNode->Height) * addon->Scale;
        drawList.PushClipRect(clipMin, clipMax, true);
        foreach (var row in Rows(list))
        {
            var name = NameNode(&row.Value->AtkComponentButton.AtkComponentBase.UldManager, kept);
            if (name == null || !Shown(&name->AtkResNode))
                continue;

            ushort width, height;
            name->GetTextDrawSize(&width, &height, null, 0, -1, false);
            var scale = addon->Scale * name->AtkResNode.ScaleX;
            var size = height * scale;
            var min = offset + new Vector2(name->AtkResNode.ScreenX + (width + IconGap) * scale,
                                           name->AtkResNode.ScreenY + (name->AtkResNode.Height * scale - size) / 2);
            var text = ItemLists.Plain(name);
            DrawMarks(drawList, min, size, IconGap * scale, glamNames.Contains(text), laterNames.Contains(text));
        }
        drawList.PopClipRect();
    }

    // The venture report shows its rewards as icons only, so a kept reward gets its marks beside its icon.
    private void DrawVentureReport()
    {
        var addon = Addon<AtkUnitBase>(ItemLists.VentureReport);
        var agent = AgentRetainerTask.Instance();
        if (addon == null || agent == null)
            return;

        var offset = ImGui.GetMainViewport().Pos;
        var drawList = ImGui.GetBackgroundDrawList();
        foreach (var reward in agent->RetainerData.RewardItemIds)
        {
            var itemId = reward % 1_000_000;
            var glam = KeptForGlam(itemId);
            var later = KeptForLater(itemId);
            if (!glam && !later)
                continue;
            var icon = IconNode(&addon->UldManager, Icon(itemId));
            if (icon == null || icon->OwnerNode == null || !Shown(&icon->OwnerNode->AtkResNode))
                continue;

            var node = &icon->OwnerNode->AtkResNode;
            var scale = addon->Scale * node->ScaleX;
            var size = node->Height * scale / 2;
            var min = offset + new Vector2(node->ScreenX + (node->Width + IconGap) * scale,
                                           node->ScreenY + (node->Height * scale - size) / 2);
            DrawMarks(drawList, min, size, IconGap * scale, glam, later);
        }
    }

    // A hanger, then a bookmark, in a row from min.
    private void DrawMarks(ImDrawListPtr drawList, Vector2 min, float size, float gap, bool glam, bool later)
    {
        var count = (glam ? 1 : 0) + (later ? 1 : 0);
        if (count == 0 || Covered(min, min + new Vector2(count * size + (count - 1) * gap, size)))
            return;
        if (glam)
        {
            TablerIcons.DrawAt(drawList, TablerIcon.Hanger, min, size, ImGui.GetColorU32(config.KeeperColor));
            min.X += size + gap;
        }
        if (later)
            TablerIcons.DrawAt(drawList, TablerIcon.Bookmark, min, size, ImGui.GetColorU32(config.LaterColor));
    }

    private bool KeptForGlam(uint itemId) => config.GlamKeeperEnabled && config.IsKept(itemId);

    private bool KeptForLater(uint itemId) => config.KeepForLaterEnabled && config.IsKeptForLater(itemId);

    // HQ icons are 1,000,000 on.
    private static AtkComponentIcon* IconNode(AtkUldManager* uld, uint iconId)
    {
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || (ushort)node->Type < 1000)
                continue;
            var component = ((AtkComponentNode*)node)->Component;
            if (component == null)
                continue;
            if (component->GetComponentType() == ComponentType.Icon &&
                ((AtkComponentIcon*)component)->IconId % 1_000_000 == iconId)
                return (AtkComponentIcon*)component;
            if (IconNode(&component->UldManager, iconId) is var found && found != null)
                return found;
        }
        return null;
    }

    private HashSet<string> Names(IEnumerable<KeptItem> items)
    {
        var kept = new HashSet<string>();
        foreach (var item in items)
        {
            if (!names.TryGetValue(item.ItemId, out var name))
                names[item.ItemId] = name = data.GetExcelSheet<Item>().GetRowOrDefault(item.ItemId)?.Name.ExtractText() ?? "";
            if (name.Length > 0)
                kept.Add(name);
        }
        return kept;
    }

    // A list's or tree list's rows in use. Rows past the end of a shorter list keep their old text.
    private static List<Pointer<AtkComponentListItemRenderer>> Rows(AtkComponentList* list)
    {
        var rows = new List<Pointer<AtkComponentListItemRenderer>>();
        var isList = list->GetComponentType() == ComponentType.List;
        ref var uld = ref list->AtkComponentBase.UldManager;
        for (var i = 0; i < uld.NodeListCount; i++)
        {
            var node = uld.NodeList[i];
            if (node == null || (ushort)node->Type < 1000)
                continue;
            var component = ((AtkComponentNode*)node)->Component;
            if (component == null || component->GetComponentType() != ComponentType.ListItemRenderer)
                continue;
            var renderer = (AtkComponentListItemRenderer*)component;
            if (isList && (renderer->ListItemIndex < 0 || renderer->ListItemIndex >= list->ListLength))
                continue;
            rows.Add(renderer);
        }
        return rows;
    }

    private static AtkComponentList* List(AtkUnitBase* addon)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || (ushort)node->Type < 1000)
                continue;
            var component = ((AtkComponentNode*)node)->Component;
            if (component != null && component->GetComponentType() is ComponentType.List or ComponentType.TreeList)
                return (AtkComponentList*)component;
        }
        return null;
    }

    private static AtkTextNode* NameNode(AtkUldManager* uld, HashSet<string> names)
    {
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null)
                continue;
            if (node->Type == NodeType.Text && names.Contains(ItemLists.Plain((AtkTextNode*)node)))
                return (AtkTextNode*)node;
            if ((ushort)node->Type >= 1000 && ((AtkComponentNode*)node)->Component is var component && component != null &&
                NameNode(&component->UldManager, names) is var found && found != null)
                return found;
        }
        return null;
    }

    // The plain text of every text node, nested components included.
    private static List<string> Texts(AtkUldManager* uld, List<string>? texts = null)
    {
        texts ??= [];
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null)
                continue;
            if (node->Type == NodeType.Text)
                texts.Add(ItemLists.Plain((AtkTextNode*)node));
            else if ((ushort)node->Type >= 1000 && ((AtkComponentNode*)node)->Component is var component && component != null)
                Texts(&component->UldManager, texts);
        }
        return texts;
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
        var laterColor = ImGui.GetColorU32(config.LaterColor);
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i].Value;
            if (slot == null || slot->OwnerNode == null)
                continue;
            var itemId = ItemAt(inventory, sorter, first + i);
            var glam = KeptForGlam(itemId);
            var later = KeptForLater(itemId);
            if (!glam && !later)
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
            if (glam)
                drawList.AddRect(min, max, color, Rounding * scale, ImDrawFlags.None, Thickness * scale);
            if (later)
            {
                // On a dark disc in the top-right corner, so it reads over any item icon.
                var size = node->Width * scale * BadgeSize;
                var center = new Vector2(max.X - size * 0.7f, min.Y + size * 0.7f);
                drawList.AddCircleFilled(center, size * 0.7f, BadgeBackground);
                TablerIcons.DrawAt(drawList, TablerIcon.Bookmark, center - new Vector2(size / 2), size, laterColor);
            }
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

        var kept = Names(config.Items.Concat(config.LaterItems));
        foreach (var name in ItemLists.Addons)
        {
            var addon = (AtkUnitBase*)gameGui.GetAddonByName(name).Address;
            if (addon == null)
                continue;
            var list = List(addon);
            if (list == null)
                continue;
            var rows = new List<string>();
            foreach (var row in Rows(list))
            {
                var uld = &row.Value->AtkComponentButton.AtkComponentBase.UldManager;
                if (rows.Count < 8)
                    rows.Add($"[{string.Join("|", Texts(uld))}]" + (NameNode(uld, kept) != null ? " kept" : ""));
            }
            log.Information($"{name}: visible {addon->IsVisible}, {list->GetComponentType()} {list->ListLength} long, rows {string.Join(" ", rows)}");
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
