using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
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

    private static readonly ConditionFlag[] Waiting =
    [
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.LoggingOut,
    ];

    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly ICondition condition;
    private readonly Configuration config;
    private readonly JobGroups groups;
    private readonly Actions actions;

    // 0 = not logged in (or turned off), so the next job seen counts as a login.
    private uint lastJob;
    private long? applyAt;
    // The profile's macros still to run, one per frame once the one before has finished.
    private readonly Queue<uint> macroQueue = new();

    public ProfileWatcher(IFramework framework, IPlayerState player, ICondition condition, Configuration config,
                          JobGroups groups, Actions actions)
    {
        this.framework = framework;
        this.player = player;
        this.condition = condition;
        this.config = config;
        this.groups = groups;
        this.actions = actions;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    /// <summary>Applies the current job's profile as soon as nothing is in the way.</summary>
    public void ApplyNow() => applyAt = Environment.TickCount64;

    /// <summary>The job's group profile with its override on top. Null HUD layout = don't change.</summary>
    public (int? HudLayout, List<uint> Macros) Resolve(uint classJobId)
    {
        if (groups.Of(classJobId) is not var (abbreviation, group))
            return (null, []);

        config.Groups.TryGetValue(group, out var forGroup);
        config.Jobs.TryGetValue(abbreviation, out var forJob);
        var hud = forJob?.HudLayout ?? forGroup?.HudLayout;
        var macros = forJob?.Macros ?? forGroup?.Macros ?? [];
        return (hud is >= 0 ? hud : null, macros);
    }

    private void OnUpdate(IFramework _)
    {
        if (!config.Enabled || !player.IsLoaded)
        {
            lastJob = 0;
            applyAt = null;
            macroQueue.Clear();
            return;
        }

        var now = Environment.TickCount64;
        var job = player.ClassJob.RowId;
        if (job != lastJob)
        {
            // The old job's macros that haven't run yet are dropped.
            macroQueue.Clear();
            if (lastJob != 0)
                applyAt = now + AfterJobChangeMs;
            else if (config.ApplyOnLogin)
                applyAt = now + AfterLoginMs;
            lastJob = job;
        }

        if (condition.Any(Waiting) || Actions.MacroRunning())
            return;

        if (applyAt is { } at && now >= at)
        {
            applyAt = null;
            macroQueue.Clear();
            var (hud, macros) = Resolve(job);
            if (hud is { } layout)
                Actions.ChangeHudLayout(layout);
            foreach (var macro in macros)
                macroQueue.Enqueue(macro);
        }

        // One macro per frame, so the next only starts once the game has picked this one up.
        if (macroQueue.TryDequeue(out var next))
            actions.RunMacro(next);
    }
}
