using System.Collections.Generic;
using Dalamud.Configuration;

namespace PlaceFinder;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool CloseAfterFlag { get; set; } = true;

    // Browse lists zones A–Z instead of by region.
    public bool BrowseByName { get; set; }

    // ExVersion rows whose zones are left out of Browse and Search.
    public HashSet<uint> HiddenExpansions { get; set; } = new();
}
