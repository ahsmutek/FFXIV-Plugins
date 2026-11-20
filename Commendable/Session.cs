using System;
using System.Collections.Generic;

namespace Commendable;

// Character names are unique per home world, so this tells same-named players from different worlds apart.
internal readonly record struct PlayerKey(string Name, uint WorldId);

// Saved by value in the settings, so new categories go at the end.
public enum Category
{
    Resurrection,
    DancePartner,
    Kardia,
    Healing,
    Cleanse,
    Synastry,
    Aetherpact,
    LimitBreakRevival,
    Protection,
    Cards,
    Rescue,
    LimitBreakUsed,
}

internal enum Direction
{
    Incoming,
    Outgoing,
}

internal enum Outcome
{
    None,
    Offered,
    Accepted,
}

internal enum Membership
{
    Party,
    Alliance,
    Other,
}

internal sealed class Interaction
{
    public required DateTime At { get; init; }

    public uint ActionId { get; init; }

    public uint StatusId { get; init; }

    public required Category Category { get; init; }

    public required Direction Direction { get; init; }

    public Outcome Outcome { get; set; }

    // A relationship lasts from At until EndedAt, which stays null while it's still going.
    public DateTime? EndedAt { get; set; }

    // The same partner again after their status dropped (death, zoning), not a new choice.
    public bool Continues { get; init; }
}

internal sealed class PlayerRecord(PlayerKey key)
{
    public PlayerKey Key { get; } = key;

    public uint JobId { get; set; }

    public int Order { get; set; } = int.MaxValue;

    public Membership Membership { get; set; } = Membership.Other;

    public DateTime? LeftAt { get; set; }

    public DateTime? LastAt { get; set; }

    public List<Interaction> Interactions { get; } = [];

    public bool Bookmarked { get; set; }

    public string Note { get; set; } = "";
}

internal sealed class DutySession(uint dutyId, string dutyName, DateTime startedAt)
{
    public uint DutyId { get; } = dutyId;

    public string DutyName { get; } = dutyName;

    public DateTime StartedAt { get; } = startedAt;

    public DateTime? CompletedAt { get; set; }

    public bool IsCompleted => CompletedAt != null;

    public Dictionary<PlayerKey, PlayerRecord> Players { get; } = [];

    // Bumped on every change, so the window rebuilds its rows only when something happened.
    public int Version { get; set; }
}
