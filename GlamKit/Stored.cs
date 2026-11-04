using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Cabinet = Lumina.Excel.Sheets.Cabinet;
using Item = Lumina.Excel.Sheets.Item;

namespace GlamKit;

// The game only knows what's in the dresser and armoire once they've been opened this session, so until then
// nothing is removed.
internal sealed unsafe class Stored : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(2);

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IChatGui chat;
    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly Action save;
    private readonly Dictionary<uint, uint> cabinetRows;

    private DateTime nextCheck;

    public Stored(IFramework framework, IClientState clientState, IChatGui chat, IDataManager data,
                  Configuration config, Action save)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.chat = chat;
        this.data = data;
        this.config = config;
        this.save = save;
        cabinetRows = data.GetExcelSheet<Cabinet>()
                          .Where(r => r.Item.RowId != 0)
                          .GroupBy(r => r.Item.RowId)
                          .ToDictionary(g => g.Key, g => g.First().RowId);
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    private void OnUpdate(IFramework _)
    {
        var glam = config.GlamKeeperEnabled && config.Items.Count > 0;
        var wish = config.WishlistEnabled && config.WishItems.Count > 0;
        if (DateTime.UtcNow < nextCheck || !clientState.IsLoggedIn || (!glam && !wish))
            return;
        nextCheck = DateTime.UtcNow + CheckEvery;

        var dresser = InDresser();
        var removed = glam && Remove(config.Items, id => Stash(dresser, id), "no longer kept for glam");
        removed |= wish && Remove(config.WishItems, id => Held(id) ?? Stash(dresser, id), "off your wishlist");
        if (removed)
            save();
    }

    private bool Remove(List<KeptItem> items, Func<uint, string?> placeOf, string outcome)
    {
        var removed = false;
        foreach (var kept in items.ToList())
        {
            if (placeOf(kept.ItemId) is not { } place)
                continue;
            items.Remove(kept);
            removed = true;
            var name = data.GetExcelSheet<Item>().GetRowOrDefault(kept.ItemId)?.Name.ExtractText() ?? "Item";
            chat.Print($"{name} is in your {place}, so it's {outcome}.", "GlamKit");
        }
        return removed;
    }

    private string? Stash(HashSet<uint> dresser, uint itemId) =>
        dresser.Contains(itemId) ? "glamour dresser" : InArmoire(itemId) ? "armoire" : null;

    private static string? Held(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        return inventory != null &&
               inventory->GetInventoryItemCount(itemId) + inventory->GetInventoryItemCount(itemId, true) > 0
            ? "inventory"
            : null;
    }

    private static HashSet<uint> InDresser()
    {
        var mirage = MirageManager.Instance();
        if (mirage == null || !mirage->PrismBoxLoaded)
            return [];
        var ids = new HashSet<uint>();
        // HQ items are 1,000,000 on.
        foreach (var id in mirage->PrismBoxItemIds)
        {
            if (id != 0)
                ids.Add(id % 1_000_000);
        }
        return ids;
    }

    private bool InArmoire(uint itemId)
    {
        var ui = UIState.Instance();
        return ui != null && ui->Cabinet.IsCabinetLoaded() &&
               cabinetRows.TryGetValue(itemId, out var row) && ui->Cabinet.IsItemInCabinet(row);
    }
}
