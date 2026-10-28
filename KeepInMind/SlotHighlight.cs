using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Inventory;

namespace KeepInMind;

// Borders the bag, saddlebag and retainer slots holding items still needed, in the moneybag's colours.
internal sealed class SlotHighlight
{
    private const float Thickness = 2f;
    private const float Rounding = 6f;

    private readonly InventorySlots slots;
    private readonly Configuration config;
    private readonly Stock stock;

    public SlotHighlight(IGameGui gameGui, IDataManager data, Configuration config, Stock stock)
    {
        slots = new InventorySlots(gameGui, data);
        this.config = config;
        this.stock = stock;
    }

    public void Draw()
    {
        if (config.Items.Count == 0)
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        foreach (var slot in slots.BagSlots())
        {
            if (Wanted(slot.ItemId) is { } color)
                drawList.AddRect(slot.Min, slot.Max, ImGui.GetColorU32(color), Rounding * slot.Scale, ImDrawFlags.None,
                                 Thickness * slot.Scale);
        }
    }

    // For /keepinmind debug.
    public void Dump(IPluginLog log) => slots.Dump(log);

    private Vector4? Wanted(uint itemId)
    {
        if (config.Items.Find(i => i.ItemId == itemId) is not { Done: false } kept)
            return null;
        var stored = stock.Stored(itemId);
        var stillShort = kept.Needed - Stock.Have(itemId) - stored.Retainers - stored.Saddlebag;
        return stillShort <= 0 ? config.StorageEnoughColor : config.StorageColor;
    }
}
