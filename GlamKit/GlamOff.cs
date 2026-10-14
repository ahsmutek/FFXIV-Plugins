using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GlamKit;

// "Glam Off" in the item menu of glamoured gear: the Fitting Room with the item's own look.
internal sealed class GlamOff : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly Configuration config;

    public GlamOff(IContextMenu contextMenu, Configuration config)
    {
        this.contextMenu = contextMenu;
        this.config = config;
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!config.GlamOffEnabled)
            return;
        if (args.MenuType != ContextMenuType.Inventory || args.Target is not MenuTargetInventory { TargetItem: { } item })
            return;
        if (item.GlamourId == 0)
            return;

        var itemId = item.BaseItemId;
        var stains = item.Stains;
        var stain0 = stains.Length > 0 ? stains[0] : (byte)0;
        var stain1 = stains.Length > 1 ? stains[1] : (byte)0;
        var crest = item.IsCompanyCrestApplied;
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

    private static unsafe uint AddonId(nint addonPtr) => addonPtr == 0 ? 0u : ((AtkUnitBase*)addonPtr)->Id;
}
