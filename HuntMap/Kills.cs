using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace HuntMap;

internal sealed class Kills : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);

    private readonly IFramework framework;
    private readonly IClientState client;
    private readonly IObjectTable objects;
    private readonly Configuration config;
    private readonly MapMarkers markers;
    private DateTime nextAt;

    public Kills(IFramework framework, IClientState client, IObjectTable objects, Configuration config, MapMarkers markers)
    {
        this.framework = framework;
        this.client = client;
        this.objects = objects;
        this.config = config;
        this.markers = markers;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    private void OnUpdate(IFramework _)
    {
        if (!config.EndHuntOnKill || DateTime.UtcNow < nextAt || markers.Target is not { } mark ||
            mark.Map.TerritoryId != client.TerritoryType)
            return;
        nextAt = DateTime.UtcNow + Every;

        foreach (var obj in objects)
        {
            // Some names are stored lowercase, so they're compared ignoring case.
            if (obj is IBattleNpc { IsDead: true } npc &&
                npc.Name.TextValue.Equals(mark.Name, StringComparison.OrdinalIgnoreCase))
            {
                markers.Hunt(null);
                return;
            }
        }
    }
}
