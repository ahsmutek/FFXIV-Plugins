using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Commendable;

internal sealed unsafe class PartyCapture : IDisposable
{
    private const int AllianceOrder = 100;
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);

    private readonly IFramework framework;
    private readonly ICondition condition;
    private readonly Sessions sessions;

    private readonly List<Slot> roster = [];
    private readonly List<Slot> scratch = [];
    private readonly Dictionary<uint, HashSet<(ushort StatusId, uint SourceId)>> statuses = [];
    private readonly HashSet<(ushort StatusId, uint SourceId)> current = [];
    private DutySession? session;
    private bool? alive;
    private DateTime nextAt;

    private readonly record struct Slot(ulong ContentId, uint EntityId, uint PetEntityId, byte Job, int Order,
                                        Membership Membership, int Index);

    public PartyCapture(IFramework framework, ICondition condition, Sessions sessions)
    {
        this.framework = framework;
        this.condition = condition;
        this.sessions = sessions;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    private void OnUpdate(IFramework _)
    {
        if (sessions.Current != session)
        {
            session = sessions.Current;
            roster.Clear();
            statuses.Clear();
            alive = null;
            nextAt = default;
        }
        if (session == null || condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
            return;

        var player = Control.GetLocalPlayer();
        if (player == null)
            return;
        var self = player->EntityId;
        if (self != sessions.SelfId)
            sessions.Observe(new SelfChanged(sessions.Ms, self));

        // The roster goes first, so statuses from players who just appeared can be credited.
        var tick = DateTime.UtcNow >= nextAt;
        if (tick)
        {
            nextAt = DateTime.UtcNow + Every;
            Roster(self);
        }

        // Departures keep updating after the duty is complete; combat stops being recorded.
        if (!sessions.Recording)
            return;

        var dead = player->IsDead();
        if (alive != !dead)
        {
            alive = !dead;
            sessions.Observe(new AliveChanged(sessions.Ms, !dead));
        }

        // Every frame while dead, so a raise accepted between ticks still shows who sent it.
        if (dead || tick)
        {
            var manager = player->GetStatusManager();
            if (manager != null)
                Diff(self, ref *manager);
        }
        if (tick)
            PartyStatuses(self);
    }

    private void Roster(uint self)
    {
        var group = &GroupManager.Instance()->MainGroup;
        var hud = AgentHUD.Instance();
        scratch.Clear();
        for (var i = 0; i < group->MemberCount; i++)
        {
            ref var member = ref group->PartyMembers[i];
            if (Listed(ref member, self))
                scratch.Add(new Slot(member.ContentId, member.EntityId, member.PetEntityId, member.ClassJob,
                                     HudOrder(hud, member.ContentId) ?? group->PartyMembers.Length + i, Membership.Party, i));
        }
        if (group->IsAlliance)
        {
            for (var i = 0; i < group->AllianceMembers.Length; i++)
            {
                ref var member = ref group->AllianceMembers[i];
                if (Listed(ref member, self))
                    scratch.Add(new Slot(member.ContentId, member.EntityId, member.PetEntityId, member.ClassJob,
                                         AllianceOrder + i, Membership.Alliance, i));
            }
        }

        if (CollectionsMarshal.AsSpan(scratch).SequenceEqual(CollectionsMarshal.AsSpan(roster)))
            return;
        roster.Clear();
        roster.AddRange(scratch);

        var members = new Member[roster.Count];
        for (var i = 0; i < members.Length; i++)
        {
            var slot = roster[i];
            ref var member = ref slot.Membership == Membership.Party
                                 ? ref group->PartyMembers[slot.Index]
                                 : ref group->AllianceMembers[slot.Index];
            members[i] = new Member(member.EntityId, member.NameString, member.HomeWorld, member.ClassJob, slot.Membership,
                                    slot.Order, member.PetEntityId);
        }
        sessions.Observe(new RosterChanged(sessions.Ms, members));
    }

    // Trust and story allies have no content ID; a disconnected player keeps their slot without an entity.
    private static bool Listed(ref PartyMember member, uint self) =>
        member.ContentId != 0 && member.EntityId != self && member.Name[0] != 0;

    private static int? HudOrder(AgentHUD* hud, ulong contentId)
    {
        if (hud == null)
            return null;
        for (var i = 0; i < hud->PartyMemberCount; i++)
        {
            if (hud->PartyMembers[i].ContentId == contentId)
                return i;
        }
        return null;
    }

    private void PartyStatuses(uint self)
    {
        var group = &GroupManager.Instance()->MainGroup;
        for (var i = 0; i < group->MemberCount; i++)
        {
            ref var member = ref group->PartyMembers[i];
            if (member.EntityId is not (0 or Interpreter.NoEntity) && member.EntityId != self)
                Diff(member.EntityId, ref member.StatusManager);
        }
    }

    private void Diff(uint entityId, ref StatusManager manager)
    {
        if (!statuses.TryGetValue(entityId, out var previous))
            statuses[entityId] = previous = [];

        current.Clear();
        foreach (ref var status in manager.Status)
        {
            if (status.StatusId != 0)
                current.Add((status.StatusId, status.SourceObject.ObjectId));
        }

        foreach (var (statusId, sourceId) in current)
        {
            if (!previous.Contains((statusId, sourceId)))
                sessions.Observe(new StatusGained(sessions.Ms, entityId, statusId, sourceId));
        }
        foreach (var (statusId, sourceId) in previous)
        {
            if (!current.Contains((statusId, sourceId)))
                sessions.Observe(new StatusLost(sessions.Ms, entityId, statusId, sourceId));
        }
        previous.Clear();
        previous.UnionWith(current);
    }
}
