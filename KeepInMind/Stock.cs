using System;
using System.Collections.Generic;
using System.Linq;
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

    // Allagan Tools' InventoryCategory: RetainerBags and Crystals, which also holds your own.
    private static readonly uint[] RetainerCategories = [4, 13];

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
    private readonly ICallGateSubscriber<uint, bool, uint[], bool, Dictionary<ulong, uint>> countsByCharacter;
    private readonly ICallGateSubscriber<ulong> currentCharacter;

    private Dictionary<uint, (int Needed, bool Enough)> last = [];
    private DateTime nextCheck;
    private Dictionary<uint, (int Retainers, int Saddlebag)> stored = [];
    private Dictionary<uint, List<(ulong Retainer, int Count)>> holders = [];
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
        countsByCharacter = pluginInterface.GetIpcSubscriber<uint, bool, uint[], bool, Dictionary<ulong, uint>>(
            "AllaganTools.GetItemCountsByCharacter");
        currentCharacter = pluginInterface.GetIpcSubscriber<ulong>("AllaganTools.CurrentCharacter");
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

    // Most first.
    public IReadOnlyList<(ulong Retainer, int Count)> Holders(uint itemId) =>
        holders.TryGetValue(itemId, out var list) ? list : [];

    public string RetainerName(ulong retainerId) =>
        config.Retainers.Find(r => r.Id == retainerId)?.Name is { Length: > 0 } name ? name : "Retainer";

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
        RememberRetainers();
        var now = new Dictionary<uint, (int Retainers, int Saddlebag)>(config.Items.Count);
        var nowHolders = new Dictionary<uint, List<(ulong Retainer, int Count)>>();
        try
        {
            if (storageReady.InvokeFunc())
            {
                var player = currentCharacter.InvokeFunc();
                foreach (var kept in config.Items)
                {
                    var byRetainer = countsByCharacter.InvokeFunc(kept.ItemId, true, RetainerCategories, false)
                                                      .Where(c => c.Key != player && c.Value > 0)
                                                      .Select(c => (c.Key, (int)c.Value))
                                                      .OrderByDescending(c => c.Item2)
                                                      .ToList();
                    var retainers = byRetainer.Sum(c => c.Item2);
                    var saddlebag = (int)storageCount.InvokeFunc(kept.ItemId, true, SaddlebagTypes);
                    if (retainers > 0 || saddlebag > 0)
                        now[kept.ItemId] = (retainers, saddlebag);
                    if (byRetainer.Count > 0)
                        nowHolders[kept.ItemId] = byRetainer;
                }
            }
        }
        catch (IpcNotReadyError)
        {
        }
        catch (Exception e)
        {
            log.Debug(e, "Allagan Tools didn't answer an item count.");
        }
        stored = now;
        holders = nowHolders;
    }

    private unsafe void RememberRetainers()
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
            return;

        var seen = new List<KnownRetainer>();
        foreach (ref var r in manager->Retainers)
        {
            if (r.RetainerId != 0 && r.Available)
                seen.Add(new KnownRetainer { Id = r.RetainerId, Name = r.NameString });
        }
        if (seen.Count == 0 ||
            seen.Select(r => (r.Id, r.Name)).SequenceEqual(config.Retainers.Select(r => (r.Id, r.Name))))
        {
            return;
        }
        config.Retainers = seen;
        save();
    }
}
