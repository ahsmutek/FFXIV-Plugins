using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace HuntMap;

// Pixel is on the 2048px map texture.
internal sealed record Spot(int Index, Vector2 Coord, Vector2 Pixel, Vector2 World);

internal sealed record Target(int Bill, uint Order, string Name, string Zone, uint TerritoryId, uint MapId, string MapTexture,
                              float Scale, Vector2 Offset, int Needed, List<Spot> Spots)
{
    public Vector2 CoordOf(Vector2 world) => 41f / Scale * (((world + Offset) * Scale + new Vector2(1024)) / 2048f) + Vector2.One;
}

internal sealed unsafe class Bills
{
    // MobHunt mark bill indices of the weekly Elite bills, A Realm Reborn to Dawntrail.
    private static readonly int[] Elite = [4, 5, 9, 13, 17, 21];

    private readonly IDataManager data;
    private readonly Marks marks;
    private readonly Dictionary<uint, Target?> byOrder = new();

    public Bills(IDataManager data, Marks marks)
    {
        this.data = data;
        this.marks = marks;
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
                byOrder[order] = target = Load(bill, order);
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
        if (marks.For(name, zone) is not { } coords)
            return null;

        var scale = map.SizeFactor / 100f;
        var offset = new Vector2(map.OffsetX, map.OffsetY);
        var spots = coords.Select((coord, i) =>
        {
            var pixel = (coord - Vector2.One) * scale / 41f * 2048f;
            return new Spot(i, coord, pixel, (pixel - new Vector2(1024)) / scale - offset);
        }).ToList();

        var id = map.Id.ExtractText();
        return new Target(bill, orderRow, Title(target.Name.ValueNullable?.Singular.ExtractText() ?? name),
                          map.PlaceName.ValueNullable?.Name.ExtractText() ?? zone, map.TerritoryType.RowId, map.RowId,
                          $"ui/map/{id}/{id.Replace("/", "")}_m.tex", scale, offset, order.NeededKills, spots);
    }

    // Some names are stored lowercase, e.g. "mad maguey".
    private static string Title(string name) =>
        name.Length > 0 && char.IsLower(name[0])
            ? string.Join(' ', name.Split(' ').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w))
            : name;
}
