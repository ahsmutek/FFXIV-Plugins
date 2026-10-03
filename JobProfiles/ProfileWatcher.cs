using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace JobProfiles;

/// <summary>
/// Applies the job's profile whenever the job changes, however it was changed (a gear set,
/// /gs, another plugin), and once after logging in. Waits out zoning, cutscenes and a macro
/// that's still running, so nothing gets lost.
/// </summary>
internal sealed class ProfileWatcher : IDisposable
{
    // The gear set change and the login take a moment to settle before the HUD can change.
    private const long AfterJobChangeMs = 500;
    private const long AfterLoginMs = 3000;

    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly Configuration config;
    private readonly JobGroups groups;
    private readonly MacroRunner runner;

    // 0 = not logged in (or turned off), so the next job seen counts as a login.
    private uint lastJob;
    private long? applyAt;

    public ProfileWatcher(IFramework framework, IPlayerState player, Configuration config, JobGroups groups,
                          MacroRunner runner)
    {
        this.framework = framework;
        this.player = player;
        this.config = config;
        this.groups = groups;
        this.runner = runner;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    /// <summary>Applies the current job's profile as soon as nothing is in the way.</summary>
    public void ApplyNow() => applyAt = Environment.TickCount64;

    /// <summary>The job's group profile with its override on top. Null HUD layout = don't change.</summary>
    public (int? HudLayout, List<Step> Steps) Resolve(uint classJobId)
    {
        if (groups.Of(classJobId) is not var (abbreviation, group))
            return (null, []);

        config.Groups.TryGetValue(group, out var forGroup);
        config.Jobs.TryGetValue(abbreviation, out var forJob);
        var hud = forJob?.HudLayout ?? forGroup?.HudLayout;
        var steps = forJob?.Steps ?? forGroup?.Steps ?? [];
        return (hud is >= 0 ? hud : null, steps);
    }

    private void OnUpdate(IFramework _)
    {
        if (!config.Enabled || !player.IsLoaded)
        {
            lastJob = 0;
            applyAt = null;
            return;
        }

        var now = Environment.TickCount64;
        var job = player.ClassJob.RowId;
        if (job != lastJob)
        {
            // The old job's macros that haven't run yet are dropped.
            runner.Clear();
            if (lastJob != 0)
                applyAt = now + AfterJobChangeMs;
            else if (config.ApplyOnLogin)
                applyAt = now + AfterLoginMs;
            lastJob = job;
        }

        if (applyAt is not { } at || now < at || runner.Busy)
            return;

        applyAt = null;
        var (hud, steps) = Resolve(job);
        if (hud is { } layout)
            Actions.ChangeHudLayout(layout);
        runner.Enqueue(steps);
    }
}
