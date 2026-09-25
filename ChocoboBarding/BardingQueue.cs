using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace ChocoboBarding;

/// <summary>
/// Applies slot changes one at a time, like a player clicking through the Appearance tab:
/// send one change, wait until the game shows it, pause briefly, then send the next.
/// </summary>
internal sealed class BardingQueue : IDisposable
{
    private const long ConfirmTimeoutMs = 3000;
    private const int MinPauseMs = 400;
    private const int MaxPauseMs = 700;

    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Queue<(int Slot, SlotChoice Choice)> pending = new();

    // The change sent last, until the game shows it or it times out.
    private (int Slot, uint RowId, uint Expected, long SentAt)? awaiting;
    private long nextSendAt;

    public BardingQueue(IFramework framework, IPluginLog log)
    {
        this.framework = framework;
        this.log = log;
        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        pending.Clear();
    }

    /// <summary>Replaces any steps not yet sent, so the latest command wins.</summary>
    public void Replace(IReadOnlyList<SlotChoice> choices)
    {
        pending.Clear();
        for (var slot = 0; slot < choices.Count; slot++)
            if (choices[slot].Action != SlotAction.Keep)
                pending.Enqueue((slot, choices[slot]));
    }

    private void OnUpdate(IFramework _)
    {
        var now = Environment.TickCount64;

        if (awaiting is { } sent)
        {
            var confirmed = Barding.WornRowId(sent.Slot) == sent.Expected;
            if (!confirmed && now - sent.SentAt < ConfirmTimeoutMs)
                return;

            log.Debug($"Slot {Barding.SlotNames[sent.Slot]}: BuddyEquip #{sent.RowId} {(confirmed ? "confirmed" : "timed out")}.");
            awaiting = null;
            nextSendAt = now + Random.Shared.Next(MinPauseMs, MaxPauseMs + 1);
        }

        if (now < nextSendAt)
            return;

        // Re-check each step against what's worn now; skip steps that are already done.
        while (pending.TryDequeue(out var step))
        {
            var worn = Barding.WornRowId(step.Slot);
            uint send, expected;
            if (step.Choice.Action == SlotAction.Remove)
            {
                if (worn == 0)
                    continue;
                (send, expected) = (worn, 0);
            }
            else
            {
                if (worn == step.Choice.RowId)
                    continue;
                (send, expected) = (step.Choice.RowId, step.Choice.RowId);
            }

            if (!Barding.SendOne(step.Slot, send))
            {
                log.Warning("Companion agent is not available; dropping remaining barding changes.");
                pending.Clear();
                return;
            }

            log.Debug($"Slot {Barding.SlotNames[step.Slot]}: sent BuddyEquip #{send}.");
            awaiting = (step.Slot, send, expected, now);
            return;
        }
    }
}
