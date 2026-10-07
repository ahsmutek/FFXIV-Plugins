using System.Collections.Generic;
using Dalamud.Configuration;

namespace ChocoboBarding;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public Dictionary<uint, SlotChoice[]> JobBarding { get; set; } = new();

    public bool MatchJobAutomatically { get; set; }
}
