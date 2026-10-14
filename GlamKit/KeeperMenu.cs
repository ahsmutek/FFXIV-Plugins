using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GlamKit;

// "Keep for Glam" / "Stop Keeping for Glam" in the item menu of gear.
internal sealed class KeeperMenu : IDisposable
{
    private const uint SoulCrystal = 62;

    private readonly IContextMenu contextMenu;
    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly Action<uint, bool> setKept;

    public KeeperMenu(IContextMenu contextMenu, IDataManager data, Configuration config, Action<uint, bool> setKept)
    {
        this.contextMenu = contextMenu;
        this.data = data;
        this.config = config;
        this.setKept = setKept;
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!config.GlamKeeperEnabled)
            return;
        if (args.MenuType != ContextMenuType.Inventory || args.Target is not MenuTargetInventory { TargetItem: { } item })
            return;

        var itemId = item.BaseItemId;
        if (!IsGear(itemId))
            return;

        var kept = config.IsKept(itemId);
        args.AddMenuItem(new MenuItem
        {
            Name = kept ? "Stop Keeping for Glam" : "Keep for Glam",
            Prefix = SeIconChar.BoxedLetterG,
            PrefixColor = 37,
            OnClicked = _ => setKept(itemId, !kept),
        });
    }

    private bool IsGear(uint itemId) =>
        data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } row &&
        row.EquipSlotCategory.RowId != 0 && row.ItemUICategory.RowId != SoulCrystal;
}
