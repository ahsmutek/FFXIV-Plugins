using System.Collections.Generic;
using Dalamud.Configuration;

namespace HuntMap;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool CloseAfterFlag { get; set; }

    public bool CheckOnArrival { get; set; } = true;

    public bool EndHuntOnKill { get; set; } = true;

    public uint UncheckedIcon { get; set; } = MapMarkers.DefaultUncheckedIcon;

    public uint CheckedIcon { get; set; } = MapMarkers.DefaultCheckedIcon;

    public bool HideCheckedOnMap { get; set; }

    // Key of the mark being hunted.
    public string? Target { get; set; }

    // Browse lists marks A–Z instead of by region.
    public bool BrowseByName { get; set; }

    // ExVersion rows whose zones are left out of Browse and Search.
    public HashSet<uint> HiddenExpansions { get; set; } = new();

    // Left out of Here, Browse and Search.
    public HashSet<string> HiddenRanks { get; set; } = new();

    // Checked spot indices by mark key.
    public Dictionary<string, HashSet<int>> CheckedSpots { get; set; } = new();

    // MobHuntOrder row last seen per mark bill index; a different one means a new bill.
    public Dictionary<int, uint> BillOrders { get; set; } = new();

    // Bill targets whose hunt was stopped, so their spots stay off the map until hunted again or a new bill.
    public HashSet<string> StoppedMarks { get; set; } = new();
}
