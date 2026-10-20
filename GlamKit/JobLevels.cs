using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace GlamKit;

// Turns an item's class/job line into each eligible job's level, coloured against the item's equip level.
internal sealed unsafe class JobLevels
{
    private const int MostListed = 10;
    private const int MostClosest = 5;

    private const uint Green = 0x8CE36B;
    private const uint Yellow = 0xF2D75A;
    private const uint Grey = 0x9A9A9A;

    private readonly record struct Job(string Abbreviation, int ExpIndex);

    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly Dictionary<uint, Job[]> categories = [];
    private readonly ushort green;
    private readonly ushort yellow;
    private readonly ushort grey;

    public JobLevels(IDataManager data, Configuration config)
    {
        this.data = data;
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

        foreach (var category in data.GetExcelSheet<ClassJobCategory>())
        {
            // A class and its job share a level, so only the job (the later row) is listed.
            var allowed = jobs
                .Where(j => (bool)j.Column!.GetValue(category)!)
                .GroupBy(j => j.Row.ExpArrayIndex)
                .Select(g => g.MaxBy(j => j.Row.RowId).Row)
                .OrderBy(j => j.UIPriority)
                .Select(j => new Job(j.Abbreviation.ExtractText(), j.ExpArrayIndex))
                .ToArray();
            if (allowed.Length > 0)
                categories[category.RowId] = allowed;
        }
    }

    public SeString? Line(uint itemId, SeString original)
    {
        var player = PlayerState.Instance();
        if (player == null || !data.GetExcelSheet<Item>().TryGetRow(itemId, out var item) ||
            item.EquipSlotCategory.RowId == 0 || !categories.TryGetValue(item.ClassJobCategory.RowId, out var jobs))
            return null;

        var levels = jobs.Select(j => (j.Abbreviation, Level: (int)player->ClassJobLevels[j.ExpIndex])).ToList();
        var builder = new SeStringBuilder();

        // Wide categories list only the green jobs, or else the one nearest its level.
        if (levels.Count > MostListed)
        {
            // Every started job can wear it, so the levels would say nothing.
            if (item.LevelEquip <= 1)
                return null;
            var wearable = levels.Where(l => IsGreen(l.Level, item.LevelEquip))
                               .OrderBy(l => l.Level).Take(MostClosest).ToList();
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
            var (abbreviation, level) = levels[i];
            builder.AddUiForeground($"{abbreviation} {(level == 0 ? "–" : level)}", Colour(level, item.LevelEquip));
        }
        return builder.Build();
    }

    private bool IsGreen(int level, int required) => level >= required && level <= required + config.GreenAbove;

    private ushort Colour(int level, int required) =>
        IsGreen(level, required) ? green
        : level > 0 && level < required && level >= required - config.YellowBelow ? yellow
        : grey;
}
