using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace KeepInMind;

// What Item Vendor Location can do for an item in the list, and which game log it's in.
//
// Item Vendor Location only adds its entry to the game's own context menus, and only for menus
// owned by the game windows it knows (Crafting Log, recipe tree, chat…), reading the item from
// that window's agent. An ImGui window can't open one of those, so the list uses its IPC:
// "ItemVendorLocation.OpenVendorResults" opens its vendor results for an item ID, and
// "ItemVendorLocation.GetItemVendors" tells whether it has any (its menu entry only shows then).
internal sealed class ItemActions
{
    private const string VendorPlugin = "ItemVendorLocation";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<uint, bool, HashSet<(uint npcId, uint territory, (float x, float y))>?> getItemVendors;
    private readonly ICallGateSubscriber<uint, object?> openVendorResults;

    // Which log an item is in: the Gathering Log (miner, botanist), the fish guide, the
    // Crafting Log.
    private readonly FrozenSet<uint> gatherables;
    private readonly FrozenSet<uint> fish;
    private readonly FrozenSet<uint> spearfish;
    private readonly FrozenSet<uint> craftables;

    public ItemActions(IDalamudPluginInterface pluginInterface, IDataManager data, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        getItemVendors = pluginInterface.GetIpcSubscriber<uint, bool, HashSet<(uint, uint, (float, float))>?>(
            "ItemVendorLocation.GetItemVendors");
        openVendorResults = pluginInterface.GetIpcSubscriber<uint, object?>("ItemVendorLocation.OpenVendorResults");

        gatherables = data.GetExcelSheet<GatheringItem>()
                          .Select(g => g.Item.RowId)
                          .Where(id => id is > 0 and < 1000000)
                          .ToFrozenSet();
        fish = data.GetExcelSheet<FishParameter>().Select(f => f.Item.RowId)
                   .Where(id => id is > 0 and < 1000000)
                   .ToFrozenSet();
        spearfish = data.GetExcelSheet<SpearfishingItem>().Select(f => f.Item.RowId)
                        .Where(id => id is > 0 and < 1000000)
                        .ToFrozenSet();
        craftables = data.GetExcelSheet<Recipe>().Select(r => r.ItemResult.RowId)
                         .Where(id => id != 0)
                         .ToFrozenSet();
    }

    public string? LogName(uint itemId) =>
        gatherables.Contains(itemId) ? "Gathering Log"
        : fish.Contains(itemId) || spearfish.Contains(itemId) ? "Fish Guide"
        : craftables.Contains(itemId) ? "Crafting Log"
        : null;

    // Opens the item the way the game's own "Search for Item by Gathering/Crafting Method"
    // does. Gathering first: the list is for things to fetch by hand. False when it's in no log.
    public unsafe bool OpenLog(uint itemId)
    {
        if (gatherables.Contains(itemId))
            AgentGatheringNote.Instance()->OpenGatherableByItemId((ushort)itemId);
        else if (fish.Contains(itemId) || spearfish.Contains(itemId))
            AgentFishGuide.Instance()->OpenForItemId(itemId, spearfish.Contains(itemId));
        else if (craftables.Contains(itemId))
            AgentRecipeNote.Instance()->SearchRecipeByItemId(itemId);
        else
            return false;
        return true;
    }

    public bool VendorPluginLoaded => IsLoaded(VendorPlugin);


    public bool? HasVendors(uint itemId)
    {
        if (!VendorPluginLoaded)
            return null;
        try
        {
            return getItemVendors.InvokeFunc(itemId, false) is { Count: > 0 };
        }
        catch (IpcError e)
        {
            log.Debug(e, "Item Vendor Location didn't answer GetItemVendors.");
            return null;
        }
    }

    public void ShowVendors(uint itemId)
    {
        try
        {
            openVendorResults.InvokeFunc(itemId);
        }
        catch (IpcError e)
        {
            log.Warning(e, "Couldn't open Item Vendor Location's results.");
        }
    }


    private bool IsLoaded(string internalName) =>
        pluginInterface.InstalledPlugins.Any(p => p.IsLoaded &&
                                                  string.Equals(p.InternalName, internalName, StringComparison.Ordinal));
}
