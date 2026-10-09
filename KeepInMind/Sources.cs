using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace KeepInMind;

// Declared in group priority order, so the lowest set flag is an item's primary source.
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
    }

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

    public static Source Primary(Source sources) => Order.FirstOrDefault(s => sources.HasFlag(s));

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
