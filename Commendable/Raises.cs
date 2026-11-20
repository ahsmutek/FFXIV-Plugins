using System;

namespace Commendable;

internal readonly record struct Revival(uint By, DateTime DiedAt, uint LimitBreak);

// Credit goes to whoever's Raise status you came back with, so with two raisers it's the one you accepted.
internal sealed class Raises
{
    // The status can vanish a moment before you're alive again; one that ran out long before was never taken.
    private static readonly TimeSpan AcceptedWithin = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LimitBreakWithin = TimeSpan.FromSeconds(5);

    private DateTime? diedAt;
    private uint raiser;
    private DateTime? raiseLostAt;
    private (uint Caster, uint ActionId, DateTime At)? limitBreak;

    public void Gained(uint sourceId)
    {
        if (diedAt == null)
            return;
        raiser = sourceId;
        raiseLostAt = null;
    }

    public void Lost(DateTime at)
    {
        if (diedAt != null && raiser != 0)
            raiseLostAt = at;
    }

    public void LimitBreakLanded(uint casterId, uint actionId, DateTime at)
    {
        if (diedAt != null)
            limitBreak = (casterId, actionId, at);
    }

    // A limit break revives as it lands, so it goes first even if a raise was waiting.
    public Revival? AliveChanged(bool alive, DateTime at)
    {
        if (!alive)
        {
            diedAt ??= at;
            return null;
        }

        Revival? revival = null;
        if (diedAt is { } died)
        {
            if (limitBreak is { } lb && at - lb.At <= LimitBreakWithin)
                revival = new Revival(lb.Caster, died, lb.ActionId);
            else if (raiser != 0 && (raiseLostAt == null || at - raiseLostAt <= AcceptedWithin))
                revival = new Revival(raiser, died, 0);
        }
        Reset();
        return revival;
    }

    public void Reset()
    {
        diedAt = null;
        raiser = 0;
        raiseLostAt = null;
        limitBreak = null;
    }
}
