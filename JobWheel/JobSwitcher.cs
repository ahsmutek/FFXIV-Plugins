using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace JobWheel;

internal sealed class JobSwitcher(ICondition condition, IToastGui toasts)
{
    // States where the game won't let you change gear sets.
    private static readonly ConditionFlag[] Blocking =
    [
        ConditionFlag.InCombat,
        ConditionFlag.Casting,
        ConditionFlag.Casting87,
        ConditionFlag.BoundByDuty,
        ConditionFlag.BoundByDuty56,
        ConditionFlag.BoundByDuty95,
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
        ConditionFlag.Occupied,
        ConditionFlag.OccupiedInEvent,
        ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.OccupiedSummoningBell,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.Crafting,
        ConditionFlag.PreparingToCraft,
        ConditionFlag.ExecutingCraftingAction,
        ConditionFlag.Gathering,
        ConditionFlag.ExecutingGatheringAction,
        ConditionFlag.Fishing,
        ConditionFlag.Unconscious,
        ConditionFlag.TradeOpen,
        ConditionFlag.Performing,
        ConditionFlag.LoggingOut,
    ];

    public unsafe void Switch(WheelJob job)
    {
        // Already on it: nothing to do, and nothing worth telling the player.
        if (job.IsCurrent)
            return;

        if (condition.Any(Blocking))
        {
            toasts.ShowError("Can't change job right now.");
            return;
        }

        if (job.GearsetIndex < 0)
        {
            toasts.ShowError($"No gear set for {job.Name}.");
            return;
        }

        var module = RaptureGearsetModule.Instance();
        if (module == null || !module->IsValidGearset(job.GearsetIndex))
        {
            toasts.ShowError($"No gear set for {job.Name}.");
            return;
        }

        var result = module->EquipGearset(job.GearsetIndex);
        if (result < 0)
            toasts.ShowError("Can't change job right now.");
    }
}
