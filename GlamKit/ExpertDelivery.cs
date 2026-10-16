using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Text.ReadOnly;

namespace GlamKit;

// The Grand Company Expert Delivery window.
internal static unsafe class ExpertDelivery
{
    public const string Addon = "GrandCompanySupplyList";
    private const uint ItemIdModifier = 500_000;

    // Its item menu doesn't say which item it's for, so it's the one whose tooltip was last shown.
    public static uint MenuItemId(IMenuOpenedArgs args, IGameGui gameGui)
    {
        if (args.Target is MenuTargetInventory { TargetItem: { BaseItemId: not 0 } item })
            return item.BaseItemId;
        var agent = AgentItemDetail.Instance();
        return (uint)((agent != null && agent->ItemId != 0 ? agent->ItemId : gameGui.HoveredItem) % ItemIdModifier);
    }

    // Names are wrapped in colour codes.
    public static string Plain(ref Utf8String text) =>
        new ReadOnlySeString(text.AsSpan().ToArray()).ExtractText();
}
