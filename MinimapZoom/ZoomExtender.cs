using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Dalamud.Game.Addon.Events;

namespace MinimapZoom;

/// <summary>
/// Lets the minimap's − button keep zooming out past the game's limit, down to the minimum
/// you set, and keeps that zoom when the game resets it. The game's click handler stops at
/// its own limit, so below it the zoom is written here after the game has handled the click.
/// Zooming back in to the game's range hands everything back to the game.
/// </summary>
internal sealed class ZoomExtender : IDisposable
{
    private readonly IFramework framework;
    private readonly IAddonLifecycle lifecycle;
    private readonly Configuration config;
    private readonly Action save;

    // How long after a mouse wheel event a zoom change counts as yours, not a game reset.
    private const long WheelMs = 500;

    // The zoom when a + or − click arrived, before the game handled it.
    private float? beforeClick;
    private long lastWheel;


    public ZoomExtender(IFramework framework, IAddonLifecycle lifecycle, Configuration config, Action save)
    {
        this.framework = framework;
        this.lifecycle = lifecycle;
        this.config = config;
        this.save = save;
        lifecycle.RegisterListener(AddonEvent.PreReceiveEvent, NaviMap.AddonName, OnPreEvent);
        lifecycle.RegisterListener(AddonEvent.PostReceiveEvent, NaviMap.AddonName, OnPostEvent);
        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        lifecycle.UnregisterListener(OnPreEvent, OnPostEvent);
        // Leave the game a zoom it knows, but keep yours for next time (logging in tomorrow).
        ShowGameZoom();
    }

    /// <summary>Back to the game's own furthest zoom, and forget the extended one.</summary>
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

    private static int? ZoomClick(AddonArgs args) =>
        args is AddonReceiveEventArgs { AtkEventType: AddonEventType.ButtonClick } e &&
        e.EventParam is NaviMap.ZoomInParam or NaviMap.ZoomOutParam
            ? e.EventParam
            : null;

    private void OnPreEvent(AddonEvent type, AddonArgs args)
    {
        if (args is AddonReceiveEventArgs { AtkEventType: AddonEventType.MouseWheel })
            lastWheel = Environment.TickCount64;
        beforeClick = config.Enabled && ZoomClick(args) != null ? NaviMap.Zoom() : null;
    }

    private void OnPostEvent(AddonEvent type, AddonArgs args)
    {
        if (beforeClick is not { } before || ZoomClick(args) is not { } param)
            return;
        beforeClick = null;

        // At or below the game's limit the game either does nothing (−) or knows nothing of
        // our zoom (+), so the step is done here.
        var atOrBelowLimit = before <= NaviMap.GameMinimum + NaviMap.Epsilon;
        if (!atOrBelowLimit)
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

        // You zoomed with the mouse wheel: that is the zoom to keep now.
        if (config.Zoom is { } kept && MathF.Abs(zoom - kept) > NaviMap.Epsilon &&
            Environment.TickCount64 - lastWheel < WheelMs)
        {
            config.Zoom = zoom < NaviMap.GameMinimum - NaviMap.Epsilon ? zoom : null;
            save();
        }

        // The game reset the minimap (job change, zoning, login): put the extended zoom back.
        if (config.Zoom is { } wanted && (MathF.Abs(zoom - wanted) > NaviMap.Epsilon || !NaviMap.PercentInStep(wanted)))
        {
            if (NaviMap.SetZoom(wanted))
                zoom = wanted;
        }

        // At or below the game's limit, − stays clickable until the minimum you set.
        if (zoom <= NaviMap.GameMinimum + NaviMap.Epsilon)
            NaviMap.SetZoomOutEnabled(zoom > config.Minimum + NaviMap.Epsilon);
    }
}
