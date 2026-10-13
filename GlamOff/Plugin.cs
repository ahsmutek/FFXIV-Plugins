using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GlamOff;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static IContextMenu ContextMenu { get; set; } = null!;

    private readonly WindowSystem windows = new("GlamOff");
    private readonly InfoWindow infoWindow = new();

    public Plugin()
    {
        windows.AddWindow(infoWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += infoWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += infoWindow.Toggle;
        ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose()
    {
        ContextMenu.OnMenuOpened -= OnMenuOpened;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= infoWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= infoWindow.Toggle;
        windows.RemoveAllWindows();
    }

    private static void OnMenuOpened(IMenuOpenedArgs args)
    {
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
            Name = "Try On Original",
            Prefix = SeIconChar.BoxedLetterG,
            PrefixColor = 37,
            // Glamour ID 0 makes the Fitting Room show the item's own model.
            OnClicked = _ => AgentTryon.TryOn(addonId, itemId, stain0, stain1, 0, crest),
        });
    }

    private static unsafe uint AddonId(nint addonPtr) => addonPtr == 0 ? 0u : ((AtkUnitBase*)addonPtr)->Id;
}
