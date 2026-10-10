using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace KeepInMind;

// An item with several sources goes in whichever of their groups comes first in the group order.
[Flags]
public enum Source
{
    None = 0,
    Botany = 1,
    Mining = 2,
    Fishing = 4,
    Vendor = 8,
    Crafting = 16,
}

internal sealed class Sources
{
    public static readonly Source[] Order = [Source.Botany, Source.Mining, Source.Fishing, Source.Vendor, Source.Crafting];
    public static readonly Source[] Gathering = [Source.Botany, Source.Mining, Source.Fishing];

    public static readonly Vector4 DefaultStorageColor = new(1f, 0.85f, 0.3f, 1f);
    public static readonly Vector4 DefaultStorageEnoughColor = new(0.4f, 0.9f, 0.4f, 1f);

    public static readonly IReadOnlyDictionary<Source, Vector4> DefaultColors = new Dictionary<Source, Vector4>
    {
        [Source.Botany] = new(0.55f, 0.85f, 0.45f, 1f),
        [Source.Mining] = new(0.9f, 0.6f, 0.5f, 1f),
        [Source.Fishing] = new(0.45f, 0.75f, 1f, 1f),
        [Source.Vendor] = new(0.85f, 0.6f, 1f, 1f),
        [Source.Crafting] = new(0.5f, 0.9f, 0.85f, 1f),
    };

    public readonly FrozenSet<uint> Gatherables;
    public readonly FrozenSet<uint> Fish;
    public readonly FrozenSet<uint> Spearfish;
    public readonly FrozenSet<uint> Craftables;

    private readonly FrozenSet<uint> botany;
    private readonly FrozenSet<uint> mining;
    private readonly FrozenDictionary<(Source, uint), Zone> zones;

    public Sources(IDataManager data)
    {
        var gatheringItems = data.GetExcelSheet<GatheringItem>();
        Gatherables = gatheringItems.Select(g => g.Item.RowId).Where(IsItem).ToFrozenSet();

        var byType = data.GetExcelSheet<GatheringPointBase>()
                         .Where(p => p.GatheringType.RowId <= 3)
                         .SelectMany(p => p.Item.Select(i => (Type: p.GatheringType.RowId, GatheringItem: i.RowId)))
                         .Where(p => p.GatheringItem != 0)
                         .Select(p => (p.Type, Item: gatheringItems.GetRowOrDefault(p.GatheringItem)?.Item.RowId ?? 0))
                         .Where(p => IsItem(p.Item))
                         .ToList();
        // GatheringType: 0 mining, 1 quarrying, 2 logging, 3 harvesting.
        mining = byType.Where(p => p.Type <= 1).Select(p => p.Item).ToFrozenSet();
        botany = byType.Where(p => p.Type >= 2).Select(p => p.Item).ToFrozenSet();

        Fish = data.GetExcelSheet<FishParameter>().Select(f => f.Item.RowId).Where(IsItem).ToFrozenSet();
        Spearfish = data.GetExcelSheet<SpearfishingItem>().Select(f => f.Item.RowId).Where(IsItem).ToFrozenSet();
        Craftables = data.GetExcelSheet<Recipe>().Select(r => r.ItemResult.RowId).Where(id => id != 0).ToFrozenSet();
        zones = Zones(data, gatheringItems);
    }

    // An item found in several zones goes under the one with its lowest-level node.
    private static FrozenDictionary<(Source, uint), Zone> Zones(IDataManager data, ExcelSheet<GatheringItem> gatheringItems)
    {
        var nodes = data.GetExcelSheet<GatheringPoint>()
                        .Where(p => p.GatheringPointBase.ValueNullable is { GatheringType.RowId: <= 3 })
                        .SelectMany(p =>
                        {
                            var node = p.GatheringPointBase.Value;
                            var source = node.GatheringType.RowId <= 1 ? Source.Mining : Source.Botany;
                            return node.Item.Select(i => (Source: source,
                                                          Item: gatheringItems.GetRowOrDefault(i.RowId)?.Item.RowId ?? 0,
                                                          Level: (int)node.GatheringLevel, p.TerritoryType));
                        });
        var fishing = data.GetExcelSheet<FishingSpot>()
                          .SelectMany(s => s.Item.Select(i => (Source: Source.Fishing, Item: i.RowId,
                                                               Level: (int)s.GatheringLevel, s.TerritoryType)));
        var spearfishing = data.GetExcelSheet<SpearfishingItem>()
                               .Select(s => (Source: Source.Fishing, Item: s.Item.RowId, Level: 0, s.TerritoryType));

        return nodes.Concat(fishing)
                    .Concat(spearfishing)
                    .Where(n => IsItem(n.Item) && n.TerritoryType.ValueNullable != null)
                    .GroupBy(n => (n.Source, n.Item))
                    .ToFrozenDictionary(g => g.Key, g => Zone.Of(g.MinBy(n => (n.Level, n.TerritoryType.RowId)).TerritoryType.Value));
    }

    public Zone? ZoneOf(Source source, uint itemId) => zones.GetValueOrDefault((source, itemId));

    public Source Of(uint itemId)
    {
        var source = Source.None;
        if (botany.Contains(itemId))
            source |= Source.Botany;
        if (mining.Contains(itemId))
            source |= Source.Mining;
        if (Fish.Contains(itemId) || Spearfish.Contains(itemId))
            source |= Source.Fishing;
        if (Craftables.Contains(itemId))
            source |= Source.Crafting;
        return source;
    }

    public static Source Primary(Source sources, IEnumerable<Source> order) => order.FirstOrDefault(s => sources.HasFlag(s));

    public static bool IsGathering(Source source) => source is Source.Botany or Source.Mining or Source.Fishing;

    // The game's job icons are 62000 plus the ClassJob row: Miner 16, Botanist 17, Fisher 18.
    public static uint Icon(Source source) => source switch
    {
        Source.Mining => 62016,
        Source.Botany => 62017,
        Source.Fishing => 62018,
        _ => 0,
    };

    public static string Name(Source source) => source switch
    {
        Source.Botany => "Botanist",
        Source.Mining => "Miner",
        Source.Fishing => "Fisher",
        Source.Vendor => "NPC vendor",
        Source.Crafting => "Crafting",
        _ => "Other",
    };

    private static bool IsItem(uint id) => id is > 0 and < 1000000;
}

internal sealed record Zone(uint Id, string Name, uint Expansion, uint Region)
{
    public static Zone Of(TerritoryType territory) =>
        new(territory.RowId, territory.PlaceName.ValueNullable?.Name.ExtractText() is { Length: > 0 } name ? name : "Unknown area",
            territory.ExVersion.RowId, territory.PlaceNameRegion.RowId);
}
