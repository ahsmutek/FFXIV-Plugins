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

    public static readonly Vector4 DefaultOutgrownColor = new(1f, 0.3f, 0.3f, 1f);

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

    public bool MarkUpgrades { get; set; } = true;

    public HashSet<int> HiddenUpgradeSlots { get; set; } = [];

    public bool GearsetsEnabled { get; set; } = true;

    public bool OutgrownEnabled { get; set; } = true;

    public Vector4 OutgrownColor { get; set; } = DefaultOutgrownColor;

    public bool CompareAsEnabled { get; set; } = true;

    // How Item Comparison shows each equip state, as seen in the game's own comparisons.
    public Dictionary<byte, ComparisonLook> ComparisonLooks { get; set; } = [];

    public bool WishlistEnabled { get; set; } = true;

    public Vector4 WishColor { get; set; } = DefaultWishColor;

    public List<KeptItem> Items { get; set; } = [];

    public List<KeptItem> LaterItems { get; set; } = [];

    public List<KeptItem> WishItems { get; set; } = [];

    // Each character's glamour dresser and armoire as last seen, by content ID; HQ items are 1,000,000 on.
    public Dictionary<ulong, List<uint>> Dressers { get; set; } = [];

    public Dictionary<ulong, List<uint>> Armoires { get; set; } = [];

    public UpgradeGrouping UpgradeGrouping { get; set; } = UpgradeGrouping.Job;

    public string KeeperTab { get; set; } = "";

    // The jobs the Upgrades tab hides each item for, by item ID.
    public Dictionary<uint, HashSet<uint>> NotUpgrades { get; set; } = [];

    public bool IsKept(uint itemId) => itemId != 0 && Items.Exists(i => i.ItemId == itemId);

    public bool IsKeptForLater(uint itemId) => itemId != 0 && LaterItems.Exists(i => i.ItemId == itemId);

    public bool IsWished(uint itemId) => itemId != 0 && WishItems.Exists(i => i.ItemId == itemId);

    public bool IsNotUpgrade(uint itemId, uint jobId) =>
        NotUpgrades.TryGetValue(itemId, out var jobs) && jobs.Contains(jobId);

    public void SetNotUpgrade(uint itemId, uint jobId, bool notUpgrade)
    {
        if (notUpgrade)
        {
            if (!NotUpgrades.TryGetValue(itemId, out var added))
                NotUpgrades[itemId] = added = [];
            added.Add(jobId);
        }
        else if (NotUpgrades.TryGetValue(itemId, out var listed) && listed.Remove(jobId) && listed.Count == 0)
        {
            NotUpgrades.Remove(itemId);
        }
    }
}

public enum UpgradeGrouping
{
    Job,
    Slot,
    Place,
    None,
}

public sealed class ComparisonLook
{
    public int Timeline { get; set; }

    public int LevelColor { get; set; }
}

public sealed class KeptItem
{
    public uint ItemId { get; set; }

    public DateTime Added { get; set; } = DateTime.UtcNow;
}
