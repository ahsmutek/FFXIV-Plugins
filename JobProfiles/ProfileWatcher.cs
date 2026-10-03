using System;
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

    /// <summary>The job's group profile with its override on top. Null fields = do nothing.</summary>
    public Profile Resolve(uint classJobId)
    {
        if (groups.Of(classJobId) is not var (abbreviation, group))
            return new Profile();

        config.Groups.TryGetValue(group, out var forGroup);
        config.Jobs.TryGetValue(abbreviation, out var forJob);
        var hud = forJob?.HudLayout ?? forGroup?.HudLayout;
        var macro = forJob?.MacroId ?? forGroup?.MacroId;
        return new Profile
        {
            HudLayout = hud is >= 0 ? hud : null,
            MacroId = macro == Profile.NoMacro ? null : macro,
        };
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
            if (lastJob != 0)
                applyAt = now + AfterJobChangeMs;
            else if (config.ApplyOnLogin)
                applyAt = now + AfterLoginMs;
            lastJob = job;
        }

        if (applyAt is not { } at || now < at || condition.Any(Waiting) || Actions.MacroRunning())
            return;

        applyAt = null;
        var profile = Resolve(job);
        if (profile.HudLayout is { } layout)
            Actions.ChangeHudLayout(layout);
        if (profile.MacroId is { } macro)
            actions.RunMacro(macro);
    }
}
