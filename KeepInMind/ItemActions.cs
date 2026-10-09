using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace KeepInMind;

// Item Vendor Location's menu entry only appears in the game's own menus, so its IPC is used instead.
internal sealed class ItemActions
{
    private const string VendorPlugin = "ItemVendorLocation";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly Sources sources;
    private readonly ICallGateSubscriber<uint, bool, HashSet<(uint npcId, uint territory, (float x, float y))>?> getItemVendors;
    private readonly ICallGateSubscriber<uint, object?> openVendorResults;

    private static readonly TimeSpan VendorCheckEvery = TimeSpan.FromSeconds(2);

    private readonly Dictionary<uint, bool> soldByVendor = [];
    private bool vendorPluginWasLoaded;
    private bool vendorUnanswered;
    private DateTime nextVendorCheck;

    public ItemActions(IDalamudPluginInterface pluginInterface, Sources sources, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.sources = sources;
        this.log = log;
        getItemVendors = pluginInterface.GetIpcSubscriber<uint, bool, HashSet<(uint, uint, (float, float))>?>(
            "ItemVendorLocation.GetItemVendors");
        openVendorResults = pluginInterface.GetIpcSubscriber<uint, object?>("ItemVendorLocation.OpenVendorResults");
    }

    public unsafe bool OpenLog(uint itemId)
    {
        if (sources.Gatherables.Contains(itemId))
            AgentGatheringNote.Instance()->OpenGatherableByItemId((ushort)itemId);
        else if (sources.Fish.Contains(itemId) || sources.Spearfish.Contains(itemId))
            AgentFishGuide.Instance()->OpenForItemId(itemId, sources.Spearfish.Contains(itemId));
        else if (sources.Craftables.Contains(itemId))
            AgentRecipeNote.Instance()->SearchRecipeByItemId(itemId);
        else
            return false;
        return true;
    }

    public bool VendorPluginLoaded => IsLoaded(VendorPlugin);

    public Source SourcesOf(uint itemId) => sources.Of(itemId) | (SoldByVendor(itemId) ? Source.Vendor : Source.None);

    // The list asks every frame, so answers are kept and the plugin is only re-checked now and then.
    public bool SoldByVendor(uint itemId)
    {
        if (DateTime.UtcNow >= nextVendorCheck)
        {
            nextVendorCheck = DateTime.UtcNow + VendorCheckEvery;
            vendorUnanswered = false;
            var loaded = VendorPluginLoaded;
            if (loaded != vendorPluginWasLoaded)
            {
                soldByVendor.Clear();
                vendorPluginWasLoaded = loaded;
            }
        }
        if (soldByVendor.TryGetValue(itemId, out var sold))
            return sold;
        if (!vendorPluginWasLoaded || vendorUnanswered)
            return false;

        if (HasVendors(itemId) is not { } answer)
        {
            vendorUnanswered = true;
            return false;
        }
        soldByVendor[itemId] = answer;
        return answer;
    }

    private bool? HasVendors(uint itemId)
    {
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
