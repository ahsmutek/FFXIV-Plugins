using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GlamKit;

internal sealed class GlamOff : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly IGameGui gameGui;
    private readonly Configuration config;

    public GlamOff(IContextMenu contextMenu, IGameGui gameGui, Configuration config)
    {
        this.contextMenu = contextMenu;
        this.gameGui = gameGui;
        this.config = config;
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!config.GlamOffEnabled)
            return;

        uint itemId;
        byte stain0, stain1;
        bool crest;
        if (ItemLists.Lists(args.AddonName))
        {
            if (Listed(ItemLists.MenuItemId(args, gameGui)) is not { } listed)
                return;
            (itemId, stain0, stain1, crest) = listed;
        }
        else if (args.MenuType == ContextMenuType.Inventory &&
                 args.Target is MenuTargetInventory { TargetItem: { GlamourId: not 0 } item })
        {
            itemId = item.BaseItemId;
            var stains = item.Stains;
            stain0 = stains.Length > 0 ? stains[0] : (byte)0;
            stain1 = stains.Length > 1 ? stains[1] : (byte)0;
            crest = item.IsCompanyCrestApplied;
        }
        else
        {
            return;
        }

        var addonId = AddonId(args.AddonPtr);
        args.AddMenuItem(new MenuItem
        {
            Name = "Glam Off",
            Prefix = SeIconChar.BoxedLetterG,
            PrefixColor = 37,
            // Glamour ID 0 makes the Fitting Room show the item's own model.
            OnClicked = _ => AgentTryon.TryOn(addonId, itemId, stain0, stain1, 0, crest),
        });
    }

    private static unsafe (uint ItemId, byte Stain0, byte Stain1, bool Crest)? Listed(uint baseItemId)
    {
        var item = ItemLists.GlamouredCopy(baseItemId);
        return item == null ? null :
               (baseItemId, item->GetStain(0), item->GetStain(1), item->Flags.HasFlag(InventoryItem.ItemFlags.CompanyCrestApplied));
    }

    private static unsafe uint AddonId(nint addonPtr) => addonPtr == 0 ? 0u : ((AtkUnitBase*)addonPtr)->Id;
}
