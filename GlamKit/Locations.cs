using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GlamKit;

// Where each kept item is now, for the list. Your own containers are read directly; the saddlebag and retainers
// come from Allagan Tools, which remembers them from when they were last opened.
internal sealed class Locations : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(2);

    private static readonly (string Name, InventoryType[] Types)[] Own =
    [
        ("Equipped", [InventoryType.EquippedItems]),
        ("Armoury Chest", [
            InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead,
            InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryWaist,
            InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
            InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
        ]),
        ("Inventory", [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4]),
    ];

    private static readonly uint[] SaddlebagTypes =
    [
        (uint)InventoryType.SaddleBag1, (uint)InventoryType.SaddleBag2,
        (uint)InventoryType.PremiumSaddleBag1, (uint)InventoryType.PremiumSaddleBag2,
    ];

    // Allagan Tools' InventoryCategory: RetainerBags.
    private static readonly uint[] RetainerCategories = [4];

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly ICallGateSubscriber<bool> storageReady;
    private readonly ICallGateSubscriber<uint, bool, uint[], uint> storageCount;
    private readonly ICallGateSubscriber<uint, bool, uint[], bool, Dictionary<ulong, uint>> countsByCharacter;
    private readonly ICallGateSubscriber<ulong> currentCharacter;

    private Dictionary<uint, string> places = [];
    private DateTime nextCheck;

    public Locations(IFramework framework, IClientState clientState, IDalamudPluginInterface pluginInterface,
                     IPluginLog log, Configuration config)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.log = log;
        this.config = config;
        storageReady = pluginInterface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        storageCount = pluginInterface.GetIpcSubscriber<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
        countsByCharacter = pluginInterface.GetIpcSubscriber<uint, bool, uint[], bool, Dictionary<ulong, uint>>(
            "AllaganTools.GetItemCountsByCharacter");
        currentCharacter = pluginInterface.GetIpcSubscriber<ulong>("AllaganTools.CurrentCharacter");
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    public string Of(uint itemId) => places.GetValueOrDefault(itemId, "Not found");

    private void OnUpdate(IFramework _)
    {
        if (DateTime.UtcNow < nextCheck || !clientState.IsLoggedIn || (!config.GlamKeeperEnabled && !config.KeepForLaterEnabled))
            return;
        nextCheck = DateTime.UtcNow + CheckEvery;

        var now = new Dictionary<uint, string>();
        foreach (var kept in config.Items.Concat(config.LaterItems))
        {
            if (!now.ContainsKey(kept.ItemId) && Find(kept.ItemId) is { } place)
                now[kept.ItemId] = place;
        }
        places = now;
    }

    private string? Find(uint itemId)
    {
        var found = Own.Where(o => o.Types.Any(t => Has(t, itemId))).Select(o => o.Name).ToList();
        try
        {
            if (storageReady.InvokeFunc())
            {
                if (storageCount.InvokeFunc(itemId, true, SaddlebagTypes) > 0)
                    found.Add("Saddlebag");
                var player = currentCharacter.InvokeFunc();
                if (countsByCharacter.InvokeFunc(itemId, true, RetainerCategories, false).Any(c => c.Key != player && c.Value > 0))
                    found.Add("Retainer");
            }
        }
        catch (IpcNotReadyError)
        {
        }
        catch (Exception e)
        {
            log.Debug(e, "Allagan Tools didn't answer an item count.");
        }
        return found.Count == 0 ? null : string.Join(", ", found);
    }

    private static unsafe bool Has(InventoryType type, uint itemId)
    {
        var inventory = InventoryManager.Instance();
        var container = inventory == null ? null : inventory->GetInventoryContainer(type);
        if (container == null)
            return false;
        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot != null && slot->GetBaseItemId() == itemId)
                return true;
        }
        return false;
    }
}
