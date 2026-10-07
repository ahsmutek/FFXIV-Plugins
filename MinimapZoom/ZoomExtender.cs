using System;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;

namespace MinimapZoom;

// The game's click handler stops at its own limit, so below it the zoom is written after the game handles the click.
internal sealed class ZoomExtender : IDisposable
{
    // A different zoom within this window counts as the game's reset, not the player's.
    private const long RestoreWindowMs = 5000;

    private readonly IFramework framework;
    private readonly IAddonLifecycle lifecycle;
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly Action save;
    private readonly IPluginLog log;

    private float? beforeClick;
    private long restoreUntil;

    public ZoomExtender(IFramework framework, IAddonLifecycle lifecycle, IClientState clientState, Configuration config,
                        Action save, IPluginLog log)
    {
        this.framework = framework;
        this.lifecycle = lifecycle;
        this.clientState = clientState;
        this.config = config;
        this.save = save;
        this.log = log;
        lifecycle.RegisterListener(AddonEvent.PreReceiveEvent, NaviMap.AddonName, OnPreEvent);
        lifecycle.RegisterListener(AddonEvent.PostReceiveEvent, NaviMap.AddonName, OnPostEvent);
        lifecycle.RegisterListener(AddonEvent.PostSetup, NaviMap.AddonName, OnMinimapSetup);
        clientState.TerritoryChanged += OnTerritoryChanged;
        clientState.ClassJobChanged += OnClassJobChanged;
        clientState.Login += OpenRestoreWindow;
        framework.Update += OnUpdate;
        OpenRestoreWindow();
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        clientState.ClassJobChanged -= OnClassJobChanged;
        clientState.Login -= OpenRestoreWindow;
        lifecycle.UnregisterListener(OnPreEvent, OnPostEvent, OnMinimapSetup);
        // Leave the game a zoom it knows, but keep ours for next time.
        ShowGameZoom();
    }

    public void ResetToGame()
    {
        ShowGameZoom();
        if (config.Zoom != null)
        {
            config.Zoom = null;
            save();
        }
    }

    private static void ShowGameZoom()
    {
        if (NaviMap.Zoom() is { } zoom && zoom < NaviMap.GameMinimum - NaviMap.Epsilon)
            NaviMap.SetZoom(NaviMap.GameMinimum);
    }

    private void OpenRestoreWindow() => restoreUntil = Environment.TickCount64 + RestoreWindowMs;
    private void OnTerritoryChanged(uint _) => OpenRestoreWindow();
    private void OnClassJobChanged(uint _) => OpenRestoreWindow();
    private void OnMinimapSetup(AddonEvent type, AddonArgs args) => OpenRestoreWindow();

    private static int? ZoomClick(AddonArgs args) =>
        args is AddonReceiveEventArgs { AtkEventType: AddonEventType.ButtonClick } e &&
        e.EventParam is NaviMap.ZoomInParam or NaviMap.ZoomOutParam
            ? e.EventParam
            : null;

    private void OnPreEvent(AddonEvent type, AddonArgs args)
    {
        beforeClick = config.Enabled && ZoomClick(args) != null ? NaviMap.Zoom() : null;
    }

    private void OnPostEvent(AddonEvent type, AddonArgs args)
    {
        if (beforeClick is not { } before || ZoomClick(args) is not { } param)
            return;
        beforeClick = null;

        // At or below its limit the game ignores − and knows nothing of our zoom for +, so step here.
        if (before > NaviMap.GameMinimum + NaviMap.Epsilon)
            return;

        var target = param == NaviMap.ZoomOutParam
            ? MathF.Max(config.Minimum, before - NaviMap.Step)
            : before + NaviMap.Step;
        if (!NaviMap.SetZoom(target, checkedBefore: true))
            return;

        config.Zoom = target < NaviMap.GameMinimum - NaviMap.Epsilon ? target : null;
        save();
    }

    private void OnUpdate(IFramework _)
    {
        if (!config.Enabled || NaviMap.Zoom() is not { } zoom)
            return;

        if (config.Zoom is { } wanted)
        {
            if (MathF.Abs(zoom - wanted) > NaviMap.Epsilon)
            {
                if (Environment.TickCount64 < restoreUntil)
                {
                    if (NaviMap.SetZoom(wanted))
                        zoom = wanted;
                }
                else
                {
                    config.Zoom = zoom < NaviMap.GameMinimum - NaviMap.Epsilon ? zoom : null;
                    save();
                    log.Information($"Keeping zoom {zoom:0.##}.");
                }
            }
            else if (!NaviMap.PercentInStep(wanted))
            {
                NaviMap.SetZoom(wanted);
            }
        }

        if (zoom <= NaviMap.GameMinimum + NaviMap.Epsilon)
            NaviMap.SetZoomOutEnabled(zoom > config.Minimum + NaviMap.Epsilon);
    }
}
