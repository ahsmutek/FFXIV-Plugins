using Dalamud.Configuration;

namespace MinimapZoom;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public float Minimum { get; set; } = 0.25f;

    // Restored when the game resets the minimap; null = within the game's range.
    public float? Zoom { get; set; }
}
