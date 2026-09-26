using System.Collections.Generic;
using Dalamud.Configuration;

namespace ChocoboBarding;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ClassJob row id → head, body, legs. Jobs with nothing picked have no entry.
    public Dictionary<uint, SlotChoice[]> JobBarding { get; set; } = new();
}
