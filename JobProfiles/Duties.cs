using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace JobProfiles;

// Whether you're in a duty and what kind, from the zone's Duty Finder entry. The kinds are
// the game's own ContentType rows (Dungeons, Trials, Raids, Ultimate Raids, Deep Dungeons…).
internal sealed class Duties(IClientState clientState, IDataManager data)
{
    private uint cachedTerritory = uint.MaxValue;
    private (bool InDuty, uint ContentType) cached;
    private List<(uint Id, string Name)>? kinds;

    /// <summary>In a duty, and its ContentType row (0 outside duties).</summary>
    public (bool InDuty, uint ContentType) Current()
    {
        var territory = clientState.TerritoryType;
        if (territory == cachedTerritory)
            return cached;

        cachedTerritory = territory;
        cached = data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var row) &&
                 row.ContentFinderCondition.RowId != 0 && row.ContentFinderCondition.IsValid
            ? (true, row.ContentFinderCondition.Value.ContentType.RowId)
            : (false, 0u);
        return cached;
    }

    /// <summary>Every kind of duty the Duty Finder has, by name.</summary>
    public List<(uint Id, string Name)> Kinds()
    {
        if (kinds != null)
            return kinds;

        var used = data.GetExcelSheet<ContentFinderCondition>()
                       .Select(c => c.ContentType.RowId)
                       .Where(id => id != 0)
                       .ToHashSet();
        kinds = data.GetExcelSheet<ContentType>()
                    .Where(t => used.Contains(t.RowId))
                    .Select(t => (t.RowId, t.Name.ExtractText()))
                    .Where(t => t.Item2.Length > 0)
                    .OrderBy(t => t.Item2)
                    .ToList();
        return kinds;
    }

    public string KindName(uint id) =>
        Kinds().FirstOrDefault(k => k.Id == id).Name ?? $"Duty type {id}";
}
