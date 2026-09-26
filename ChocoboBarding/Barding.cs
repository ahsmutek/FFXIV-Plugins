using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace ChocoboBarding;

public enum SlotAction
{
    Keep,
    Remove,
    Set,
}

/// <summary>What to do with one barding slot: leave it, take the piece off, or put a BuddyEquip row on.</summary>
public readonly record struct SlotChoice(SlotAction Action, uint RowId = 0)
{
    public static readonly SlotChoice Keep = new(SlotAction.Keep);
    public static readonly SlotChoice Remove = new(SlotAction.Remove);
    public static SlotChoice Set(uint rowId) => new(SlotAction.Set, rowId);
}

internal static class Barding
{
    // Buddy agent event sent by Companion → Appearance when a piece is clicked: [20, slot, BuddyEquip row]
    private const int SelectBardingEvent = 20;

    public const int SlotCount = 3;
    public static readonly string[] SlotNames = ["Head", "Body", "Legs"];

    public const string KeepToken = "-";
    public const string RemoveToken = "none";

    private const string BardingSuffix = " Barding";

    /// <summary>"Postmoogle Barding" → "Postmoogle"; names without the suffix are unchanged.</summary>
    public static string ShortName(string name)
    {
        return name.EndsWith(BardingSuffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^BardingSuffix.Length]
            : name;
    }

    public static BuddyEquip? Find(IDataManager data, string name)
    {
        var wanted = name.Trim().Trim('"');
        if (wanted.Length == 0)
            return null;

        var rows = data.GetExcelSheet<BuddyEquip>().Where(row => row.Name.ExtractText().Length > 0).ToList();

        // An exact full name wins over a short name, in case both could match.
        foreach (var row in rows)
            if (row.Name.ExtractText().Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return row;

        foreach (var row in rows)
            if (ShortName(row.Name.ExtractText()).Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return row;

        return null;
    }

    /// <summary>Owned barding names containing <paramref name="name"/>; unowned barding is never suggested.</summary>
    public static string[] Suggest(IDataManager data, IUnlockState unlocks, string name, int max = 5)
    {
        var wanted = name.Trim().Trim('"');
        return Owned(data, unlocks)
               .Select(row => row.Name.ExtractText())
               .Where(n => n.Contains(wanted, StringComparison.OrdinalIgnoreCase))
               .Select(ShortName)
               .Distinct()
               .Take(max)
               .ToArray();
    }

    public static IEnumerable<BuddyEquip> Owned(IDataManager data, IUnlockState unlocks)
    {
        return data.GetExcelSheet<BuddyEquip>()
                   .Where(row => row.Name.ExtractText().Length > 0 && unlocks.IsBuddyEquipUnlocked(row));
    }

    /// <summary>
    /// Splits command arguments into slot tokens. Without quotes the whole input is one name,
    /// so <c>/cbarding Sophic Barding</c> still works; with quotes, quoted strings and bare words
    /// (<c>-</c>, <c>none</c>) are separate tokens.
    /// </summary>
    public static List<string> Tokenize(string args)
    {
        var trimmed = args.Trim();
        if (!trimmed.Contains('"'))
            return trimmed.Length > 0 ? [trimmed] : [];

        var tokens = new List<string>();
        var i = 0;
        while (i < trimmed.Length)
        {
            if (char.IsWhiteSpace(trimmed[i]))
            {
                i++;
                continue;
            }

            var sb = new StringBuilder();
            if (trimmed[i] == '"')
            {
                i++;
                while (i < trimmed.Length && trimmed[i] != '"')
                    sb.Append(trimmed[i++]);
                i++; // closing quote (or past the end if unclosed)
            }
            else
            {
                while (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i]))
                    sb.Append(trimmed[i++]);
            }

            tokens.Add(sb.ToString());
        }

        return tokens;
    }

    /// <summary>The token a slot choice is written as in a macro.</summary>
    public static string ToToken(SlotChoice choice, string? name) => choice.Action switch
    {
        SlotAction.Keep => KeepToken,
        SlotAction.Remove => RemoveToken,
        _ => $"\"{name}\"",
    };

    /// <summary>
    /// Jobs a barding can be picked for: every job, crafter and gatherer, in the game's own order.
    /// Base classes (Arcanist, Gladiator…) are left out; they use their job's barding.
    /// </summary>
    public static List<ClassJob> Jobs(IDataManager data)
    {
        var sheet = data.GetExcelSheet<ClassJob>();
        var baseClasses = sheet.Where(HasParent).Select(job => job.ClassJobParent.RowId).ToHashSet();
        return sheet.Where(job => job.RowId != 0
                                  && job.Name.ExtractText().Length > 0
                                  && job.Abbreviation.ExtractText().Length > 0
                                  && !baseClasses.Contains(job.RowId))
                    .OrderBy(job => job.UIPriority)
                    .ToList();
    }

    /// <summary>The barding saved for a job; a base class falls back to a job built on it (Arcanist → Summoner or Scholar).</summary>
    public static SlotChoice[]? ForJob(Configuration config, IDataManager data, uint classJobId)
    {
        if (Saved(config, classJobId) is { } choices)
            return choices;

        foreach (var job in data.GetExcelSheet<ClassJob>().Where(job => HasParent(job) && job.ClassJobParent.RowId == classJobId).OrderBy(job => job.UIPriority))
            if (Saved(config, job.RowId) is { } fallback)
                return fallback;

        return null;
    }

    private static SlotChoice[]? Saved(Configuration config, uint classJobId) =>
        config.JobBarding.TryGetValue(classJobId, out var choices) && choices.Length == SlotCount ? choices : null;

    private static bool HasParent(ClassJob job) =>
        job.ClassJobParent.IsValid && job.ClassJobParent.RowId != 0 && job.ClassJobParent.RowId != job.RowId;

    public static string JobName(ClassJob job) =>
        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(job.Name.ExtractText());

    public static unsafe uint WornRowId(int slot)
    {
        return UIState.Instance()->Buddy.CompanionInfo.BuddyEquipRowIds[slot];
    }

    /// <summary>
    /// Sends the same Buddy agent event the Appearance tab sends for one click.
    /// Selecting the piece a slot already wears takes it off.
    /// </summary>
    public static unsafe bool SendOne(int slot, uint rowId)
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Buddy);
        if (agent == null)
            return false;

        var values = stackalloc AtkValue[3];
        var result = stackalloc AtkValue[1];
        values[0].SetInt(SelectBardingEvent);
        values[1].SetInt(slot);
        values[2].SetInt((int)rowId);
        agent->ReceiveEvent(result, values, 3, 0);
        return true;
    }
}
