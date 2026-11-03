using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace GlamKit;

internal static unsafe class Gearsets
{
    // Gear sets hold the item ID with HQ's 1,000,000 added.
    private const uint ItemIdModifier = 500_000;

    public static string? Line(IDataManager data, uint itemId)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null || itemId == 0)
            return null;

        var jobs = new List<uint>();
        for (var i = 0; i < module->Entries.Length; i++)
        {
            if (!module->IsValidGearset(i))
                continue;
            ref var entry = ref module->Entries[i];
            foreach (ref var item in entry.Items)
            {
                if (item.ItemId % ItemIdModifier == itemId)
                {
                    jobs.Add(entry.ClassJob);
                    break;
                }
            }
        }
        if (jobs.Count == 0)
            return null;

        var sheet = data.GetExcelSheet<ClassJob>();
        var names = jobs.Distinct()
            .Select(j => sheet.TryGetRow(j, out var row) ? row.Abbreviation.ExtractText() : null)
            .OfType<string>();
        return $"Equipped - {string.Join(", ", names)}";
    }
}
