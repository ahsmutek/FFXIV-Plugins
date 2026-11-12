using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace JobProfiles;

// Mount Roulette and Flying Mount Roulette ride the job's mount instead, wherever they're used from.
internal sealed unsafe class MountRoulette : IDisposable
{
    private const uint MountRouletteRow = 9;
    private const uint FlyingMountRouletteRow = 24;

    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly Func<uint?> jobMount;
    private readonly Hook<ActionManager.Delegates.UseAction> hook;

    public MountRoulette(IGameInteropProvider interop, ICondition condition, IPluginLog log, Func<uint?> jobMount)
    {
        this.condition = condition;
        this.log = log;
        this.jobMount = jobMount;
        hook = interop.HookFromAddress<ActionManager.Delegates.UseAction>(
            ActionManager.MemberFunctionPointers.UseAction, OnUseAction);
        hook.Enable();
    }

    public void Dispose() => hook.Dispose();

    private bool OnUseAction(ActionManager* actions, ActionType type, uint id, ulong target, uint extra,
                             ActionManager.UseActionMode mode, uint combo, bool* areaTargeted)
    {
        if (type == ActionType.GeneralAction && id is MountRouletteRow or FlyingMountRouletteRow)
        {
            try
            {
                // Never swaps the mount being ridden.
                if (!condition[ConditionFlag.Mounted] && jobMount() is { } mount)
                    return hook.Original(actions, ActionType.Mount, mount, target, extra, mode, combo, areaTargeted);
            }
            catch (Exception e)
            {
                log.Error(e, "Couldn't ride the job's mount; using the game's roulette.");
            }
        }

        return hook.Original(actions, type, id, target, extra, mode, combo, areaTargeted);
    }
}
