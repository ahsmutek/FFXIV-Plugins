using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace GlamKit;

internal sealed unsafe class JobLevels
{
    private const int MostListed = 10;
    private const int MostClosest = 5;
    private const string UpgradeMark = "▲";
    private const uint HqBonus = 1;

    private const uint Green = 0x8CE36B;
    private const uint Yellow = 0xF2D75A;
    private const uint Grey = 0x9A9A9A;

    private readonly record struct Job(string Abbreviation, int ExpIndex, uint MainStat, uint Id, uint ClassId);

    private readonly IDataManager data;
    private readonly ExcelSheet<Item> items;
    private readonly Configuration config;
    private readonly Dictionary<uint, Job[]> categories = [];
    private readonly Dictionary<uint, PropertyInfo> columns = [];
    private readonly ushort green;
    private readonly ushort yellow;
    private readonly ushort grey;

    public JobLevels(IDataManager data, Configuration config)
    {
        this.data = data;
        items = data.GetExcelSheet<Item>();
        this.config = config;
        green = ItemTooltip.Nearest(data, Green);
        yellow = ItemTooltip.Nearest(data, Yellow);
        grey = ItemTooltip.Nearest(data, Grey);

        // Lumina names ClassJobCategory's columns after each job's abbreviation.
        var jobs = data.GetExcelSheet<ClassJob>()
            .Where(j => j.RowId != 0 && j.ExpArrayIndex >= 0 && !j.Abbreviation.IsEmpty)
            .Select(j => (Row: j, Column: typeof(ClassJobCategory).GetProperty(j.Abbreviation.ExtractText())))
            .Where(j => j.Column?.PropertyType == typeof(bool))
            .ToList();
        foreach (var job in jobs)
            columns[job.Row.RowId] = job.Column!;

        foreach (var category in data.GetExcelSheet<ClassJobCategory>())
        {
            var wearers = jobs.Where(j => (bool)j.Column!.GetValue(category)!).Select(j => j.Row).ToList();
            // A class is left out when its job is listed; SMN and SCH share a level but differ in gear and main stat.
            var allowed = wearers
                .Where(j => !wearers.Any(w => w.RowId != j.RowId && w.ClassJobParent.RowId == j.RowId))
                .OrderBy(j => j.UIPriority)
                .Select(j => new Job(j.Abbreviation.ExtractText(), j.ExpArrayIndex, j.PrimaryStat, j.RowId,
                                     j.ClassJobParent.RowId))
                .ToArray();
            if (allowed.Length > 0)
                categories[category.RowId] = allowed;
        }
    }

    // Whether the current job can wear the item, or null when it isn't gear.
    public bool? CurrentJobCanWear(uint itemId)
    {
        var player = PlayerState.Instance();
        if (player == null || !items.TryGetRow(itemId, out var item) ||
            item.EquipSlotCategory.RowId == 0 || !item.ClassJobCategory.IsValid)
            return null;
        return columns.TryGetValue(player->CurrentClassJobId, out var column) &&
               (bool)column.GetValue(item.ClassJobCategory.Value)!;
    }

    public SeString? Line(uint itemId, bool hq, SeString original)
    {
        var player = PlayerState.Instance();
        if (player == null || !items.TryGetRow(itemId, out var item) ||
            item.EquipSlotCategory.RowId == 0 || !categories.TryGetValue(item.ClassJobCategory.RowId, out var jobs))
            return null;

        // Every started job can wear it, so the levels would say nothing.
        var wide = jobs.Length > MostListed;
        if (wide && item.LevelEquip <= 1)
            return null;

        var slots = config.MarkUpgrades ? Gearsets.Slots(item.EquipSlotCategory.Value) : [];
        var held = slots.Any(config.HiddenUpgradeSlots.Contains) ? [] : Gearsets.Held(slots);
        var levels = jobs.Select(j =>
        {
            var level = (int)player->ClassJobLevels[j.ExpIndex];
            return (j.Abbreviation, Level: level, Upgrade: level >= item.LevelEquip && IsUpgrade(item, hq, j, held));
        }).ToList();
        var builder = new SeStringBuilder();

        // Wide categories list only the jobs it's an upgrade for and the green jobs, or else the one nearest its level.
        if (wide)
        {
            var wearable = levels.Where(l => l.Upgrade || IsGreen(l.Level, item.LevelEquip))
                               .OrderByDescending(l => l.Upgrade).ThenBy(l => l.Level).Take(MostClosest).ToList();
            if (wearable.Count == 0)
            {
                var started = levels.Where(l => l.Level > 0).ToList();
                if (started.Count == 0)
                    return null;
                wearable = [started.MinBy(l => Math.Abs(l.Level - item.LevelEquip))];
            }
            builder.Append(original).AddText(" · ");
            levels = wearable;
        }

        for (var i = 0; i < levels.Count; i++)
        {
            if (i > 0)
                builder.AddText("  ");
            var (abbreviation, level, upgrade) = levels[i];
            builder.AddUiForeground($"{abbreviation} {(level == 0 ? "–" : level)}{(upgrade ? UpgradeMark : "")}",
                                    Colour(level, item.LevelEquip));
        }
        return builder.Build();
    }

    // Main stat, then item level, as Recommended Gear appears to judge.
    private bool IsUpgrade(Item item, bool hq, Job job, List<(uint ClassJob, Gearsets.Piece[] Pieces)> held)
    {
        var sets = held.Where(h => h.ClassJob == job.Id || h.ClassJob == job.ClassId).ToList();
        if (sets.Count == 0)
            return false;
        var best = sets.Max(s => SetScore(s.Pieces, item.RowId, job.MainStat));
        return Score(item, hq, job.MainStat).CompareTo(best) > 0;
    }

    // An item already in the set only replaces its own copy: a second may be unique, and the hovered one may be worn.
    private (int MainStat, uint ItemLevel) SetScore(Gearsets.Piece[] pieces, uint itemId, uint mainStat)
    {
        var copies = pieces.Where(p => p.ItemId == itemId).ToList();
        return copies.Count > 0 ? copies.Max(p => Score(p, mainStat)) : pieces.Min(p => Score(p, mainStat));
    }

    private (int MainStat, uint ItemLevel) Score(Gearsets.Piece piece, uint mainStat) =>
        items.TryGetRow(piece.ItemId, out var item) ? Score(item, piece.Hq, mainStat) : default;

    // Crafters and gatherers have no main stat, so only item level counts for them.
    private static (int MainStat, uint ItemLevel) Score(Item item, bool hq, uint mainStat)
    {
        var stat = 0;
        if (mainStat != 0)
        {
            for (var i = 0; i < item.BaseParam.Count; i++)
            {
                if (item.BaseParam[i].RowId == mainStat)
                    stat += item.BaseParamValue[i];
            }
            // Other special bonuses are set or area effects.
            if (hq && item.ItemSpecialBonus.RowId == HqBonus)
            {
                for (var i = 0; i < item.BaseParamSpecial.Count; i++)
                {
                    if (item.BaseParamSpecial[i].RowId == mainStat)
                        stat += item.BaseParamValueSpecial[i];
                }
            }
        }
        return (stat, item.LevelItem.RowId);
    }

    private bool IsGreen(int level, int required) => level >= required && level <= required + config.GreenAbove;

    private ushort Colour(int level, int required) =>
        IsGreen(level, required) ? green
        : level > 0 && level < required && level >= required - config.YellowBelow ? yellow
        : grey;
}
