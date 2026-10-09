using System;
using System.Collections.Generic;
using System.Linq;

namespace KeepInMind;

// Groups are ids: a custom group's Guid, or a Source's name ("None" for Other).
internal static class GroupOrder
{
    public static string Id(CustomGroup group) => group.Id.ToString();

    public static string Id(Source source) => source.ToString();

    // New custom groups go first and new sources last, so a saved order never hides a group.
    public static List<string> Of(Configuration config)
    {
        var custom = config.Groups.Select(Id).ToList();
        var sources = Sources.Order.Append(Source.None).Select(Id).ToList();
        var known = custom.Concat(sources).ToHashSet();
        var saved = config.GroupOrder.Where(known.Contains).Distinct().ToList();
        return [.. custom.Except(saved), .. saved, .. sources.Except(saved)];
    }

    public static List<Source> SourcePriority(Configuration config) =>
        [.. Of(config).Select(id => Enum.TryParse<Source>(id, out var s) ? s : Source.None).Where(s => s != Source.None)];

    public static bool Move(Configuration config, string id, int step)
    {
        var order = Of(config);
        var from = order.IndexOf(id);
        var to = from + step;
        if (from < 0 || to < 0 || to >= order.Count)
            return false;
        (order[from], order[to]) = (order[to], order[from]);
        config.GroupOrder = order;
        return true;
    }
}
