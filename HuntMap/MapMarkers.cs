using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace HuntMap;

// Adds the target's spawn points to the game map's own markers. They're appended after the game's, rather
// than replacing them, so aetherytes and the rest stay; the game rebuilds the list when the map opens or
// changes, so they're put back whenever they're gone.
internal sealed unsafe class MapMarkers : IDisposable
{
    public const uint UncheckedIcon = 60576;
    public const uint CheckedIcon = 60575;
    private const string AreaMap = "AreaMap";

    private readonly IFramework framework;
    private readonly IAddonLifecycle lifecycle;
    private readonly Bills bills;
    private readonly Configuration config;

    private int start = -1;
    private int count;
    private bool redraw = true;

    public MapMarkers(IFramework framework, IAddonLifecycle lifecycle, Bills bills, Configuration config)
    {
        this.framework = framework;
        this.lifecycle = lifecycle;
        this.bills = bills;
        this.config = config;
        framework.Update += OnUpdate;
        lifecycle.RegisterListener(AddonEvent.PostSetup, AreaMap, OnMapChanged);
        lifecycle.RegisterListener(AddonEvent.PostRefresh, AreaMap, OnMapChanged);
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        lifecycle.UnregisterListener(AddonEvent.PostSetup, AreaMap, OnMapChanged);
        lifecycle.UnregisterListener(AddonEvent.PostRefresh, AreaMap, OnMapChanged);
        if (AgentMap.Instance() is var agent && agent != null)
            Remove(agent);
    }

    // After a spot is checked or unchecked.
    public void Redraw() => redraw = true;

    private void OnMapChanged(AddonEvent type, AddonArgs args) => redraw = true;

    private void OnUpdate(IFramework _)
    {
        var agent = AgentMap.Instance();
        if (agent == null)
            return;

        var target = bills.Held().FirstOrDefault(t => t.MapId == agent->SelectedMapId && Bills.Kills(t) < t.Needed);
        if (target == null)
        {
            Remove(agent);
            return;
        }
        if (!redraw && Ours(agent))
            return;

        redraw = false;
        Remove(agent);
        start = agent->MapMarkerCount;
        var checks = config.Checked.GetValueOrDefault(target.Bill);
        var checkedSpots = checks?.Order == target.Order ? checks.Spots : null;
        foreach (var spot in target.Spots)
        {
            var icon = checkedSpots?.Contains(spot.Index) == true ? CheckedIcon : UncheckedIcon;
            // The list is shared with the game's markers and holds 132, so a long list can be cut short.
            if (!agent->AddMapMarker(new Vector3(spot.World.X, 0, spot.World.Y), icon))
                break;
            count++;
        }
    }

    // Whether the markers we added are still there, as we left them at the end of the list.
    private bool Ours(AgentMap* agent)
    {
        if (start < 0 || count == 0 || agent->MapMarkerCount != start + count)
            return false;
        for (var i = start; i < start + count; i++)
        {
            if (agent->MapMarkers[i].MapMarker.IconId is not (UncheckedIcon or CheckedIcon))
                return false;
        }
        return true;
    }

    private void Remove(AgentMap* agent)
    {
        if (Ours(agent))
            agent->MapMarkerCount = (byte)start;
        start = -1;
        count = 0;
    }
}
