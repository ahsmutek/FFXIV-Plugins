using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace HuntMap;

internal sealed record MapInfo(uint TerritoryId, uint MapId, string Texture, float Scale, Vector2 Offset)
{
    public Vector2 CoordOf(Vector2 world) => 41f / Scale * (((world + Offset) * Scale + new Vector2(1024)) / 2048f) + Vector2.One;

    // On the 2048px map texture.
    public Vector2 PixelOf(Vector2 coord) => (coord - Vector2.One) * Scale / 41f * 2048f;

    public Vector2 WorldOf(Vector2 pixel) => (pixel - new Vector2(1024)) / Scale - Offset;
}

internal sealed record Spot(int Index, Vector2 Coord, Vector2 Pixel, Vector2 World);

// Key is the English name and zone; checks are kept by it.
internal sealed record Mark(string Key, string Name, string Rank, string Zone, MapInfo Map, List<Spot> Spots);

internal sealed record Zone(string Name, uint Expansion, MapInfo Map, List<Mark> Marks);

internal sealed record Region(string Name, List<Zone> Zones);

// Spawn points from ffxiv-huntmaps-maker's marks.json, in map coordinates, by English name and zone.
internal sealed class Marks
{
    public static readonly string[] Ranks = ["B", "A", "S", "SS"];

    private sealed record Entry(string Name, string Rank, string Zone, List<float[]> Spawns);

    private readonly Dictionary<string, Mark> byKey = new();
    private readonly Dictionary<uint, Zone> byTerritory = new();

    public List<Region> Regions { get; } = new();
    public List<(uint Id, string Name)> Expansions { get; } = new();

    public Marks(IDataManager data)
    {
        using var stream = typeof(Marks).Assembly.GetManifestResourceStream("marks.json")
                           ?? throw new InvalidOperationException("marks.json isn't embedded.");
        using var reader = new StreamReader(stream);
        var entries = JsonSerializer.Deserialize<List<Entry>>(reader.ReadToEnd(),
                                                              new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var englishPlaces = data.GetExcelSheet<PlaceName>(ClientLanguage.English);
        string English(Map map) => englishPlaces.GetRowOrDefault(map.PlaceName.RowId)?.Name.ExtractText() ?? "";

        // Hunt targets' own maps first, so a bill's target and its mark share one map.
        var maps = new Dictionary<string, Map>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in data.GetExcelSheet<MobHuntTarget>())
        {
            if (target.Map.ValueNullable is { } map && English(map) is { Length: > 0 } zone)
                maps.TryAdd(Plain(zone), map);
        }
        foreach (var map in data.GetExcelSheet<Map>())
        {
            if (map.TerritoryType.ValueNullable?.TerritoryIntendedUse.RowId == 1 && English(map) is { Length: > 0 } zone)
                maps.TryAdd(Plain(zone), map);
        }

        var wanted = entries.Select(e => Plain(e.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var npcs = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var npc in data.GetExcelSheet<BNpcName>(ClientLanguage.English))
        {
            if (Plain(npc.Singular.ExtractText()) is var name && wanted.Contains(name))
                npcs.TryAdd(name, npc.RowId);
        }
        var names = data.GetExcelSheet<BNpcName>();

        var regions = new Dictionary<string, (uint ExVersion, Dictionary<string, Zone> Zones)>();
        foreach (var entry in entries)
        {
            if (!Ranks.Contains(entry.Rank) || !maps.TryGetValue(Plain(entry.Zone), out var map) || byKey.ContainsKey(Key(entry.Name, entry.Zone)))
                continue;

            var id = map.Id.ExtractText();
            var info = new MapInfo(map.TerritoryType.RowId, map.RowId, $"ui/map/{id}/{id.Replace("/", "")}_m.tex", map.SizeFactor / 100f,
                                   new Vector2(map.OffsetX, map.OffsetY));
            // Some lists repeat a point.
            var spots = entry.Spawns.Where(s => s.Length >= 2)
                             .Select(s => new Vector2(s[0], s[1]))
                             .Distinct()
                             .Select((coord, i) =>
                             {
                                 var pixel = info.PixelOf(coord);
                                 return new Spot(i, coord, pixel, info.WorldOf(pixel));
                             })
                             .ToList();
            var name = npcs.TryGetValue(Plain(entry.Name), out var npc) && names.GetRowOrDefault(npc)?.Singular.ExtractText() is { Length: > 0 } local
                           ? local
                           : entry.Name;
            var zoneName = map.PlaceName.ValueNullable?.Name.ExtractText() is { Length: > 0 } z ? z : entry.Zone;
            var mark = new Mark(Key(entry.Name, entry.Zone), Title(name), entry.Rank, zoneName, info, spots);
            byKey[mark.Key] = mark;

            var regionName = map.PlaceNameRegion.ValueNullable?.Name.ExtractText() is { Length: > 0 } r ? r : "Other";
            var expansion = map.TerritoryType.ValueNullable?.ExVersion.RowId ?? 0;
            if (!regions.TryGetValue(regionName, out var region))
                regions[regionName] = region = (expansion, new());
            if (!region.Zones.TryGetValue(zoneName, out var zone))
                region.Zones[zoneName] = byTerritory[info.TerritoryId] = zone = new Zone(zoneName, expansion, info, new());
            zone.Marks.Add(mark);
        }

        foreach (var (name, region) in regions.OrderBy(r => r.Value.ExVersion).ThenBy(r => r.Key))
        {
            foreach (var zone in region.Zones.Values)
                zone.Marks.Sort(ByRank);
            Regions.Add(new Region(name, region.Zones.Values.OrderBy(z => SortKey(z.Name)).ToList()));
        }

        var used = Regions.SelectMany(r => r.Zones).Select(z => z.Expansion).ToHashSet();
        foreach (var expansion in data.GetExcelSheet<ExVersion>())
        {
            if (used.Contains(expansion.RowId))
                Expansions.Add((expansion.RowId, expansion.Name.ExtractText()));
        }
    }

    public Mark? ByKey(string key) => byKey.GetValueOrDefault(key);

    public Mark? Find(string englishName, string englishZone) => byKey.GetValueOrDefault(Key(englishName, englishZone));

    public IReadOnlyList<Mark> In(uint territoryId) => byTerritory.TryGetValue(territoryId, out var zone) ? zone.Marks : [];

    // A word can also be a rank, e.g. "s".
    public static List<Mark> Search(string text, IEnumerable<Mark> marks)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = marks.Where(m => words.All(w => m.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                                                    m.Zone.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                                                    m.Rank.Equals(w, StringComparison.OrdinalIgnoreCase)))
                         .ToList();
        found.Sort(ByName);
        return found;
    }

    public static string Letter(string name) =>
        SortKey(name) is { Length: > 0 } key && char.IsLetter(key[0]) ? char.ToUpperInvariant(key[0]).ToString() : "#";

    private static int ByRank(Mark a, Mark b) =>
        Array.IndexOf(Ranks, a.Rank) - Array.IndexOf(Ranks, b.Rank) is var rank && rank != 0 ? rank : ByName(a, b);

    private static int ByName(Mark a, Mark b) =>
        string.Compare(SortKey(a.Name), SortKey(b.Name), StringComparison.OrdinalIgnoreCase);

    private static string SortKey(string name) => name.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;

    private static string Key(string name, string zone) => Plain($"{name}|{zone}").ToLowerInvariant();

    // The game writes some names with a curly apostrophe, e.g. Kozama’uka.
    private static string Plain(string text) => text.Replace('’', '\'');

    // Some names are stored lowercase, e.g. "mad maguey".
    private static string Title(string name) =>
        name.Length > 0 && char.IsLower(name[0])
            ? string.Join(' ', name.Split(' ').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w))
            : name;
}
