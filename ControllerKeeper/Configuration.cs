using System;
using Dalamud.Configuration;

namespace ControllerKeeper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool SkipRescans { get; set; } = true;

    public bool KeepChosen { get; set; } = true;

    // Product and name find it again if Windows assigns a new instance.
    public Guid? Instance { get; set; }
    public Guid? Product { get; set; }
    public string? Name { get; set; }
}
