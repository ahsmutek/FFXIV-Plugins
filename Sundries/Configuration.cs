using System.Numerics;
using Dalamud.Configuration;

namespace Sundries;

public sealed class Configuration : IPluginConfiguration
{
    public static readonly Vector4 DefaultGuideHighlightColor = new(1f, 0.8f, 0.3f, 1f);

    public int Version { get; set; } = 1;

    public bool GuideHighlightEnabled { get; set; } = true;

    public Vector4 GuideHighlightColor { get; set; } = DefaultGuideHighlightColor;

    public bool MinimapZoomEnabled { get; set; } = true;

    public float MinimapMinimum { get; set; } = 0.25f;

    // Restored when the game resets the minimap; null = within the game's range.
    public float? MinimapZoom { get; set; }
}
