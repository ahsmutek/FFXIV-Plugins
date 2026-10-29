using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace HuntMap;

internal sealed record Target(int Bill, uint Order, Mark Mark, int Needed);

internal sealed unsafe class Bills
{
    // MobHunt mark bill indices of the weekly Elite bills, A Realm Reborn to Dawntrail.
    private static readonly int[] Elite = [4, 5, 9, 13, 17, 21];

    private readonly IDataManager data;
    private readonly Marks marks;
    private readonly Checks checks;
    private readonly Dictionary<uint, Target?> byOrder = new();

    public Bills(IDataManager data, Marks marks, Checks checks)
    {
        this.data = data;
        this.marks = marks;
        this.checks = checks;
    }

    public List<Target> Held()
    {
        var hunt = MobHunt.Instance();
        var held = new List<Target>();
        if (hunt == null)
            return held;
        foreach (var bill in Elite)
        {
            if (!hunt->IsMarkBillObtained(bill))
                continue;
            var order = (uint)hunt->GetObtainedHuntOrderRowId((byte)bill);
            if (!byOrder.TryGetValue(order, out var target))
            {
                byOrder[order] = target = Load(bill, order);
                if (target != null)
                    checks.BillHeld(target);
            }
            if (target != null)
                held.Add(target);
        }
        return held;
    }

    public static int Kills(Target target)
    {
        var hunt = MobHunt.Instance();
        return hunt == null ? 0 : hunt->GetKillCount((byte)target.Bill, 0);
    }

    public static bool Done(Target target) => Kills(target) >= target.Needed;

    private Target? Load(int bill, uint orderRow)
    {
        if (!data.GetSubrowExcelSheet<MobHuntOrder>().TryGetRow(orderRow, out var orders) || orders.Count == 0)
            return null;
        var order = orders[0];
        if (order.Target.ValueNullable is not { } target || target.Map.ValueNullable is not { } map)
            return null;

        // marks.json is in English.
        var name = data.GetExcelSheet<BNpcName>(ClientLanguage.English).GetRowOrDefault(target.Name.RowId)?.Singular.ExtractText() ?? "";
        var zone = data.GetExcelSheet<PlaceName>(ClientLanguage.English).GetRowOrDefault(map.PlaceName.RowId)?.Name.ExtractText() ?? "";
        return marks.Find(name, zone) is { } mark ? new Target(bill, orderRow, mark, order.NeededKills) : null;
    }
}
