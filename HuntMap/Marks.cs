using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace HuntMap;

// B-rank spawn points from ffxiv-huntmaps-maker's marks.json, in map coordinates, by English name and zone.
internal sealed class Marks
{
    private sealed record Entry(string Name, string Rank, string Zone, List<float[]> Spawns);

    private readonly Dictionary<(string, string), List<Vector2>> spawns = new();

    public Marks()
    {
        using var stream = typeof(Marks).Assembly.GetManifestResourceStream("marks.json")
                           ?? throw new InvalidOperationException("marks.json isn't embedded.");
        using var reader = new StreamReader(stream);
        var entries = JsonSerializer.Deserialize<List<Entry>>(reader.ReadToEnd(),
                                                              new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        foreach (var entry in entries.Where(e => e.Rank == "B"))
        {
            // Some lists repeat a point.
            spawns[Key(entry.Name, entry.Zone)] = entry.Spawns.Where(s => s.Length >= 2)
                                                              .Select(s => new Vector2(s[0], s[1]))
                                                              .Distinct()
                                                              .ToList();
        }
    }

    public List<Vector2>? For(string name, string zone) => spawns.GetValueOrDefault(Key(name, zone));

    private static (string, string) Key(string name, string zone) => (name.ToLowerInvariant(), zone.ToLowerInvariant());
}
