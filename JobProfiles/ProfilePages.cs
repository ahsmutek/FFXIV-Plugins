using System.Collections.Generic;
using System.Linq;

namespace JobProfiles;

internal sealed class ProfilePages(Configuration config)
{
    public Profile? ProfileOf(string who) => ButtonsTab.ParseWho(who) switch
    {
        (string job, _) => config.Jobs.GetValueOrDefault(job),
        (_, JobGroup group) => config.Groups.GetValueOrDefault(group),
        _ => null,
    };

    public Profile Create(string who)
    {
        var (job, group) = ButtonsTab.ParseWho(who);
        if (job != null)
        {
            if (!config.Jobs.TryGetValue(job, out var jobProfile))
                config.Jobs[job] = jobProfile = new Profile();
            return jobProfile;
        }
        if (!config.Groups.TryGetValue(group!.Value, out var profile))
            config.Groups[group.Value] = profile = new Profile();
        return profile;
    }

    public static string? ParentOf(string who) => ButtonsTab.ParseWho(who) switch
    {
        (string job, _) => $"g:{GroupOf(job)}",
        (_, JobGroup group) when JobGroups.Parent(group) is { } parent => $"g:{parent}",
        _ => null,
    };

    public static JobGroup GroupOf(string abbreviation) => JobGroups.All.First(g => g.Jobs.Contains(abbreviation)).Group;
}
