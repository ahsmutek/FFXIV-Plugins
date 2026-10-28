using System.Collections.Generic;
using Dalamud.Configuration;

namespace HuntMap;

public sealed class BillChecks
{
    // MobHuntOrder row; a different one means a new bill, so the checks start over.
    public uint Order { get; set; }
    public HashSet<int> Spots { get; set; } = new();
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // By mark bill index.
    public Dictionary<int, BillChecks> Checked { get; set; } = new();
}
