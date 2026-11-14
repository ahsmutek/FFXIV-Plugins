using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace GlamKit;

internal static unsafe class Gearsets
{
    private static readonly int[] Rings =
        [(int)RaptureGearsetModule.GearsetItemIndex.RingRight, (int)RaptureGearsetModule.GearsetItemIndex.RingLeft];

    public readonly record struct Piece(uint ItemId, bool Hq);

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
                if (ItemTooltip.SplitItemId(item.ItemId).Id == itemId)
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

    // Gear set slot indices match the equipped container's.
    public static int[] Slots(EquipSlotCategory category)
    {
        if (category.FingerL == 1 || category.FingerR == 1)
            return Rings;
        sbyte[] inGearsetOrder =
        [
            category.MainHand, category.OffHand, category.Head, category.Body, category.Gloves, category.Waist,
            category.Legs, category.Feet, category.Ears, category.Neck, category.Wrists,
        ];
        var slot = Array.IndexOf(inGearsetOrder, (sbyte)1);
        return slot < 0 ? [] : [slot];
    }

    public static List<(uint ClassJob, Piece[] Pieces)> Held(int[] slots)
    {
        var held = new List<(uint ClassJob, Piece[] Pieces)>();
        if (slots.Length == 0)
            return held;

        var module = RaptureGearsetModule.Instance();
        if (module != null)
        {
            for (var i = 0; i < module->Entries.Length; i++)
            {
                if (!module->IsValidGearset(i))
                    continue;
                held.Add((module->Entries[i].ClassJob, slots.Select(s =>
                {
                    ref var item = ref module->Entries[i].Items[s];
                    if (item.Flags.HasFlag(RaptureGearsetModule.GearsetItemFlag.ItemMissing))
                        return default;
                    var (itemId, hq) = ItemTooltip.SplitItemId(item.ItemId);
                    return new Piece(itemId, hq);
                }).ToArray()));
            }
        }

        var player = PlayerState.Instance();
        var inventory = InventoryManager.Instance();
        var equipped = inventory == null ? null : inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (player != null && equipped != null && equipped->IsLoaded)
        {
            held.Add((player->CurrentClassJobId, slots.Select(s =>
            {
                var item = equipped->GetInventorySlot(s);
                return item == null ? default : new Piece(item->ItemId, item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality));
            }).ToArray()));
        }
        return held;
    }
}
