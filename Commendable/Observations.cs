using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Commendable;

// What the plugin saw in the game, before any rule decides what it means. Ms counts from the duty's start.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "k")]
[JsonDerivedType(typeof(DutyStart), "start")]
[JsonDerivedType(typeof(SelfChanged), "self")]
[JsonDerivedType(typeof(RosterChanged), "roster")]
[JsonDerivedType(typeof(ActorSeen), "actor")]
[JsonDerivedType(typeof(PetSeen), "pet")]
[JsonDerivedType(typeof(EffectSeen), "effect")]
[JsonDerivedType(typeof(StatusGained), "gain")]
[JsonDerivedType(typeof(StatusLost), "lose")]
[JsonDerivedType(typeof(AliveChanged), "alive")]
[JsonDerivedType(typeof(DutyComplete), "complete")]
[JsonDerivedType(typeof(DutyEnd), "end")]
[JsonDerivedType(typeof(Resumed), "resume")]
internal abstract record Observation(long Ms);

internal sealed record DutyStart(
    long Ms, int Format, DateTime StartedAt, uint TerritoryId, uint DutyId, string DutyName,
    uint SelfId, string SelfName, uint SelfWorld, uint SelfJob) : Observation(Ms);

internal sealed record SelfChanged(long Ms, uint EntityId) : Observation(Ms);

internal sealed record Member(
    uint EntityId, string Name, uint WorldId, uint JobId, Membership Membership, int Order, uint PetEntityId = 0);

// A pet or summon whose actions and statuses count as its owner's, like a fairy's Fey Union or Rekindle.
internal sealed record PetSeen(long Ms, uint EntityId, uint OwnerId) : Observation(Ms);

internal sealed record RosterChanged(long Ms, Member[] Members) : Observation(Ms);

internal sealed record ActorSeen(long Ms, uint EntityId, string Name, uint WorldId, uint JobId) : Observation(Ms);

// Each effect is the game's 8-byte entry as is: type, five params, then a 16-bit value in the top bytes.
internal sealed record EffectTarget(uint EntityId, ulong[] Effects);

// LimitBreak comes from the game's action sheet when it's recorded, so replays don't need game data.
internal sealed record EffectSeen(
    long Ms, uint Caster, uint ActionId, byte ActionType, uint Sequence, EffectTarget[] Targets,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool LimitBreak = false)
    : Observation(Ms);

internal sealed record StatusGained(long Ms, uint EntityId, ushort StatusId, uint SourceId) : Observation(Ms);

internal sealed record StatusLost(long Ms, uint EntityId, ushort StatusId, uint SourceId) : Observation(Ms);

internal sealed record AliveChanged(long Ms, bool Alive) : Observation(Ms);

internal sealed record DutyComplete(long Ms) : Observation(Ms);

internal sealed record DutyEnd(long Ms) : Observation(Ms);

// The plugin picked the duty back up after a reload or restart; whatever changed in between wasn't seen.
internal sealed record Resumed(long Ms) : Observation(Ms);

internal static class ObservationJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(Observation observation) => JsonSerializer.Serialize(observation, Options);

    public static Observation? Read(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<Observation>(line, Options);
        }
        // A line without a known kind can't become the abstract Observation, which throws NotSupportedException.
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
