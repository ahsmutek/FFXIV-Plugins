using System.Collections.Generic;
using Dalamud.Configuration;

namespace Commendable;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool AutoOpen { get; set; } = true;

    public bool WaitForCommendations { get; set; }

    public bool HideQuiet { get; set; } = true;

    public bool HideLeft { get; set; }

    public bool Compact { get; set; }

    public bool KeepLastDuty { get; set; } = true;

    public bool RememberPosition { get; set; } = true;

    public HashSet<Category> DisabledCategories { get; set; } = [];

    public HealingShown Healing { get; set; } = HealingShown.NonHealers;

    public bool Bookmarks { get; set; } = true;

    public bool ChatNotifications { get; set; }

    public int SavedDuties { get; set; } = 3;

    public SortOrder Sort { get; set; } = SortOrder.Party;
}

public enum HealingShown
{
    Off,
    NonHealers,
    All,
}

public enum SortOrder
{
    Party,
    Bookmarks,
    Recent,
    Name,
}
