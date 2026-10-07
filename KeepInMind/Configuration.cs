using System.Collections.Generic;
using Dalamud.Configuration;

namespace KeepInMind;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<KeptItem> Items { get; set; } = [];
}

public sealed class KeptItem
{
    public uint ItemId { get; set; }

    public int Needed { get; set; } = 1;

    public bool Done { get; set; }
}
