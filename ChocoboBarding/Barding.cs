using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace ChocoboBarding;

internal static class Barding
{
    // Buddy agent event sent by Companion → Appearance when a piece is clicked: [20, slot, BuddyEquip row]
    private const int SelectBardingEvent = 20;
    private const int SlotCount = 3; // 0 = Head, 1 = Body, 2 = Legs

    public static BuddyEquip? Find(IDataManager data, string name)
    {
        var wanted = name.Trim().Trim('"');
        if (wanted.Length == 0)
            return null;

        foreach (var row in data.GetExcelSheet<BuddyEquip>())
        {
            var rowName = row.Name.ExtractText();
            if (rowName.Length == 0)
                continue;

            if (rowName.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
                rowName.Equals($"{wanted} Barding", StringComparison.OrdinalIgnoreCase))
                return row;
        }

        return null;
    }

    /// <summary>Owned barding names containing <paramref name="name"/>; unowned barding is never suggested.</summary>
    public static string[] Suggest(IDataManager data, IUnlockState unlocks, string name, int max = 5)
    {
        var wanted = name.Trim().Trim('"');
        return Owned(data, unlocks)
               .Select(row => row.Name.ExtractText())
               .Where(n => n.Contains(wanted, StringComparison.OrdinalIgnoreCase))
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
    /// Sends the same Buddy agent event the Appearance tab sends, once per slot.
    /// Slots already wearing the piece are skipped, because selecting a worn piece takes it off.
    /// </summary>
    public static unsafe bool Apply(uint rowId)
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Buddy);
        if (agent == null)
            return false;

        var worn = UIState.Instance()->Buddy.CompanionInfo.BuddyEquipRowIds;
        var values = stackalloc AtkValue[3];
        var result = stackalloc AtkValue[1];

        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (worn[slot] == rowId)
                continue;

            values[0].SetInt(SelectBardingEvent);
            values[1].SetInt(slot);
            values[2].SetInt((int)rowId);
            agent->ReceiveEvent(result, values, 3, 0);
        }

        return true;
    }
}
