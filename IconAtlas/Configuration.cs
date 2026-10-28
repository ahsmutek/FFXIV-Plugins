using System.Collections.Generic;
using Dalamud.Configuration;

namespace IconAtlas;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool OnlyExisting { get; set; } = true;

    public int TileSize { get; set; } = 48;

    public uint LastId { get; set; }

    // Kept sorted by ID.
    public List<uint> Favorites { get; set; } = new();

    public HashSet<string> ExpandedGroups { get; set; } = new();

    public List<string> PinnedGroups { get; set; } = new();

    // By the range's first ID.
    public List<uint> PinnedRanges { get; set; } = new();
}
