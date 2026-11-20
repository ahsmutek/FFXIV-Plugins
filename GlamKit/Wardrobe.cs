using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Cabinet = Lumina.Excel.Sheets.Cabinet;
using Item = Lumina.Excel.Sheets.Item;

namespace GlamKit;

// The game only knows what's in the dresser and armoire once they've been opened this session, so each character's
// are remembered.
internal sealed unsafe class Wardrobe : IDisposable
{
    public const string Dresser = "Dresser";
    public const string Armoire = "Armoire";

    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(2);

    public sealed record Upgrade(uint ItemId, int Slot, string[] Places, List<JobLevels.CompareJob> Jobs);

    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly ExcelSheet<Item> items;
    private readonly Configuration config;
    private readonly Action save;
    private readonly JobLevels jobLevels;
    private readonly (uint Row, uint ItemId)[] cabinet;

    private DateTime nextCheck;
    private DateTime nextRefresh;
    private List<Upgrade> upgrades = [];

    public Wardrobe(IFramework framework, IPlayerState player, IDataManager data, Configuration config, Action save,
                    JobLevels jobLevels)
    {
        this.framework = framework;
        this.player = player;
        items = data.GetExcelSheet<Item>();
        this.config = config;
        this.save = save;
        this.jobLevels = jobLevels;
        cabinet = data.GetExcelSheet<Cabinet>()
                      .Where(r => r.Item.RowId != 0)
                      .Select(r => (r.RowId, r.Item.RowId))
                      .ToArray();
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    public bool DresserKnown => Saved(config.Dressers) != null;

    public bool ArmoireKnown => Saved(config.Armoires) != null;

    public uint CurrentJob => player.IsLoaded ? player.ClassJob.RowId : 0;

    public bool Holds(uint itemId) =>
        Saved(config.Dressers)?.Any(id => ItemTooltip.SplitItemId(id).Id == itemId) == true ||
        Saved(config.Armoires)?.Contains(itemId) == true;

    public List<Upgrade> Upgrades()
    {
        if (DateTime.UtcNow < nextRefresh)
            return upgrades;
        nextRefresh = DateTime.UtcNow + CheckEvery;

        // Only the best copy of an item counts, and an HQ copy is at least as good.
        var places = new Dictionary<uint, List<string>>();
        var hq = new HashSet<uint>();
        foreach (var raw in Saved(config.Dressers) ?? [])
        {
            var (id, isHq) = ItemTooltip.SplitItemId(raw);
            AddPlace(places, id, Dresser);
            if (isHq)
                hq.Add(id);
        }
        foreach (var id in Saved(config.Armoires) ?? [])
            AddPlace(places, id, Armoire);

        var pieces = places.Keys.Select(id => (Id: id, Hq: hq.Contains(id))).ToList();
        upgrades = pieces.Zip(jobLevels.Upgrades(pieces))
                         .Where(p => p.Second.Count > 0)
                         .Select(p => new Upgrade(p.First.Id, Slot(p.First.Id), places[p.First.Id].ToArray(), p.Second))
                         .ToList();
        return upgrades;
    }

    private static void AddPlace(Dictionary<uint, List<string>> places, uint itemId, string place)
    {
        if (!places.TryGetValue(itemId, out var found))
            places[itemId] = found = [];
        if (!found.Contains(place))
            found.Add(place);
    }

    private int Slot(uint itemId) =>
        items.TryGetRow(itemId, out var item) ? Gearsets.Slots(item.EquipSlotCategory.Value).FirstOrDefault(-1) : -1;

    private List<uint>? Saved(Dictionary<ulong, List<uint>> byCharacter) =>
        player.IsLoaded && byCharacter.TryGetValue(player.ContentId, out var ids) ? ids : null;

    private void OnUpdate(IFramework _)
    {
        if (DateTime.UtcNow < nextCheck || !player.IsLoaded)
            return;
        nextCheck = DateTime.UtcNow + CheckEvery;

        var mirage = MirageManager.Instance();
        if (mirage != null && mirage->PrismBoxLoaded)
        {
            var ids = new List<uint>();
            foreach (var id in mirage->PrismBoxItemIds)
            {
                if (id != 0)
                    ids.Add(id);
            }
            Remember(config.Dressers, ids);
        }

        var ui = UIState.Instance();
        if (ui != null && ui->Cabinet.IsCabinetLoaded())
        {
            var ids = new List<uint>();
            foreach (var (row, itemId) in cabinet)
            {
                if (ui->Cabinet.IsItemInCabinet(row))
                    ids.Add(itemId);
            }
            Remember(config.Armoires, ids);
        }
    }

    private void Remember(Dictionary<ulong, List<uint>> byCharacter, List<uint> ids)
    {
        ids.Sort();
        if (Saved(byCharacter) is { } saved && saved.SequenceEqual(ids))
            return;
        byCharacter[player.ContentId] = ids;
        save();
        nextRefresh = DateTime.MinValue;
    }
}
