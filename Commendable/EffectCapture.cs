using System;
using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ActionRow = Lumina.Excel.Sheets.Action;

namespace Commendable;

// Records every action between a player (or their pet) and you, not just the tracked ones, so saved duties can test
// new rules, plus every player's limit break whoever it hits. The hook only reads: the game's own call runs first with
// the same arguments.
internal sealed unsafe class EffectCapture : IDisposable
{
    private const int EffectsPerTarget = 8;
    private const uint LimitBreakCategory = 9;

    private readonly IPluginLog log;
    private readonly Sessions sessions;
    private readonly FrozenSet<uint> limitBreaks;
    private readonly Hook<ActionEffectHandler.Delegates.Receive>? hook;

    public EffectCapture(IGameInteropProvider interop, IDataManager data, IPluginLog log, Sessions sessions)
    {
        this.log = log;
        this.sessions = sessions;
        limitBreaks = data.GetExcelSheet<ActionRow>()
                          .Where(a => a.ActionCategory.RowId == LimitBreakCategory)
                          .Select(a => a.RowId)
                          .ToFrozenSet();
        try
        {
            hook = interop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
                ActionEffectHandler.MemberFunctionPointers.Receive, OnReceive);
            hook.Enable();
        }
        catch (Exception e)
        {
            log.Warning(e, "Couldn't find where the game receives actions, so raises won't be recorded.");
        }
    }

    public void Dispose() => hook?.Dispose();

    private void OnReceive(uint casterEntityId, Character* caster, Vector3* targetPos, ActionEffectHandler.Header* header,
                           ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        hook!.Original(casterEntityId, caster, targetPos, header, effects, targetEntityIds);
        try
        {
            Capture(casterEntityId, caster, header, effects, targetEntityIds);
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't record an action.");
        }
    }

    private void Capture(uint casterEntityId, Character* caster, ActionEffectHandler.Header* header,
                         ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        if (!sessions.Recording || caster == null || Owner(casterEntityId, caster) is not { } owner)
            return;

        var self = sessions.SelfId;
        var count = header->NumTargets;
        var limitBreak = limitBreaks.Contains(header->ActionId);
        if (!limitBreak &&
            (owner == self ? !TargetsOtherPlayer(targetEntityIds, count, self) : !Targets(targetEntityIds, count, self)))
            return;

        if (owner != casterEntityId)
        {
            if (!sessions.KnowsPet(casterEntityId))
                sessions.Observe(new PetSeen(sessions.Ms, casterEntityId, owner));
        }
        else if (!sessions.Knows(casterEntityId))
        {
            sessions.Observe(new ActorSeen(sessions.Ms, casterEntityId, caster->NameString, caster->HomeWorld,
                                           caster->CharacterData.ClassJob));
        }

        var targets = new EffectTarget[count];
        for (var i = 0; i < count; i++)
            targets[i] = new EffectTarget(targetEntityIds[i].ObjectId, Pack((ulong*)(effects + i)));
        sessions.Observe(new EffectSeen(sessions.Ms, casterEntityId, header->ActionId, header->ActionType,
                                        header->GlobalSequence, targets, limitBreak));
    }

    // The player behind the action: the caster itself, or the owner of a pet such as a fairy or demi-summon.
    private uint? Owner(uint casterEntityId, Character* caster) => caster->ObjectKind switch
    {
        ObjectKind.Pc => casterEntityId,
        ObjectKind.BattleNpc when sessions.Knows(caster->OwnerId) => caster->OwnerId,
        _ => null,
    };

    private static bool Targets(GameObjectId* targetEntityIds, int count, uint entityId)
    {
        for (var i = 0; i < count; i++)
        {
            if (targetEntityIds[i].ObjectId == entityId)
                return true;
        }
        return false;
    }

    // Your own actions only matter when they reach a player you know of, not enemies or yourself.
    private bool TargetsOtherPlayer(GameObjectId* targetEntityIds, int count, uint self)
    {
        for (var i = 0; i < count; i++)
        {
            var target = targetEntityIds[i].ObjectId;
            if (target != self && sessions.Knows(target))
                return true;
        }
        return false;
    }

    private static ulong[] Pack(ulong* effects)
    {
        var length = EffectsPerTarget;
        while (length > 0 && effects[length - 1] == 0)
            length--;
        return new ReadOnlySpan<ulong>(effects, length).ToArray();
    }
}
