using System;
using System.Collections.Generic;
using System.Linq;

namespace Commendable;

// Turns observations into a duty summary. The same code runs live and on saved duties.
internal sealed class Interpreter
{
    private const byte ActionTypeAction = 1;
    public const uint NoEntity = 0xE0000000;

    private readonly Dictionary<uint, PlayerKey> keys = [];
    private readonly Dictionary<uint, uint> petOwners = [];
    private readonly HashSet<(uint Caster, uint Sequence)> effectsSeen = [];
    private readonly HashSet<PlayerKey> present = [];
    private readonly Dictionary<(Category Category, PlayerKey Source, PlayerKey Partner), Interaction> ongoing = [];
    private readonly Raises raises = new();
    private readonly Partners partners = new();
    private PlayerKey self;

    public DutySession? Session { get; private set; }

    public uint SelfId { get; private set; }

    public event Action<PlayerRecord, Interaction>? Recorded;

    public static DutySession? Replay(IEnumerable<Observation> observations)
    {
        var interpreter = new Interpreter();
        foreach (var observation in observations)
            interpreter.Apply(observation);
        return interpreter.Session;
    }

    public bool Knows(uint entityId) => keys.ContainsKey(entityId);

    public bool KnowsPet(uint entityId) => petOwners.ContainsKey(entityId);

    public void Apply(Observation observation)
    {
        if (observation is DutyStart start)
        {
            Start(start);
            return;
        }
        if (Session is not { } session)
            return;

        var at = session.StartedAt.AddMilliseconds(observation.Ms);
        switch (observation)
        {
            case SelfChanged changed:
                SelfId = changed.EntityId;
                keys[SelfId] = self;
                break;
            case RosterChanged roster:
                ApplyRoster(session, roster, at);
                break;
            case ActorSeen actor:
                ApplyActor(session, actor);
                break;
            case PetSeen pet:
                petOwners[pet.EntityId] = pet.OwnerId;
                break;
            case EffectSeen effect:
                ApplyEffect(session, effect, at);
                break;
            case StatusGained gained:
                ApplyStatusGained(session, gained, at);
                break;
            case StatusLost lost:
                ApplyStatusLost(session, lost, at);
                break;
            case AliveChanged alive:
                ApplyAlive(session, alive.Alive, at);
                break;
            case DutyComplete:
                session.CompletedAt ??= at;
                EndRelationships(session, at, _ => true);
                session.Version++;
                break;
            case DutyEnd:
                EndRelationships(session, at, _ => true);
                break;
            case Resumed:
                // Still-active relationships and an ongoing death are seen again on the next capture.
                EndRelationships(session, at, _ => true);
                raises.Reset();
                break;
        }
    }

    private void Start(DutyStart start)
    {
        if (Session != null)
            return;
        self = new PlayerKey(start.SelfName, start.SelfWorld);
        SelfId = start.SelfId;
        keys[SelfId] = self;
        Session = new DutySession(start.DutyId, start.DutyName, start.StartedAt);
    }

    private void ApplyRoster(DutySession session, RosterChanged roster, DateTime at)
    {
        var now = new HashSet<PlayerKey>();
        foreach (var member in roster.Members)
        {
            var key = new PlayerKey(member.Name, member.WorldId);
            if (member.EntityId is not (0 or NoEntity))
                keys[member.EntityId] = key;
            if (member.PetEntityId is not (0 or NoEntity) && member.EntityId is not (0 or NoEntity))
                petOwners[member.PetEntityId] = member.EntityId;
            if (key == self)
                continue;

            var player = Player(session, key);
            player.JobId = member.JobId;
            player.Order = member.Order;
            player.Membership = member.Membership;
            player.LeftAt = null;
            now.Add(key);
        }

        // Their statuses aren't seen once they're gone, so relationships with them end here.
        foreach (var key in present)
        {
            if (now.Contains(key))
                continue;
            session.Players[key].LeftAt ??= at;
            EndRelationships(session, at, slot => slot.Source == key || slot.Partner == key);
        }
        present.Clear();
        present.UnionWith(now);
        session.Version++;
    }

    private void ApplyActor(DutySession session, ActorSeen actor)
    {
        var key = new PlayerKey(actor.Name, actor.WorldId);
        keys[actor.EntityId] = key;
        if (key == self || session.Players.ContainsKey(key))
            return;
        Player(session, key).JobId = actor.JobId;
        session.Version++;
    }

    private void ApplyEffect(DutySession session, EffectSeen effect, DateTime at)
    {
        if (effect.ActionType != ActionTypeAction || !TryKey(effect.Caster, out var caster) || caster == self ||
            !effectsSeen.Add((effect.Caster, effect.Sequence)))
            return;

        var player = Player(session, caster);
        if (effect.LimitBreak)
            Add(session, player, Incoming(at, effect.ActionId, Category.LimitBreakUsed));
        if (Array.Find(effect.Targets, t => t.EntityId == SelfId) is not { } mine)
            return;

        if (Value(mine, Registry.StatusAppliedEffect, Registry.CardIsOffensive.ContainsKey) is { } card)
        {
            Add(session, player, Incoming(at, effect.ActionId, Category.Cards, statusId: card));
            return;
        }
        if (!Registry.Actions.TryGetValue(effect.ActionId, out var category))
            return;

        switch (category)
        {
            case Category.LimitBreakRevival:
                raises.LimitBreakLanded(effect.Caster, effect.ActionId, at);
                break;
            case Category.Cleanse when effect.ActionId == Registry.Esuna:
                if (Value(mine, Registry.StatusRemovedEffect, _ => true) is { } removed)
                    Add(session, player, Incoming(at, effect.ActionId, category, statusId: removed));
                break;
            case Category.Resurrection:
                Add(session, player, Incoming(at, effect.ActionId, category, Outcome.Offered));
                break;
            default:
                Add(session, player, Incoming(at, effect.ActionId, category));
                break;
        }
    }

    private void ApplyStatusGained(DutySession session, StatusGained gained, DateTime at)
    {
        if (gained.EntityId == SelfId && Registry.RaiseStatuses.Contains(gained.StatusId))
            raises.Gained(gained.SourceId);

        // Choices between two other players still count, so switching away and back again shows up.
        if (!Registry.PartnerStatuses.TryGetValue(gained.StatusId, out var partner) ||
            !TryKey(gained.SourceId, out var source) || !TryKey(gained.EntityId, out var holder) || source == holder)
            return;
        var newChoice = partners.Chose(partner.Category, source, holder);

        PlayerKey other;
        Direction direction;
        if (source == self)
            (other, direction) = (holder, Direction.Outgoing);
        else if (holder == self)
            (other, direction) = (source, Direction.Incoming);
        else
            return;

        var slot = (partner.Category, source, holder);
        if (ongoing.ContainsKey(slot))
            return;
        var relationship = new Interaction
        {
            At = at,
            ActionId = partner.ActionId,
            StatusId = gained.StatusId,
            Category = partner.Category,
            Direction = direction,
            Continues = !newChoice,
        };
        ongoing[slot] = relationship;
        Add(session, Player(session, other), relationship);
    }

    private void ApplyStatusLost(DutySession session, StatusLost lost, DateTime at)
    {
        if (lost.EntityId == SelfId && Registry.RaiseStatuses.Contains(lost.StatusId))
            raises.Lost(at);

        if (Registry.PartnerStatuses.TryGetValue(lost.StatusId, out var partner) &&
            TryKey(lost.SourceId, out var source) && TryKey(lost.EntityId, out var holder) &&
            ongoing.Remove((partner.Category, source, holder), out var relationship))
        {
            relationship.EndedAt = at;
            session.Version++;
        }
    }

    private void EndRelationships(DutySession session, DateTime at,
                                  Func<(Category Category, PlayerKey Source, PlayerKey Partner), bool> which)
    {
        foreach (var slot in ongoing.Keys.Where(which).ToList())
        {
            ongoing.Remove(slot, out var relationship);
            relationship!.EndedAt = at;
            session.Version++;
        }
    }

    private void ApplyAlive(DutySession session, bool alive, DateTime at)
    {
        if (raises.AliveChanged(alive, at) is not { } revival || !TryKey(revival.By, out var key) || key == self)
            return;

        var player = Player(session, key);
        if (revival.LimitBreak != 0)
        {
            Add(session, player, Incoming(at, revival.LimitBreak, Category.LimitBreakRevival, Outcome.Accepted));
            return;
        }

        var offer = player.Interactions.FindLast(i =>
            i is { Category: Category.Resurrection, Direction: Direction.Incoming, Outcome: Outcome.Offered } &&
            i.At >= revival.DiedAt);
        if (offer == null)
        {
            Add(session, player, Incoming(at, 0, Category.Resurrection, Outcome.Accepted));
            return;
        }

        offer.Outcome = Outcome.Accepted;
        Touch(session, player, at);
        Recorded?.Invoke(player, offer);
    }

    // A pet's actions and statuses count as its owner's.
    private bool TryKey(uint entityId, out PlayerKey key) =>
        keys.TryGetValue(entityId, out key) ||
        (petOwners.TryGetValue(entityId, out var owner) && keys.TryGetValue(owner, out key));

    private static ushort? Value(EffectTarget target, byte type, Func<ushort, bool> matches)
    {
        foreach (var effect in target.Effects)
        {
            var value = (ushort)(effect >> 48);
            if ((byte)effect == type && matches(value))
                return value;
        }
        return null;
    }

    private static Interaction Incoming(DateTime at, uint actionId, Category category, Outcome outcome = Outcome.None,
                                        uint statusId = 0) =>
        new()
        {
            At = at,
            ActionId = actionId,
            StatusId = statusId,
            Category = category,
            Direction = Direction.Incoming,
            Outcome = outcome,
        };

    private void Add(DutySession session, PlayerRecord player, Interaction interaction)
    {
        player.Interactions.Add(interaction);
        Touch(session, player, interaction.At);
        Recorded?.Invoke(player, interaction);
    }

    private static void Touch(DutySession session, PlayerRecord player, DateTime at)
    {
        if (player.LastAt == null || at > player.LastAt)
            player.LastAt = at;
        session.Version++;
    }

    private static PlayerRecord Player(DutySession session, PlayerKey key)
    {
        if (!session.Players.TryGetValue(key, out var player))
            session.Players[key] = player = new PlayerRecord(key);
        return player;
    }
}
