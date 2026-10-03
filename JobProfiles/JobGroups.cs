using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace JobProfiles;

public enum JobGroup
{
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    Caster,
    Limited,
    Hand,
    Land,
}

// The role groups and their jobs (the same groups as Flick Wheel's job wheels), and
// lookups between the game's ClassJob rows and the English abbreviations settings use.
internal sealed class JobGroups(IDataManager data)
{
    // Gold job glyphs on a transparent background are 62000 + ClassJob row id.
    public const uint JobIconBase = 62000;

    public static readonly (JobGroup Group, string Label, string[] Jobs)[] All =
    [
        (JobGroup.Tank, "Tanks", ["PLD", "WAR", "DRK", "GNB"]),
        (JobGroup.Healer, "Healers", ["WHM", "SCH", "AST", "SGE"]),
        (JobGroup.Melee, "Melee DPS", ["MNK", "DRG", "NIN", "SAM", "RPR", "VPR"]),
        (JobGroup.PhysicalRanged, "Physical Ranged DPS", ["BRD", "MCH", "DNC"]),
        (JobGroup.Caster, "Magical Ranged DPS", ["BLM", "SMN", "RDM", "PCT"]),
        (JobGroup.Limited, "Limited Jobs", ["BLU", "BST"]),
        (JobGroup.Hand, "Disciples of the Hand", ["CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL"]),
        (JobGroup.Land, "Disciples of the Land", ["MIN", "BTN", "FSH"]),
    ];

    private Dictionary<string, ClassJob>? byAbbreviation;
    private Dictionary<uint, (string Abbreviation, JobGroup Group)>? byRow;

    public ClassJob? Job(string abbreviation) =>
        Lookup().TryGetValue(abbreviation, out var job) ? job : null;

    // The job a ClassJob row counts as, with its group. A base class counts as its job
    // (Gladiator → PLD); rows on no wheel (Adventurer) are null.
    public (string Abbreviation, JobGroup Group)? Of(uint classJobId)
    {
        if (byRow == null)
        {
            byRow = new Dictionary<uint, (string, JobGroup)>();
            foreach (var (group, _, jobs) in All)
            {
                foreach (var abbreviation in jobs)
                {
                    if (Job(abbreviation) is not { } job)
                        continue;
                    byRow[job.RowId] = (abbreviation, group);
                    if (HasParent(job))
                        byRow.TryAdd(job.ClassJobParent.RowId, (abbreviation, group));
                }
            }
        }

        return byRow.TryGetValue(classJobId, out var found) ? found : null;
    }

    public static string DisplayName(ClassJob job) =>
        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(job.Name.ExtractText());

    private static bool HasParent(ClassJob job) =>
        job.ClassJobParent.IsValid && job.ClassJobParent.RowId != 0 && job.ClassJobParent.RowId != job.RowId;

    // Abbreviations are matched against the English sheet so they're the same in every client language.
    private Dictionary<string, ClassJob> Lookup()
    {
        if (byAbbreviation != null)
            return byAbbreviation;

        var english = data.GetExcelSheet<ClassJob>(ClientLanguage.English);
        var localized = data.GetExcelSheet<ClassJob>();
        byAbbreviation = new Dictionary<string, ClassJob>();
        foreach (var row in english)
        {
            var abbreviation = row.Abbreviation.ExtractText();
            if (abbreviation.Length > 0 && localized.TryGetRow(row.RowId, out var local))
                byAbbreviation.TryAdd(abbreviation, local);
        }

        return byAbbreviation;
    }
}
