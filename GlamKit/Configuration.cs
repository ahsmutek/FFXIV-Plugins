using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace GlamKit;

public sealed class Configuration : IPluginConfiguration
{
    public static readonly Vector4 DefaultKeeperColor = new(0.78f, 0.55f, 1f, 1f);

    public int Version { get; set; } = 1;

    public bool GlamOffEnabled { get; set; } = true;

    public bool GlamKeeperEnabled { get; set; } = true;

    public Vector4 KeeperColor { get; set; } = DefaultKeeperColor;

    public bool JobLevelsEnabled { get; set; } = true;

    public int GreenAbove { get; set; } = 10;

    public int YellowBelow { get; set; } = 5;

    public bool UncolourUnwearableLevel { get; set; } = true;

    public List<KeptItem> Items { get; set; } = [];

    public bool IsKept(uint itemId) => itemId != 0 && Items.Exists(i => i.ItemId == itemId);
}

public sealed class KeptItem
{
    public uint ItemId { get; set; }

    public DateTime Added { get; set; } = DateTime.UtcNow;
}
