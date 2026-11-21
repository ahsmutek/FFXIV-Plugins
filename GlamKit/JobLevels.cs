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
    private static readonly TimeSpan OutgrownFor = TimeSpan.FromSeconds(2);

    private const uint Green = 0x8CE36B;
    private const uint Yellow = 0xF2D75A;
    private const uint Grey = 0x9A9A9A;

    private readonly record struct Job(string Abbreviation, int ExpIndex, uint MainStat, uint Id, uint ClassId);

    public sealed record CompareJob(string Abbreviation, uint Id, uint ClassId, int Level, bool Upgrade, Gearsets.Piece[] Pieces);

    private readonly IDataManager data;
    private readonly ExcelSheet<Item> items;
    private readonly Configuration config;
    private readonly Dictionary<uint, Job[]> categories = [];
    private readonly Dictionary<uint, PropertyInfo> columns = [];
    private readonly ushort green;
    private readonly ushort yellow;
    private readonly ushort grey;
    private readonly Dictionary<uint, bool> outgrown = [];
    private readonly Dictionary<int, List<(uint ClassJob, Gearsets.Piece[] Pieces)>> outgrownHeld = [];
    private DateTime outgrownUntil;

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

    // Whether the job's category includes the item and the level it needs, or null when race, sex or Grand Company also decide.
    public (bool ForJob, int Level)? Requirement(uint itemId, uint jobId)
    {
        if (!items.TryGetRow(itemId, out var item) || item.EquipRestriction.RowId > 1 || item.GrandCompany.RowId != 0 ||
            !item.ClassJobCategory.IsValid || !columns.TryGetValue(jobId, out var column))
            return null;
        return ((bool)column.GetValue(item.ClassJobCategory.Value)!, item.LevelEquip);
    }

    public SeString? Line(uint itemId, bool hq, SeString original)
    {
        var player = PlayerState.Instance();
        if (player == null || Wearers(itemId, out var item) is not { } jobs)
            return null;

        // Every started job can wear it, so the levels would say nothing.
        var wide = jobs.Length > MostListed;
        if (wide && item.LevelEquip <= 1)
            return null;

        var slots = Gearsets.Slots(item.EquipSlotCategory.Value);
        var held = MarksUpgrades(slots) ? Gearsets.Held(slots) : [];
        var levels = jobs.Select(j =>
        {
            var level = (int)player->ClassJobLevels[j.ExpIndex];
            return (j.Abbreviation, Level: level, Upgrade: IsUpgrade(item, hq, j, level, BestSet(item, j, held)));
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

    // Jobs that can wear the item and have a gear set, each with its best set's pieces for the item's slots.
    public List<CompareJob> CompareJobs(uint itemId, bool hq)
    {
        if (Wearers(itemId, out var item) is not { } jobs)
            return [];
        var slots = Gearsets.Slots(item.EquipSlotCategory.Value);
        return Compare(item, hq, jobs, Gearsets.Held(slots), MarksUpgrades(slots));
    }

    // The jobs each piece is an upgrade for, reading the gear sets once per slot rather than once per piece.
    public List<List<CompareJob>> Upgrades(IEnumerable<(uint Id, bool Hq)> pieces)
    {
        var heldBySlot = new Dictionary<int, List<(uint ClassJob, Gearsets.Piece[] Pieces)>>();
        return pieces.Select(p => UpgradesFor(p.Id, p.Hq, heldBySlot)).ToList();
    }

    private List<CompareJob> UpgradesFor(uint itemId, bool hq,
                                         Dictionary<int, List<(uint ClassJob, Gearsets.Piece[] Pieces)>> heldBySlot)
    {
        if (Wearers(itemId, out var item) is not { } jobs)
            return [];
        var slots = Gearsets.Slots(item.EquipSlotCategory.Value);
        if (!MarksUpgrades(slots))
            return [];
        if (!heldBySlot.TryGetValue(slots[0], out var held))
            heldBySlot[slots[0]] = held = Gearsets.Held(slots);
        return Compare(item, hq, jobs, held, true).Where(j => j.Upgrade).ToList();
    }

    // Asked for every bag slot each frame, so answers are kept a moment.
    public bool IsOutgrown(uint itemId)
    {
        if (DateTime.UtcNow >= outgrownUntil)
        {
            outgrown.Clear();
            outgrownHeld.Clear();
            outgrownUntil = DateTime.UtcNow + OutgrownFor;
        }
        if (!outgrown.TryGetValue(itemId, out var result))
            outgrown[itemId] = result = Outgrown(itemId);
        return result;
    }

    // No gear set has it, and every started job that can wear it has as good in its gear sets, even before reaching the
    // item's level, or has no gear set and is past the item's green levels. Judged as HQ, so a useful copy is never marked.
    private bool Outgrown(uint itemId)
    {
        var player = PlayerState.Instance();
        if (player == null || Wearers(itemId, out var item) is not { } jobs || item.LevelEquip <= 1)
            return false;
        var slots = Gearsets.Slots(item.EquipSlotCategory.Value);
        if (slots.Length == 0)
            return false;
        if (!outgrownHeld.TryGetValue(slots[0], out var held))
            outgrownHeld[slots[0]] = held = Gearsets.Held(slots);
        if (held.Any(h => h.Pieces.Any(p => p.ItemId == itemId)))
            return false;

        var started = false;
        foreach (var job in jobs)
        {
            var level = (int)player->ClassJobLevels[job.ExpIndex];
            if (level <= 0)
                continue;
            started = true;
            if (config.IsNotUpgrade(itemId, job.Id))
                continue;
            if (BestSet(item, job, held) is not { } best)
            {
                if (level <= item.LevelEquip + config.GreenAbove)
                    return false;
            }
            else if (Score(item, true, job.MainStat).CompareTo(SetScore(best, itemId, job.MainStat)) > 0)
            {
                return false;
            }
        }
        return started;
    }

    private List<CompareJob> Compare(Item item, bool hq, Job[] jobs, List<(uint ClassJob, Gearsets.Piece[] Pieces)> held,
                                     bool marks)
    {
        var player = PlayerState.Instance();
        if (player == null)
            return [];

        var compared = new List<CompareJob>();
        foreach (var job in jobs)
        {
            if (BestSet(item, job, held) is not { } best)
                continue;
            var level = (int)player->ClassJobLevels[job.ExpIndex];
            compared.Add(new CompareJob(job.Abbreviation, job.Id, job.ClassId, level,
                                        marks && IsUpgrade(item, hq, job, level, best), best));
        }
        return compared;
    }

    private Job[]? Wearers(uint itemId, out Item item) =>
        items.TryGetRow(itemId, out item) && item.EquipSlotCategory.RowId != 0 &&
        categories.TryGetValue(item.ClassJobCategory.RowId, out var jobs) ? jobs : null;

    private bool MarksUpgrades(int[] slots) =>
        config.MarkUpgrades && slots.Length > 0 && !slots.Any(config.HiddenUpgradeSlots.Contains);

    private Gearsets.Piece[]? BestSet(Item item, Job job, List<(uint ClassJob, Gearsets.Piece[] Pieces)> held) =>
        held.Where(h => h.ClassJob == job.Id || h.ClassJob == job.ClassId)
            .Select(h => h.Pieces)
            .MaxBy(p => SetScore(p, item.RowId, job.MainStat));

    // Main stat, then item level, as Recommended Gear appears to judge.
    private bool IsUpgrade(Item item, bool hq, Job job, int level, Gearsets.Piece[]? best) =>
        best != null && level >= item.LevelEquip &&
        Score(item, hq, job.MainStat).CompareTo(SetScore(best, item.RowId, job.MainStat)) > 0;

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
