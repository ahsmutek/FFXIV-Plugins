using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Shortcuts;

namespace JobProfiles;

internal sealed class MacroRunner : IDisposable
{
    private static readonly ConditionFlag[] Waiting =
    [
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.LoggingOut,
    ];

    // The game's own macros stop at /wait 60.
    private const double MaxWaitSeconds = 60;
    // Spaced out so a long profile doesn't reach the server as a burst.
    private const long StepGapMs = 150;

    private readonly IFramework framework;
    private readonly ICondition condition;
    private readonly ShortcutCatalog catalog;
    private readonly Queue<Step> queue = new();
    private long resumeAt;

    public MacroRunner(IFramework framework, ICondition condition, ShortcutCatalog catalog)
    {
        this.framework = framework;
        this.condition = condition;
        this.catalog = catalog;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    public bool Busy => condition.Any(Waiting) || Actions.MacroRunning();

    public void Enqueue(IEnumerable<Step> steps)
    {
        foreach (var step in steps)
            queue.Enqueue(step);
    }

    public void Clear()
    {
        queue.Clear();
        resumeAt = 0;
    }

    private void OnUpdate(IFramework _)
    {
        var now = Environment.TickCount64;
        if (queue.Count == 0 || Busy || now < resumeAt)
            return;

        var step = queue.Dequeue();
        if (step.Kind == ShortcutKind.Text && WaitSeconds(step.Command) is { } seconds)
        {
            resumeAt = now + (long)(Math.Min(seconds, MaxWaitSeconds) * 1000);
        }
        else if (catalog.Available(step))
        {
            catalog.Run(step);
            resumeAt = now + StepGapMs;
        }
    }

    private static double? WaitSeconds(string? command)
    {
        var parts = command?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is ["/wait", var value] &&
               double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? seconds
            : null;
    }
}
