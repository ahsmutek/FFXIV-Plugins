using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Text.ReadOnly;

namespace GlamKit;

// Game windows that list gear by name: Expert Delivery, the armoire and Glamour Creation.
internal static unsafe class ItemLists
{
    public static readonly string[] Addons = ["GrandCompanySupplyList", "Cabinet", "MiragePrismPrismBoxCrystallize"];

    private const uint ItemIdModifier = 500_000;

    private static readonly InventoryType[] Held =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.EquippedItems,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead,
        InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryWaist,
        InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
    ];

    public static bool Lists(string? addonName) => Addons.Contains(addonName);

    // Their item menus don't say which item they're for, so it's the one whose tooltip was last shown.
    public static uint MenuItemId(IMenuOpenedArgs args, IGameGui gameGui)
    {
        if (args.Target is MenuTargetInventory { TargetItem: { BaseItemId: not 0 } item })
            return item.BaseItemId;
        var agent = AgentItemDetail.Instance();
        return (uint)((agent != null && agent->ItemId != 0 ? agent->ItemId : gameGui.HoveredItem) % ItemIdModifier);
    }

    // The lists show copies of the gear without its glamour; this is the one in the bags or Armoury Chest.
    public static InventoryItem* GlamouredCopy(uint baseItemId)
    {
        var inventory = InventoryManager.Instance();
        if (baseItemId == 0 || inventory == null)
            return null;
        foreach (var type in Held)
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null)
                continue;
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item != null && item->GlamourId != 0 && item->GetBaseItemId() == baseItemId)
                    return item;
            }
        }
        return null;
    }

    // Names are wrapped in colour codes, and HQ names end in a space and the HQ glyph.
    public static string Plain(ref Utf8String text) =>
        new ReadOnlySeString(text.AsSpan().ToArray()).ExtractText().TrimEnd(' ', '');
}
