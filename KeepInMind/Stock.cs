using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace KeepInMind;

// Ticks an item once your inventory holds as many as it needs. Only when the count reaches
// it, not every time it's checked, so un-ticking an item by hand sticks until the count
// drops below and climbs back (or how many you need changes). Running out again, e.g. by
// crafting with them, leaves it ticked.
internal sealed class Stock : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.5);

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly Action save;

    // Per item, whether it had enough at the last check, and for how many needed. Only items
    // still on the list, so one removed and added again counts afresh.
    private Dictionary<uint, (int Needed, bool Enough)> last = [];
    private DateTime nextCheck;

    public Stock(IFramework framework, IClientState clientState, Configuration config, Action save)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.config = config;
        this.save = save;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    // NQ and HQ together, in your bags and crystals; not the armoury chest, retainers or
    // saddlebags.
    public static unsafe int Have(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return 0;
        return inventory->GetInventoryItemCount(itemId, false, false, false) +
               inventory->GetInventoryItemCount(itemId, true, false, false);
    }

    private void OnUpdate(IFramework _)
    {
        if (DateTime.UtcNow < nextCheck || !clientState.IsLoggedIn)
            return;
        nextCheck = DateTime.UtcNow + CheckEvery;

        var changed = false;
        var now = new Dictionary<uint, (int Needed, bool Enough)>(config.Items.Count);
        foreach (var kept in config.Items)
        {
            var enough = Have(kept.ItemId) >= kept.Needed;
            var reached = enough && !(last.TryGetValue(kept.ItemId, out var was) && was.Needed == kept.Needed && was.Enough);
            now[kept.ItemId] = (kept.Needed, enough);
            if (reached && !kept.Done)
            {
                kept.Done = true;
                changed = true;
            }
        }
        last = now;
        if (changed)
            save();
    }
}
