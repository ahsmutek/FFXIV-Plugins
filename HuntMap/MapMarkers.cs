using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace HuntMap;

// Spawn points are appended after the game's markers so aetherytes and the rest stay; the game rebuilds the lists
// when the map opens or changes, so they're put back whenever they're gone.
internal sealed unsafe class MapMarkers : IDisposable
{
    public const uint DefaultUncheckedIcon = 60442;
    public const uint DefaultCheckedIcon = 60408;
    private const string AreaMap = "AreaMap";

    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IClientState client;
    private readonly Configuration config;
    private readonly Action save;
    private readonly Bills bills;
    private readonly Checks checks;

    private readonly Drawn map = new();
    private readonly Drawn minimap = new();

    public MapMarkers(IFramework framework, IGameGui gameGui, IClientState client, Configuration config, Action save,
                      Marks marks, Bills bills, Checks checks)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.client = client;
        this.config = config;
        this.save = save;
        this.bills = bills;
        this.checks = checks;
        Target = config.Target is { } key ? marks.ByKey(key) : null;
        framework.Update += OnUpdate;
    }

    // The mark being hunted; on its map it wins over a bill's target.
    public Mark? Target { get; private set; }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        if (AgentMap.Instance() is var agent && agent != null)
        {
            Remove(agent, map, false);
            Remove(agent, minimap, true);
        }
    }

    // Starting or ending a hunt, rather than flagging another spot of the one going on, starts its checks over.
    public void Hunt(Mark? mark)
    {
        if (mark != Target)
        {
            if (Target != null)
                checks.Clear(Target);
            if (mark != null)
                checks.Clear(mark);
        }
        if (mark == null && Target != null)
            config.StoppedMarks.Add(Target.Key);
        if (mark != null)
            config.StoppedMarks.Remove(mark.Key);
        Target = mark;
        config.Target = mark?.Key;
        save();
        Redraw();
    }

    public void Redraw() => map.Redraw = minimap.Redraw = true;

    private void OnUpdate(IFramework _)
    {
        var agent = AgentMap.Instance();
        if (agent == null)
            return;

        // Adding markers refreshes the map, so redrawing on each refresh kept it refreshing and taking focus.
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(AreaMap).Address;
        if (addon != null && addon->IsVisible)
            Update(agent, map, MarkOn(m => m.Map.MapId == agent->SelectedMapId), false);

        Update(agent, minimap, MarkOn(m => m.Map.TerritoryId == client.TerritoryType), true);
    }

    private Mark? MarkOn(Func<Mark, bool> here) =>
        Target != null && here(Target)
            ? Target
            : bills.Held().FirstOrDefault(t => here(t.Mark) && !Bills.Done(t) && !config.StoppedMarks.Contains(t.Mark.Key))?.Mark;

    private void Update(AgentMap* agent, Drawn drawn, Mark? mark, bool mini)
    {
        if (mark == null)
        {
            Remove(agent, drawn, mini);
            return;
        }
        if (!drawn.Redraw && mark == drawn.Mark && Ours(agent, drawn, mini))
            return;

        drawn.Redraw = false;
        Remove(agent, drawn, mini);
        drawn.Mark = mark;
        drawn.Unchecked = config.UncheckedIcon;
        drawn.Checked = config.CheckedIcon;
        drawn.Start = mini ? agent->MiniMapMarkerCount : agent->MapMarkerCount;
        var checkedSpots = checks.Of(mark);
        foreach (var spot in mark.Spots)
        {
            var done = checkedSpots?.Contains(spot.Index) == true;
            if (done && config.HideCheckedOnMap)
                continue;
            var icon = done ? drawn.Checked : drawn.Unchecked;
            var at = new Vector3(spot.World.X, 0, spot.World.Y);
            // The lists are shared with the game's markers and are fixed-size, so a long list can be cut short.
            if (!(mini ? agent->AddMiniMapMarker(at, icon) : agent->AddMapMarker(at, icon)))
                break;
            drawn.Count++;
        }
    }

    // Whether the markers we added are still there, as we left them at the end of the list.
    private static bool Ours(AgentMap* agent, Drawn drawn, bool mini)
    {
        var total = mini ? agent->MiniMapMarkerCount : agent->MapMarkerCount;
        if (drawn.Start < 0 || (drawn.Count > 0 && total != drawn.Start + drawn.Count))
            return false;
        for (var i = drawn.Start; i < drawn.Start + drawn.Count; i++)
        {
            var icon = mini ? agent->MiniMapMarkers[i].MapMarker.IconId : agent->MapMarkers[i].MapMarker.IconId;
            if (icon != drawn.Unchecked && icon != drawn.Checked)
                return false;
        }
        return true;
    }

    // The game can add its own markers after ours, so ours are taken out wherever they are, not just from the end.
    private static void Remove(AgentMap* agent, Drawn drawn, bool mini)
    {
        if (drawn.Start >= 0)
        {
            var total = mini ? agent->MiniMapMarkerCount : agent->MapMarkerCount;
            var kept = 0;
            for (var i = 0; i < total; i++)
            {
                var icon = mini ? agent->MiniMapMarkers[i].MapMarker.IconId : agent->MapMarkers[i].MapMarker.IconId;
                if (i >= drawn.Start && (icon == drawn.Unchecked || icon == drawn.Checked))
                    continue;
                if (kept != i)
                {
                    if (mini)
                        agent->MiniMapMarkers[kept] = agent->MiniMapMarkers[i];
                    else
                        agent->MapMarkers[kept] = agent->MapMarkers[i];
                }
                kept++;
            }
            if (mini)
                agent->MiniMapMarkerCount = (byte)kept;
            else
                agent->MapMarkerCount = (byte)kept;
        }
        drawn.Mark = null;
        drawn.Start = -1;
        drawn.Count = 0;
    }

    private sealed class Drawn
    {
        public Mark? Mark;
        public int Start = -1;
        public int Count;
        public uint Unchecked;
        public uint Checked;
        public bool Redraw = true;
    }
}
