using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;

namespace HuntMap;

internal sealed class Arrivals : IDisposable
{
    private const float Radius = 20f;
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);

    private readonly IFramework framework;
    private readonly IClientState client;
    private readonly IObjectTable objects;
    private readonly Configuration config;
    private readonly Bills bills;
    private readonly Checks checks;
    private readonly MapMarkers markers;
    private DateTime nextAt;

    public Arrivals(IFramework framework, IClientState client, IObjectTable objects, Configuration config, Bills bills,
                    Checks checks, MapMarkers markers)
    {
        this.framework = framework;
        this.client = client;
        this.objects = objects;
        this.config = config;
        this.bills = bills;
        this.checks = checks;
        this.markers = markers;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    private void OnUpdate(IFramework _)
    {
        if (!config.CheckOnArrival || DateTime.UtcNow < nextAt || objects.LocalPlayer is not { } player)
            return;
        nextAt = DateTime.UtcNow + Every;

        var at = new Vector2(player.Position.X, player.Position.Z);
        var marks = bills.Held().Where(t => !Bills.Done(t)).Select(t => t.Mark).Append(markers.Target)
                         .Where(m => m != null && m.Map.TerritoryId == client.TerritoryType).Distinct();
        var changed = false;
        foreach (var mark in marks)
        {
            foreach (var spot in mark!.Spots.Where(s => Vector2.Distance(s.World, at) <= Radius))
                changed |= checks.Check(mark, spot.Index);
        }
        if (changed)
            markers.Redraw();
    }
}
