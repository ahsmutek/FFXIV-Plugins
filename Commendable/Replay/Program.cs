using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Commendable;
using Lumina;
using Lumina.Excel.Sheets;
using ActionRow = Lumina.Excel.Sheets.Action;

// dotnet run --project Commendable/Replay -- [n | path] [--log] [--raw]
// No argument replays every saved duty, oldest first; n is the n-th newest. --log adds each player's event log,
// --raw what the tracked rules read.
Console.OutputEncoding = Encoding.UTF8;
var raw = args.Contains("--raw");
var log = args.Contains("--log");
var target = args.FirstOrDefault(a => !a.StartsWith("--"));
var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var folder = Path.Combine(appData, "XIVLauncher", "pluginConfigs", "Commendable", "Journal");

var saved = Journal.Saved(folder);
List<string> files = target switch
{
    null => saved.AsEnumerable().Reverse().ToList(),
    _ when int.TryParse(target, out var n) => saved.Skip(n - 1).Take(1).ToList(),
    _ => [target],
};
if (files.Count == 0)
{
    Console.Error.WriteLine($"No saved duties in {folder}.");
    return 1;
}

var names = Names.Load(appData);
foreach (var file in files)
    Print(file);
return 0;

void Print(string file)
{
    var observations = Journal.Read(file).ToList();
    Console.WriteLine($"== {Path.GetFileName(file)} ({observations.Count} observations)");
    if (Interpreter.Replay(observations) is not { } session)
    {
        Console.WriteLine("   No duty start in this file.");
        return;
    }

    Console.WriteLine($"   {session.DutyName}, {(session.IsCompleted ? "complete" : "not completed")}");
    var end = session.CompletedAt ?? session.StartedAt.AddMilliseconds(observations[^1].Ms);
    foreach (var player in session.Players.Values.OrderBy(p => p.Membership).ThenBy(p => p.Order))
    {
        var left = player.LeftAt is { } at ? $" (left at {Clock(at - session.StartedAt)})" : "";
        Console.WriteLine($"   {names.Job(player.JobId),-4} {player.Key.Name} @{names.World(player.Key.WorldId)} " +
                          $"[{player.Membership}]{left}");
        foreach (var line in Lines.Of(player, _ => true, names.Action, end))
        {
            var arrow = line.Direction == Direction.Outgoing ? "→" : "←";
            var note = line.Highlight ? "  (from a non-healer)" : "";
            Console.WriteLine($"        {arrow} {line.Text}{note}");
        }
        if (log)
        {
            foreach (var interaction in player.Interactions.OrderBy(i => i.At))
                Console.WriteLine($"            {Lines.Event(interaction, session.StartedAt, names.Action, names.Status)}");
        }
    }

    if (raw)
        PrintRaw(observations);
    Console.WriteLine();
}

void PrintRaw(List<Observation> observations)
{
    var who = new Dictionary<uint, string>();
    var partnerActions = Registry.PartnerStatuses.Values.Select(p => p.ActionId).ToHashSet();
    var otherEffects = 0;
    Console.WriteLine("   -- raw");
    foreach (var observation in observations)
    {
        var time = Clock(TimeSpan.FromMilliseconds(observation.Ms));
        switch (observation)
        {
            case DutyStart start:
                who[start.SelfId] = "you";
                break;
            case SelfChanged self:
                who[self.EntityId] = "you";
                Console.WriteLine($"   {time} you are now {self.EntityId:X8}");
                break;
            case RosterChanged roster:
                foreach (var member in roster.Members)
                    who[member.EntityId] = member.Name;
                Console.WriteLine($"   {time} roster: {string.Join(", ", roster.Members.Select(m => m.Name))}");
                break;
            case ActorSeen actor:
                who[actor.EntityId] = actor.Name;
                Console.WriteLine($"   {time} seen {actor.Name} ({names.Job(actor.JobId)})");
                break;
            case EffectSeen effect when effect.LimitBreak || Registry.Actions.ContainsKey(effect.ActionId) ||
                                        partnerActions.Contains(effect.ActionId) || GivesCard(effect):
                Console.WriteLine($"   {time} {Name(effect.Caster)} used {names.Action(effect.ActionId)} " +
                                  $"(type {effect.ActionType}, seq {effect.Sequence})");
                foreach (var t in effect.Targets)
                    Console.WriteLine($"            on {Name(t.EntityId)}: {string.Join(" | ", t.Effects.Select(Decode))}");
                break;
            case EffectSeen:
                otherEffects++;
                break;
            case StatusGained gained when Tracked(gained.StatusId):
                Console.WriteLine($"   {time} {Name(gained.EntityId)} gained {names.Status(gained.StatusId)} from {Name(gained.SourceId)}");
                break;
            case StatusLost lost when Tracked(lost.StatusId):
                Console.WriteLine($"   {time} {Name(lost.EntityId)} lost {names.Status(lost.StatusId)} from {Name(lost.SourceId)}");
                break;
            case AliveChanged alive:
                Console.WriteLine($"   {time} you {(alive.Alive ? "are alive" : "died")}");
                break;
            case DutyComplete:
                Console.WriteLine($"   {time} duty complete");
                break;
            case DutyEnd:
                Console.WriteLine($"   {time} left the duty");
                break;
        }
    }
    Console.WriteLine($"   ({otherEffects} other actions between players and you)");

    string Name(uint entityId) => who.TryGetValue(entityId, out var name) ? name : $"{entityId:X8}";
}

static bool Tracked(ushort statusId) =>
    Registry.RaiseStatuses.Contains(statusId) || Registry.PartnerStatuses.ContainsKey(statusId) ||
    Registry.CardIsOffensive.ContainsKey(statusId);

static bool GivesCard(EffectSeen effect) =>
    effect.Targets.Any(t => t.Effects.Any(e => (byte)e == Registry.StatusAppliedEffect &&
                                               Registry.CardIsOffensive.ContainsKey((ushort)(e >> 48))));

static string Decode(ulong effect) =>
    $"type {effect & 0xFF} params {(effect >> 8) & 0xFF} {(effect >> 16) & 0xFF} {(effect >> 24) & 0xFF} " +
    $"{(effect >> 32) & 0xFF} {(effect >> 40) & 0xFF} value {effect >> 48}";

static string Clock(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";

internal sealed class Names(GameData? game)
{
    public static Names Load(string appData)
    {
        try
        {
            var launcher = File.ReadAllText(Path.Combine(appData, "XIVLauncher", "launcherConfigV3.json"));
            var gamePath = JsonDocument.Parse(launcher).RootElement.GetProperty("GamePath").GetString() ?? "";
            return new Names(new GameData(Path.Combine(gamePath, "game", "sqpack")));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Couldn't open the game data ({e.Message}); showing IDs instead of names.");
            return new Names(null);
        }
    }

    public string Action(uint id) => Text(game?.GetExcelSheet<ActionRow>()?.GetRowOrDefault(id)?.Name.ExtractText(), $"action {id}");

    public string Status(uint id) => Text(game?.GetExcelSheet<Status>()?.GetRowOrDefault(id)?.Name.ExtractText(), $"status {id}");

    public string Job(uint id) => Text(game?.GetExcelSheet<ClassJob>()?.GetRowOrDefault(id)?.Abbreviation.ExtractText(), $"#{id}");

    public string World(uint id) => Text(game?.GetExcelSheet<World>()?.GetRowOrDefault(id)?.Name.ExtractText(), $"world {id}");

    private static string Text(string? name, string fallback) => string.IsNullOrEmpty(name) ? fallback : name;
}
