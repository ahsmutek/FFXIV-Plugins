using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace ChocoboBarding;

internal sealed class AutoBarding : IDisposable
{
    private const long AfterSummonMs = 1500;
    private const long AfterJobChangeMs = 500;

    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly ICondition condition;
    private readonly Configuration config;
    private readonly Action apply;

    private bool wasOut;
    private uint lastJob;
    private long? applyAt;

    public AutoBarding(IFramework framework, IPlayerState player, ICondition condition, Configuration config, Action apply)
    {
        this.framework = framework;
        this.player = player;
        this.condition = condition;
        this.config = config;
        this.apply = apply;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    private void OnUpdate(IFramework _)
    {
        if (!config.MatchJobAutomatically || !player.IsLoaded)
        {
            wasOut = false;
            applyAt = null;
            return;
        }

        var now = Environment.TickCount64;
        var isOut = Barding.IsChocoboOut();
        var job = player.ClassJob.RowId;

        if (isOut && !wasOut)
            applyAt = now + AfterSummonMs;
        else if (isOut && job != lastJob)
            applyAt = now + AfterJobChangeMs;

        wasOut = isOut;
        lastJob = job;

        if (!isOut)
        {
            applyAt = null;
            return;
        }

        // Barding can't change mid-fight; wait until it's over.
        if (applyAt is not { } at || now < at || condition[ConditionFlag.InCombat])
            return;

        applyAt = null;
        apply();
    }
}
