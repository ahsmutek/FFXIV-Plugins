using System;
using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;

namespace Sundries;

// The game's click handler stops at its own limit, so below it the zoom is written after the game handles the click.
internal sealed class MinimapZoom : IModule
{
    // A different zoom within this window counts as the game's reset, not the player's.
    private const long RestoreWindowMs = 5000;

    private static readonly float[] Minimums = [0.25f, 0.5f, 0.75f, 1f];

    private readonly IFramework framework;
    private readonly IAddonLifecycle lifecycle;
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly Action save;
    private readonly IPluginLog log;

    private float? beforeClick;
    private long restoreUntil;

    public MinimapZoom(IFramework framework, IAddonLifecycle lifecycle, IClientState clientState, Configuration config,
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

    public string Name => "Minimap Zoom";

    public string[] Description =>
    [
        "Lets the minimap's − button zoom out past the game's 1.25, 0.25 a click. Your zoom is kept",
        "when you change job, zone or log in.",
    ];

    public bool Enabled
    {
        get => config.MinimapZoomEnabled;
        set
        {
            config.MinimapZoomEnabled = value;
            if (!value)
                ResetToGame();
        }
    }

    public void DrawSettings()
    {
        var index = Math.Max(0, Array.FindIndex(Minimums, m => MathF.Abs(m - config.MinimapMinimum) < NaviMap.Epsilon));
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderInt("Furthest zoom", ref index, 0, Minimums.Length - 1,
                            Minimums[index].ToString("0.##", CultureInfo.InvariantCulture)))
        {
            config.MinimapMinimum = Minimums[index];
            if (config.MinimapZoom is { } kept && kept < config.MinimapMinimum)
            {
                config.MinimapZoom = config.MinimapMinimum;
                NaviMap.SetZoom(config.MinimapMinimum);
            }
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The game stops at 1.25. Smaller shows more of the map.");

        var current = NaviMap.Zoom();
        ImGui.TextDisabled(current is { } zoom
            ? $"Minimap zoom now: {zoom.ToString("0.##", CultureInfo.InvariantCulture)}"
            : "Minimap zoom now: unknown (minimap hidden, or a game update moved it)");
        if (config.MinimapZoom != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Back to 1.25"))
                ResetToGame();
        }
    }

    public void ResetToGame()
    {
        ShowGameZoom();
        if (config.MinimapZoom != null)
        {
            config.MinimapZoom = null;
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
        beforeClick = config.MinimapZoomEnabled && ZoomClick(args) != null ? NaviMap.Zoom() : null;
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
            ? MathF.Max(config.MinimapMinimum, before - NaviMap.Step)
            : before + NaviMap.Step;
        if (!NaviMap.SetZoom(target, checkedBefore: true))
            return;

        config.MinimapZoom = target < NaviMap.GameMinimum - NaviMap.Epsilon ? target : null;
        save();
    }

    private void OnUpdate(IFramework _)
    {
        if (!config.MinimapZoomEnabled || NaviMap.Zoom() is not { } zoom)
            return;

        if (config.MinimapZoom is { } wanted)
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
                    config.MinimapZoom = zoom < NaviMap.GameMinimum - NaviMap.Epsilon ? zoom : null;
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
            NaviMap.SetZoomOutEnabled(zoom > config.MinimapMinimum + NaviMap.Epsilon);
    }
}
