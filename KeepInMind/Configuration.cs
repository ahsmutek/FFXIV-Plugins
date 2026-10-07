using System.Collections.Generic;
using Dalamud.Configuration;

namespace KeepInMind;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // The one list, shared by every recipe and crafting job, in the order items were added.
    public List<KeptItem> Items { get; set; } = [];
}

public sealed class KeptItem
{
    public uint ItemId { get; set; }

    // How many you need, added up over every recipe you kept it from.
    public int Needed { get; set; } = 1;

    public bool Done { get; set; }
}
