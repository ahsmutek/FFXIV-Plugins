using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace GlamKit;

public sealed class Configuration : IPluginConfiguration
{
    public static readonly Vector4 DefaultKeeperColor = new(0.78f, 0.55f, 1f, 1f);

    public static readonly Vector4 DefaultLaterColor = new(0.35f, 0.85f, 0.75f, 1f);

    public static readonly Vector4 DefaultWishColor = new(1f, 0.55f, 0.7f, 1f);

    public int Version { get; set; } = 1;

    public bool GlamOffEnabled { get; set; } = true;

    public bool GlamKeeperEnabled { get; set; } = true;

    public Vector4 KeeperColor { get; set; } = DefaultKeeperColor;

    public bool KeepForLaterEnabled { get; set; } = true;

    public Vector4 LaterColor { get; set; } = DefaultLaterColor;

    public bool JobLevelsEnabled { get; set; } = true;

    public int GreenAbove { get; set; } = 10;

    public int YellowBelow { get; set; } = 5;

    public bool UncolourUnwearableLevel { get; set; } = true;

    public bool GearsetsEnabled { get; set; } = true;

    public bool WishlistEnabled { get; set; } = true;

    public Vector4 WishColor { get; set; } = DefaultWishColor;

    public List<KeptItem> Items { get; set; } = [];

    public List<KeptItem> LaterItems { get; set; } = [];

    public List<KeptItem> WishItems { get; set; } = [];

    public bool IsKept(uint itemId) => itemId != 0 && Items.Exists(i => i.ItemId == itemId);

    public bool IsKeptForLater(uint itemId) => itemId != 0 && LaterItems.Exists(i => i.ItemId == itemId);

    public bool IsWished(uint itemId) => itemId != 0 && WishItems.Exists(i => i.ItemId == itemId);
}

public sealed class KeptItem
{
    public uint ItemId { get; set; }

    public DateTime Added { get; set; } = DateTime.UtcNow;
}
