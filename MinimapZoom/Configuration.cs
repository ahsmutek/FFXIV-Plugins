using Dalamud.Configuration;

namespace MinimapZoom;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    // How far the − button can zoom out, past the game's own 1.25.
    public float Minimum { get; set; } = 0.25f;

    // The zoom you picked below the game's limit, put back when the game resets the minimap
    // (changing job, zoning, logging in). Null = at or above the game's limit; nothing to keep.
    public float? Zoom { get; set; }
}
