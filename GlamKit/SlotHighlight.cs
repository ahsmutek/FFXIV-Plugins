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
using Inventory;
using Lumina.Excel.Sheets;

namespace GlamKit;

// Borders the bag, saddlebag, retainer and Armoury Chest slots holding gear kept for glam, as Keep in Mind does,
// and in teal those holding gear kept for later.
internal sealed unsafe class SlotHighlight
{
    private const float Thickness = 2f;
    private const float Rounding = 6f;
    private const float IconGap = 8f;

    private const string Armoury = "ArmouryBoard";

    private readonly IGameGui gameGui;
    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly InventorySlots slots;
    private readonly Dictionary<uint, string> names = [];

    public SlotHighlight(IGameGui gameGui, IDataManager data, Configuration config)
    {
        this.gameGui = gameGui;
        this.data = data;
        this.config = config;
        slots = new InventorySlots(gameGui, data);
    }

    public void Draw()
    {
        var anyGlam = config.GlamKeeperEnabled && config.Items.Count > 0;
        var anyLater = config.KeepForLaterEnabled && config.LaterItems.Count > 0;
        if (!anyGlam && !anyLater)
            return;

        var found = slots.BagSlots();
        if (slots.Addon<AddonArmouryBoard>(Armoury) is var armoury && armoury != null &&
            ArmourySorter(armoury) is var armourySorter && armourySorter != null)
        {
            slots.Add(found, &armoury->AtkUnitBase, armoury->Slots, 0, armourySorter);
        }
        DrawBorders(found);

        foreach (var name in ItemLists.Addons)
            DrawItemList(name);
        DrawVentureReport();
    }

    private void DrawBorders(List<InventorySlots.Slot> found)
    {
        var drawList = ImGui.GetBackgroundDrawList();
        var color = ImGui.GetColorU32(config.KeeperColor);
        var laterColor = ImGui.GetColorU32(config.LaterColor);
        foreach (var slot in found)
        {
            var glam = KeptForGlam(slot.ItemId);
            var later = KeptForLater(slot.ItemId);
            var scale = slot.Scale;
            if (glam)
                drawList.AddRect(slot.Min, slot.Max, color, Rounding * scale, ImDrawFlags.None, Thickness * scale);
            if (later)
            {
                // Inside the glam border when the item has both.
                var inset = new Vector2(glam ? Thickness * scale : 0);
                drawList.AddRect(slot.Min + inset, slot.Max - inset, laterColor, Rounding * scale, ImDrawFlags.None,
                                 Thickness * scale);
            }
        }
    }

    // A row of a window listing gear by name is marked when it shows a kept item's name: a hanger after it for
    // glam, a bookmark for later.
    private void DrawItemList(string addonName)
    {
        var addon = slots.Addon<AtkUnitBase>(addonName);
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
            if (name == null || !InventorySlots.Shown(&name->AtkResNode))
                continue;

            ushort width, height;
            name->GetTextDrawSize(&width, &height, null, 0, -1, false);
            var scale = addon->Scale * name->AtkResNode.ScaleX;
            var size = height * scale;
            var min = offset + new Vector2(name->AtkResNode.ScreenX + (width + IconGap) * scale,
                                           name->AtkResNode.ScreenY + (name->AtkResNode.Height * scale - size) / 2);
            var text = ItemLists.Plain(name);
            DrawMarks(drawList, addon, min, size, IconGap * scale, glamNames.Contains(text), laterNames.Contains(text));
        }
        drawList.PopClipRect();
    }

    // The venture report shows its rewards as icons only, so a kept reward gets its marks beside its icon.
    private void DrawVentureReport()
    {
        var addon = slots.Addon<AtkUnitBase>(ItemLists.VentureReport);
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
            var icon = IconNode(&addon->UldManager, slots.Icon(itemId));
            if (icon == null || icon->OwnerNode == null || !InventorySlots.Shown(&icon->OwnerNode->AtkResNode))
                continue;

            var node = &icon->OwnerNode->AtkResNode;
            var scale = addon->Scale * node->ScaleX;
            var size = node->Height * scale / 2;
            var min = offset + new Vector2(node->ScreenX + (node->Width + IconGap) * scale,
                                           node->ScreenY + (node->Height * scale - size) / 2);
            DrawMarks(drawList, addon, min, size, IconGap * scale, glam, later);
        }
    }

    // A hanger, then a bookmark, in a row from min.
    private void DrawMarks(ImDrawListPtr drawList, AtkUnitBase* owner, Vector2 min, float size, float gap, bool glam,
                           bool later)
    {
        var count = (glam ? 1 : 0) + (later ? 1 : 0);
        var width = count * size + (count - 1) * gap;
        if (count == 0 || InventorySlots.Covered(owner, min, min + new Vector2(width, size)))
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
    private ItemOrderModuleSorter* ArmourySorter(AddonArmouryBoard* armoury)
    {
        var module = ItemOrderModule.Instance();
        var inventory = InventoryManager.Instance();
        if (module == null || inventory == null)
            return null;
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
                var itemId = InventorySlots.ItemAt(inventory, sorter, i);
                if (slot != null && itemId != 0 && InventorySlots.IconOf(slot) == slots.Icon(itemId))
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

    // For /glamkit debug: what each inventory window the highlight looks at shows.
    public void Dump(IPluginLog log)
    {
        slots.Dump(log);
        if ((AddonArmouryBoard*)gameGui.GetAddonByName(Armoury).Address is var armoury && armoury != null)
        {
            var shown = new List<string>();
            foreach (var pointer in armoury->Slots)
            {
                if (pointer.Value != null && shown.Count < 6)
                    shown.Add(pointer.Value->GetIconId().ToString());
            }
            var sorter = ArmourySorter(armoury);
            log.Information($"{Armoury}: visible {armoury->AtkUnitBase.IsVisible}, tab {armoury->TabIndex}, sorter " +
                            $"{(sorter == null ? "none" : sorter->InventoryType.ToString())}, first icons {string.Join(" ", shown)}");
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

}
