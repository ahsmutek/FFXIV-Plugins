using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace FlickWheel;

// RowId is the base class while the job isn't unlocked yet.
internal sealed record WheelJob(
    int Slot,
    string Abbreviation,
    JobGroup Group,
    uint RowId,
    string Name,
    uint IconId,
    int GearsetIndex,
    bool IsCurrent,
    uint CommandId = 0);

internal readonly record struct GearsetInfo(int Index, string Name, uint ClassJobId);

internal sealed class JobCatalog(IDataManager data, IUnlockState unlocks, IPlayerState player, Configuration config)
{
    private const uint JobIconBase = 62000;

    private Dictionary<string, ClassJob>? byAbbreviation;

    public List<WheelJob> Build(WheelRing ring)
    {
        var jobs = new List<WheelJob>();
        if (!player.IsLoaded)
            return jobs;

        var gearsets = Gearsets();
        var current = player.ClassJob.RowId;

        for (var slot = 0; slot < ring.Slots.Length; slot++)
        {
            var abbreviation = ring.Slots[slot].Key;
            var group = (JobGroup)ring.Slots[slot].Group;
            if (JobLayout.Commands.TryGetValue(abbreviation, out var commandId))
            {
                if (CommandEntry(slot, abbreviation, group, commandId) is { } entry)
                    jobs.Add(entry);
                continue;
            }

            if (!Lookup().TryGetValue(abbreviation, out var job))
                continue;

            // A class and its job share one level, so a level means one of them is unlocked.
            if (player.GetClassJobLevel(job) <= 0)
                continue;

            var shown = job;
            if (HasParent(job) && !unlocks.IsClassJobUnlocked(job))
                shown = job.ClassJobParent.Value;

            jobs.Add(new WheelJob(
                slot,
                abbreviation,
                group,
                shown.RowId,
                DisplayName(shown),
                JobIconBase + shown.RowId,
                ResolveGearset(abbreviation, shown, job, gearsets),
                shown.RowId == current));
        }

        return jobs;
    }

    public static unsafe List<GearsetInfo> Gearsets()
    {
        var list = new List<GearsetInfo>();
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return list;

        for (var i = 0; i < module->Entries.Length; i++)
        {
            if (!module->IsValidGearset(i))
                continue;

            ref var entry = ref module->Entries[i];
            list.Add(new GearsetInfo(i, entry.NameString, entry.ClassJob));
        }

        return list;
    }

    public (uint IconId, string Name)? Info(string abbreviation)
    {
        if (JobLayout.Commands.TryGetValue(abbreviation, out var commandId))
            return data.GetExcelSheet<MainCommand>().TryGetRow(commandId, out var command)
                ? ((uint)command.Icon, command.Name.ExtractText())
                : null;

        return Lookup().TryGetValue(abbreviation, out var job) ? (JobIconBase + job.RowId, DisplayName(job)) : null;
    }

    private unsafe WheelJob? CommandEntry(int slot, string abbreviation, JobGroup group, uint commandId)
    {
        var ui = UIModule.Instance();
        if (ui == null || !ui->IsMainCommandUnlocked(commandId) || Info(abbreviation) is not { } info)
            return null;

        return new WheelJob(slot, abbreviation, group, 0, info.Name, info.IconId, -1, false, commandId);
    }

    public static string DisplayName(ClassJob job) =>
        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(job.Name.ExtractText());

    public string Abbreviation(uint classJobId) =>
        data.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var row) ? row.Abbreviation.ExtractText() : string.Empty;

    private static bool HasParent(ClassJob job) =>
        job.ClassJobParent.IsValid && job.ClassJobParent.RowId != 0 && job.ClassJobParent.RowId != job.RowId;

    private int ResolveGearset(string abbreviation, ClassJob shown, ClassJob job, List<GearsetInfo> gearsets)
    {
        if (config.GearsetOverrides.TryGetValue(abbreviation, out var number) && number > 0)
        {
            foreach (var set in gearsets)
                if (set.Index == number - 1)
                    return set.Index;
        }

        foreach (var set in gearsets)
            if (set.ClassJobId == shown.RowId)
                return set.Index;

        var parent = job.ClassJobParent.RowId;
        if (parent != 0 && parent != shown.RowId)
        {
            foreach (var set in gearsets)
                if (set.ClassJobId == parent)
                    return set.Index;
        }

        return -1;
    }

    // English names, so they match in every client language.
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
