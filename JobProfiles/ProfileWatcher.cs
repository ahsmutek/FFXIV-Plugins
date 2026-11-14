using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Shortcuts;

namespace JobProfiles;

internal readonly record struct Resolved(int? HudLayout, List<uint> Mounts, List<uint> Minions);

internal sealed class ProfileWatcher : IDisposable
{
    private const long AfterJobChangeMs = 500;
    private const long AfterLoginMs = 3000;

    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly Configuration config;
    private readonly JobGroups groups;
    private readonly MacroRunner runner;
    private readonly ShortcutCatalog catalog;

    // 0 = not logged in (or turned off), so the next job seen counts as a login.
    private uint lastJob;
    private long? applyAt;
    private uint? mountPick;

    public ProfileWatcher(IFramework framework, IPlayerState player, Configuration config, JobGroups groups,
                          MacroRunner runner, ShortcutCatalog catalog)
    {
        this.framework = framework;
        this.player = player;
        this.config = config;
        this.groups = groups;
        this.runner = runner;
        this.catalog = catalog;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    public void ApplyNow()
    {
        applyAt = Environment.TickCount64;
        mountPick = null;
    }

    private Resolved Resolve(uint classJobId)
    {
        if (groups.Of(classJobId) is not var (abbreviation, group))
            return new Resolved(null, [], []);

        config.Groups.TryGetValue(group, out var forGroup);
        config.Jobs.TryGetValue(abbreviation, out var forJob);
        var forParent = JobGroups.Parent(group) is { } parent ? config.Groups.GetValueOrDefault(parent) : null;
        var hud = forJob?.HudLayout ?? forGroup?.HudLayout ?? forParent?.HudLayout;
        return new Resolved(
            hud is >= 0 ? hud : null,
            forJob?.Mounts ?? forGroup?.Mounts ?? forParent?.Mounts ?? [],
            forJob?.Minions ?? forGroup?.Minions ?? forParent?.Minions ?? []);
    }

    // Kept until the job changes, so Mount Roulette rides the same one until then.
    public uint? JobMount()
    {
        if (!config.Enabled || !player.IsLoaded)
            return null;

        var mounts = Resolve(player.ClassJob.RowId).Mounts;
        if (mountPick is { } kept && mounts.Contains(kept) && catalog.Available(MountStep(kept)))
            return kept;
        mountPick = RandomOf(mounts.Where(id => catalog.Available(MountStep(id))));
        return mountPick;
    }

    private void OnUpdate(IFramework _)
    {
        if (!player.IsLoaded)
        {
            lastJob = 0;
            applyAt = null;
            return;
        }

        var now = Environment.TickCount64;
        var job = player.ClassJob.RowId;
        // Off, only Apply now applies; turning it back on counts as a login.
        if (!config.Enabled)
        {
            lastJob = 0;
        }
        else if (job != lastJob)
        {
            runner.Clear();
            mountPick = null;
            if (lastJob != 0)
                applyAt = now + AfterJobChangeMs;
            else if (config.ApplyOnLogin)
                applyAt = now + AfterLoginMs;
            lastJob = job;
        }

        if (applyAt is not { } at || now < at || runner.Busy)
            return;

        applyAt = null;
        var resolved = Resolve(job);
        if (resolved.HudLayout is { } layout)
            Actions.ChangeHudLayout(layout);
        var current = catalog.CurrentMinion();
        if (RandomOf(resolved.Minions.Where(id => id != current && catalog.Available(MinionStep(id)))) is { } minion)
            runner.Enqueue([MinionStep(minion)]);
    }

    private static uint? RandomOf(IEnumerable<uint> ids) =>
        ids.ToList() is { Count: > 0 } list ? list[Random.Shared.Next(list.Count)] : null;

    private static Step MountStep(uint id) => new() { Kind = ShortcutKind.Mount, RowId = id };

    private static Step MinionStep(uint id) => new() { Kind = ShortcutKind.Minion, RowId = id };
}
