using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace KeepInMind;

// Ticks only when the count reaches the need, so un-ticking by hand sticks.
internal sealed class Stock : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan CheckStorageEvery = TimeSpan.FromSeconds(2);

    private static readonly uint[] RetainerTypes =
    [
        (uint)InventoryType.RetainerPage1, (uint)InventoryType.RetainerPage2, (uint)InventoryType.RetainerPage3,
        (uint)InventoryType.RetainerPage4, (uint)InventoryType.RetainerPage5, (uint)InventoryType.RetainerPage6,
        (uint)InventoryType.RetainerPage7, (uint)InventoryType.RetainerCrystals,
    ];

    private static readonly uint[] SaddlebagTypes =
    [
        (uint)InventoryType.SaddleBag1, (uint)InventoryType.SaddleBag2,
        (uint)InventoryType.PremiumSaddleBag1, (uint)InventoryType.PremiumSaddleBag2,
    ];

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly Action save;
    private readonly ICallGateSubscriber<bool> storageReady;
    private readonly ICallGateSubscriber<uint, bool, uint[], uint> storageCount;

    private Dictionary<uint, (int Needed, bool Enough)> last = [];
    private DateTime nextCheck;
    private Dictionary<uint, (int Retainers, int Saddlebag)> stored = [];
    private DateTime nextStorageCheck;

    public Stock(IFramework framework, IClientState clientState, IDalamudPluginInterface pluginInterface,
                 IPluginLog log, Configuration config, Action save)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.log = log;
        this.config = config;
        this.save = save;
        storageReady = pluginInterface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        storageCount = pluginInterface.GetIpcSubscriber<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    public static unsafe int Have(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return 0;
        return inventory->GetInventoryItemCount(itemId, false, false, false) +
               inventory->GetInventoryItemCount(itemId, true, false, false);
    }

    public (int Retainers, int Saddlebag) Stored(uint itemId) => stored.GetValueOrDefault(itemId);

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

        if (DateTime.UtcNow >= nextStorageCheck)
        {
            nextStorageCheck = DateTime.UtcNow + CheckStorageEvery;
            CheckStorage();
        }
    }

    // Allagan Tools remembers retainers and the saddlebag from when they were last opened.
    private void CheckStorage()
    {
        var now = new Dictionary<uint, (int Retainers, int Saddlebag)>(config.Items.Count);
        try
        {
            if (storageReady.InvokeFunc())
            {
                foreach (var kept in config.Items)
                {
                    var retainers = (int)storageCount.InvokeFunc(kept.ItemId, true, RetainerTypes);
                    var saddlebag = (int)storageCount.InvokeFunc(kept.ItemId, true, SaddlebagTypes);
                    if (retainers > 0 || saddlebag > 0)
                        now[kept.ItemId] = (retainers, saddlebag);
                }
            }
        }
        catch (IpcNotReadyError)
        {
        }
        catch (IpcError e)
        {
            log.Debug(e, "Allagan Tools didn't answer ItemCountOwned.");
        }
        stored = now;
    }
}
