using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GlamKit;

internal sealed class KeeperMenu : IDisposable
{
    private const uint SoulCrystal = 62;

    private readonly IContextMenu contextMenu;
    private readonly IGameGui gameGui;
    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly Action<uint, bool> setKept;
    private readonly Action<uint, bool> setLater;
    private readonly Action<uint, bool> setWished;
    private readonly ushort pink;

    public KeeperMenu(IContextMenu contextMenu, IGameGui gameGui, IDataManager data, Configuration config,
                      Action<uint, bool> setKept, Action<uint, bool> setLater, Action<uint, bool> setWished)
    {
        this.contextMenu = contextMenu;
        this.gameGui = gameGui;
        this.data = data;
        this.config = config;
        this.setKept = setKept;
        this.setLater = setLater;
        this.setWished = setWished;
        pink = ItemTooltip.Nearest(data, ItemTooltip.Pink);
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (ItemLists.IsShop(args.AddonName))
            AddWish(args, ItemLists.MenuItemId(args, gameGui), true);
        else if (ItemLists.ChatLinkItemId(args) is var linked and not 0)
            AddWish(args, linked, true);
        else
            AddKeep(args);
    }

    private void AddKeep(IMenuOpenedArgs args)
    {
        uint itemId;
        var owned = false;
        if (ItemLists.Lists(args.AddonName))
            itemId = ItemLists.MenuItemId(args, gameGui);
        else if (ItemLists.IsVentureReport(args, gameGui))
            itemId = ItemLists.VentureReward(args, gameGui);
        else if (args.MenuType == ContextMenuType.Inventory && args.Target is MenuTargetInventory { TargetItem: { } item })
        {
            itemId = item.BaseItemId;
            owned = true;
        }
        else
            return;
        if (!IsGear(data, itemId))
            return;

        if (config.GlamKeeperEnabled)
        {
            var kept = config.IsKept(itemId);
            args.AddMenuItem(new MenuItem
            {
                Name = kept ? "Stop Keeping for Glam" : "Keep for Glam",
                Prefix = SeIconChar.BoxedLetterG,
                PrefixColor = 37,
                OnClicked = _ => setKept(itemId, !kept),
            });
        }
        if (config.KeepForLaterEnabled)
        {
            var later = config.IsKeptForLater(itemId);
            args.AddMenuItem(new MenuItem
            {
                Name = later ? "Stop Keeping for Later" : "Keep for Later",
                Prefix = SeIconChar.BoxedLetterL,
                PrefixColor = 45,
                OnClicked = _ => setLater(itemId, !later),
            });
        }
        AddWish(args, itemId, !owned);
    }

    // Gear you already hold only offers removing it.
    private void AddWish(IMenuOpenedArgs args, uint itemId, bool canAdd)
    {
        if (!config.WishlistEnabled || itemId == 0 || data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is not { } row ||
            row.Name.IsEmpty)
            return;
        var wished = config.IsWished(itemId);
        if (!wished && !canAdd)
            return;
        args.AddMenuItem(new MenuItem
        {
            Name = wished ? "Remove from Wishlist" : "Add to Wishlist",
            Prefix = SeIconChar.BoxedLetterW,
            PrefixColor = pink,
            OnClicked = _ => setWished(itemId, !wished),
        });
    }

    public static bool IsGear(IDataManager data, uint itemId) =>
        data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } row &&
        row.EquipSlotCategory.RowId != 0 && row.ItemUICategory.RowId != SoulCrystal;
}
