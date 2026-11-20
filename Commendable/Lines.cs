using System;
using System.Collections.Generic;
using System.Linq;

namespace Commendable;

internal readonly record struct Line(Category Category, Direction Direction, Outcome Outcome, uint ActionId, string Text,
                                     bool Highlight);

internal static class Lines
{
    // Relationships, personal assistance, the party's limit breaks, then routine support.
    private static readonly Category[] Order =
    [
        Category.DancePartner, Category.Kardia, Category.Synastry, Category.Aetherpact,
        Category.Resurrection, Category.LimitBreakRevival, Category.Protection, Category.Cards, Category.Cleanse,
        Category.Rescue, Category.LimitBreakUsed, Category.Healing,
    ];

    public static bool IsRelationship(Category category) =>
        category is Category.DancePartner or Category.Kardia or Category.Synastry or Category.Aetherpact;

    // `now` ends relationships that are still going.
    public static List<Line> Of(PlayerRecord player, Func<Category, bool> shown, Func<uint, string> actionName, DateTime now) =>
        player.Interactions
              .Where(i => shown(i.Category))
              .GroupBy(i => (i.Category, i.Direction, i.Outcome, ActionId: OwnLine(i)))
              .Select(g => (Group: g, Count: g.Count()))
              .OrderBy(g => Array.IndexOf(Order, g.Group.Key.Category))
              .ThenBy(g => g.Group.Key.Direction)
              .ThenByDescending(g => g.Group.Key.Outcome)
              .ThenByDescending(g => g.Count)
              .Select(g => new Line(g.Group.Key.Category, g.Group.Key.Direction, g.Group.Key.Outcome,
                                    g.Group.Key.ActionId != 0 ? g.Group.Key.ActionId
                                        : g.Group.FirstOrDefault(i => i.ActionId != 0)?.ActionId ?? 0,
                                    Text(g.Group.Key, g.Group, g.Count, actionName, now),
                                    g.Group.Key.Category == Category.Healing && !Registry.HealerJobs.Contains(player.JobId)))
              .ToList();

    public static string? Chat(PlayerRecord player, Interaction interaction)
    {
        var name = player.Key.Name;
        return interaction switch
        {
            { Direction: Direction.Outgoing } or { Continues: true } => null,
            { Category: Category.Resurrection, Outcome: Outcome.Accepted } => $"{name} raised you.",
            { Category: Category.LimitBreakRevival } => $"{name} revived you with a limit break.",
            { Category: Category.DancePartner } => $"{name} selected you as Dance Partner.",
            { Category: Category.Kardia } => $"{name} chose you as their Kardion target.",
            _ => null,
        };
    }

    // One entry of a player's event log, with the action actually used.
    public static string Event(Interaction interaction, DateTime start, Func<uint, string> actionName,
                               Func<uint, string> statusName)
    {
        var at = Clock(interaction.At - start);
        if (IsRelationship(interaction.Category))
        {
            var until = interaction.EndedAt is { } ended ? Clock(ended - start) : "";
            return $"{at}–{until}  {Relationship(interaction.Category, interaction.Direction)}";
        }

        var action = interaction.ActionId != 0 ? Capitalized(actionName(interaction.ActionId)) : "Raise";
        var what = interaction switch
        {
            { Category: Category.Resurrection, Outcome: Outcome.Accepted } => $"{action}, accepted",
            { Category: Category.Resurrection } => $"{action}, not taken",
            { Category: Category.LimitBreakRevival } => $"{action} revived you",
            { Category: Category.LimitBreakUsed } => $"{action} (limit break)",
            { Category: Category.Cards } => statusName(interaction.StatusId),
            { Category: Category.Cleanse, StatusId: not 0 } => $"{action} removed {statusName(interaction.StatusId)}",
            _ => action,
        };
        return $"{at}  {what}";
    }

    public static string Clock(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";

    // These get a line per action, an upgrade sharing its original's.
    private static uint OwnLine(Interaction interaction) =>
        interaction.Category is Category.Protection or Category.Cleanse or Category.Healing or Category.LimitBreakUsed
            ? Registry.ShownAs.GetValueOrDefault(interaction.ActionId, interaction.ActionId)
            : 0;

    private static string Text((Category Category, Direction Direction, Outcome Outcome, uint ActionId) kind,
                               IEnumerable<Interaction> group, int count, Func<uint, string> actionName, DateTime now)
    {
        if (IsRelationship(kind.Category))
            return $"{Relationship(kind.Category, kind.Direction)} · {Duration(group, now)}";

        return kind switch
        {
            { Category: Category.Resurrection, Outcome: Outcome.Accepted } => $"Raised you ×{count}",
            { Category: Category.Resurrection } => $"Offered a raise ×{count}",
            { Category: Category.LimitBreakRevival } => $"Revived you with a limit break ×{count}",
            { Category: Category.LimitBreakUsed } => $"Limit break: {Capitalized(actionName(kind.ActionId))} ×{count}",
            { Category: Category.Cards } => Cards(group, count),
            { Category: Category.Cleanse, ActionId: Registry.Esuna } => $"Removed your debuff ×{count}",
            { Category: Category.Rescue } => $"Used Rescue on you ×{count}",
            _ => $"{Capitalized(actionName(kind.ActionId))} ×{count}",
        };
    }

    // Some names start in lowercase, like "the Warden's Paean".
    private static string Capitalized(string name) => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;

    private static string Relationship(Category category, Direction direction) => (category, direction) switch
    {
        (Category.DancePartner, Direction.Incoming) => "Selected you as Dance Partner",
        (Category.DancePartner, _) => "You selected them as Dance Partner",
        (Category.Kardia, Direction.Incoming) => "Chose you as Kardion target",
        (Category.Kardia, _) => "You chose them as Kardion target",
        (Category.Synastry, Direction.Incoming) => "Synastry with you",
        (Category.Synastry, _) => "Your Synastry with them",
        (Category.Aetherpact, Direction.Incoming) => "Fey Union with you",
        _ => "Your Fey Union with them",
    };

    private static string Cards(IEnumerable<Interaction> group, int count)
    {
        var offensive = group.Count(i => Registry.CardIsOffensive.GetValueOrDefault((ushort)i.StatusId));
        var parts = new List<string>(2);
        if (offensive > 0)
            parts.Add($"{offensive} offensive");
        if (count - offensive > 0)
            parts.Add($"{count - offensive} defensive/healing");
        return $"Received {count} card{(count == 1 ? "" : "s")} — {string.Join(", ", parts)}";
    }

    private static string Duration(IEnumerable<Interaction> group, DateTime now)
    {
        var total = group.Aggregate(TimeSpan.Zero, (sum, i) => sum + ((i.EndedAt ?? now) - i.At));
        if (total < TimeSpan.Zero)
            total = TimeSpan.Zero;
        return total.TotalHours >= 1 ? $"{(int)total.TotalHours}h {total.Minutes}m"
               : total.TotalMinutes >= 1 ? $"{total.Minutes}m {total.Seconds}s"
               : $"{total.Seconds}s";
    }
}
