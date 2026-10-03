using System;
using Dalamud.Configuration;

namespace ControllerKeeper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Only let the game rescan controllers when the set of connected ones changed.
    public bool SkipRescans { get; set; } = true;

    // Switch the game back to the chosen controller whenever it's on another one.
    public bool KeepChosen { get; set; } = true;

    // The chosen controller, by its DirectInput instance; the product and name find it
    // again if Windows gives it a new instance (plugged into another port).
    public Guid? Instance { get; set; }
    public Guid? Product { get; set; }
    public string? Name { get; set; }
}
