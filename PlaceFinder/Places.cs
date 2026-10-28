using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace PlaceFinder;

// Pixel is on the 2048px map texture at MapTexture.
internal sealed record Place(string Name, uint TerritoryId, uint MapId, string MapTexture, string Zone, Vector2 Pixel,
                             Vector2 MapCoord, Vector2 World);

internal sealed record Zone(string Name, uint Expansion, List<Place> Places);

internal sealed record Region(string Name, List<Zone> Zones);

internal sealed class Places
{
    // TerritoryIntendedUse: towns and open-world fields. Other territories only show while you're in them.
    private static readonly HashSet<uint> Browsable = [0, 1];
    // MapMarker.DataType: an exit icon labelled with the zone it leads to.
    private const byte ZoneExit = 1;

    private readonly Dictionary<uint, List<Place>> byTerritory = new();

    public List<Region> Regions { get; } = new();
    public List<Zone> Zones { get; } = new();
    public List<(uint Id, string Name)> Expansions { get; } = new();

    public Places(IDataManager data)
    {
        var markers = data.GetSubrowExcelSheet<MapMarker>();
        var regions = new Dictionary<string, (uint ExVersion, Dictionary<string, Zone> Zones)>();

        foreach (var map in data.GetExcelSheet<Map>())
        {
            if (map.TerritoryType.RowId == 0 || !markers.TryGetRow(map.MapMarkerRange, out var subrows))
                continue;

            var zoneName = OneLine(map.PlaceName.ValueNullable?.Name.ExtractText());
            if (zoneName.Length == 0)
                continue;
            if (OneLine(map.PlaceNameSub.ValueNullable?.Name.ExtractText()) is { Length: > 0 } sub)
                zoneName = $"{zoneName} – {sub}";

            var id = map.Id.ExtractText();
            var texture = $"ui/map/{id}/{id.Replace("/", "")}_m.tex";
            var scale = map.SizeFactor / 100f;
            var seen = new HashSet<uint>();
            var places = new List<Place>();
            foreach (var marker in subrows)
            {
                if (marker.DataType == ZoneExit || !seen.Add(marker.PlaceNameSubtext.RowId))
                    continue;
                var name = OneLine(marker.PlaceNameSubtext.ValueNullable?.Name.ExtractText());
                if (name.Length == 0)
                    continue;

                var pixel = new Vector2(marker.X, marker.Y);
                var world = (pixel - new Vector2(1024)) / scale - new Vector2(map.OffsetX, map.OffsetY);
                var coord = 41f / scale * (pixel / 2048f) + Vector2.One;
                places.Add(new Place(name, map.TerritoryType.RowId, map.RowId, texture, zoneName, pixel, coord, world));
            }
            if (places.Count == 0)
                continue;

            places.Sort(ByName);
            if (!byTerritory.TryGetValue(map.TerritoryType.RowId, out var list))
                byTerritory[map.TerritoryType.RowId] = list = new();
            list.AddRange(places);

            var territory = map.TerritoryType.Value;
            if (!Browsable.Contains(territory.TerritoryIntendedUse.RowId))
                continue;

            var regionName = OneLine(map.PlaceNameRegion.ValueNullable?.Name.ExtractText()) is { Length: > 0 } r ? r : "Other";
            if (!regions.TryGetValue(regionName, out var region))
                regions[regionName] = region = (territory.ExVersion.RowId, new());
            // Housing wards and instanced copies share a zone name; the first map is enough.
            region.Zones.TryAdd(zoneName, new Zone(zoneName, territory.ExVersion.RowId, places));
        }

        foreach (var (name, region) in regions.OrderBy(r => r.Value.ExVersion).ThenBy(r => r.Key))
            Regions.Add(new Region(name, region.Zones.Values.OrderBy(z => SortKey(z.Name)).ToList()));
        Zones.AddRange(Regions.SelectMany(r => r.Zones).OrderBy(z => SortKey(z.Name)));

        var used = Zones.Select(z => z.Expansion).ToHashSet();
        foreach (var expansion in data.GetExcelSheet<ExVersion>())
        {
            if (used.Contains(expansion.RowId))
                Expansions.Add((expansion.RowId, expansion.Name.ExtractText()));
        }
    }

    public IReadOnlyList<Place> In(uint territoryId) =>
        byTerritory.TryGetValue(territoryId, out var list) ? list : [];

    public List<Place> Search(string text, IEnumerable<Zone> zones)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = zones.SelectMany(z => z.Places)
                         .Where(p => words.All(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                                                    p.Zone.Contains(w, StringComparison.OrdinalIgnoreCase)))
                         .ToList();
        found.Sort(ByName);
        return found;
    }

    private static int ByName(Place a, Place b) =>
        string.Compare(SortKey(a.Name), SortKey(b.Name), StringComparison.OrdinalIgnoreCase);

    // Map labels break long names over lines, e.g. "Fisherman's Bottom" over "(Fishermen's Guild)".
    private static string OneLine(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split(['\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries));

    public static string Letter(string name) =>
        SortKey(name) is { Length: > 0 } key && char.IsLetter(key[0]) ? char.ToUpperInvariant(key[0]).ToString() : "#";

    // So The Slow Wash sorts under S.
    private static string SortKey(string name) => name.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
}
