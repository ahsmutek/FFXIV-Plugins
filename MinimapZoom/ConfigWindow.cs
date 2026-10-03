using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MinimapZoom;

internal sealed class ConfigWindow : Window
{
    // The minimums on offer: 0.25 up to one step below the game's own limit.
    private static readonly float[] Minimums = [0.25f, 0.5f, 0.75f, 1f];

    private readonly Configuration config;
    private readonly Action save;
    private readonly ZoomExtender extender;

    public ConfigWindow(Configuration config, Action save, ZoomExtender extender)
        : base("Minimap Zoom###MinimapZoomConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.save = save;
        this.extender = extender;
    }

    public override void Draw()
    {
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Let − zoom out further than the game allows", ref enabled))
        {
            config.Enabled = enabled;
            if (!enabled)
                extender.ResetToGame();
            save();
        }

        var index = Math.Max(0, Array.FindIndex(Minimums, m => MathF.Abs(m - config.Minimum) < NaviMap.Epsilon));
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderInt("Furthest zoom", ref index, 0, Minimums.Length - 1,
                            Minimums[index].ToString("0.##", CultureInfo.InvariantCulture)))
        {
            config.Minimum = Minimums[index];
            // Already zoomed out further than the new limit: come back to it.
            if (config.Zoom is { } kept && kept < config.Minimum)
            {
                config.Zoom = config.Minimum;
                NaviMap.SetZoom(config.Minimum);
            }
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The game stops at 1.25. Smaller shows more of the map.");

        ImGui.Spacing();
        var current = NaviMap.Zoom();
        ImGui.TextDisabled(current is { } zoom
            ? $"Minimap zoom now: {zoom.ToString("0.##", CultureInfo.InvariantCulture)}"
            : "Minimap zoom now: unknown (minimap hidden, or a game update moved it)");

        if (config.Zoom != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Back to 1.25"))
                extender.ResetToGame();
        }

        ImGui.Spacing();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 360f);
        ImGui.TextDisabled("Use the minimap's − button to zoom out, 0.25 a click. Your zoom is kept when you change job, zone or log in.");
        ImGui.PopTextWrapPos();
    }
}
