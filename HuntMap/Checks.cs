using System;
using System.Collections.Generic;

namespace HuntMap;

internal sealed class Checks
{
    private readonly Configuration config;
    private readonly Action save;

    public Checks(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public HashSet<int>? Of(Mark mark) => config.CheckedSpots.GetValueOrDefault(mark.Key);

    public bool Check(Mark mark, int spot)
    {
        if (!config.CheckedSpots.TryGetValue(mark.Key, out var spots))
            config.CheckedSpots[mark.Key] = spots = new();
        if (!spots.Add(spot))
            return false;
        save();
        return true;
    }

    public void Toggle(Mark mark, int spot)
    {
        if (!config.CheckedSpots.TryGetValue(mark.Key, out var spots))
            config.CheckedSpots[mark.Key] = spots = new();
        if (!spots.Remove(spot))
            spots.Add(spot);
        if (spots.Count == 0)
            config.CheckedSpots.Remove(mark.Key);
        save();
    }

    public void Clear(Mark mark)
    {
        if (config.CheckedSpots.Remove(mark.Key))
            save();
    }

    // A new bill starts its mark's checks over.
    public void BillHeld(Target target)
    {
        if (config.BillOrders.GetValueOrDefault(target.Bill) == target.Order)
            return;
        config.BillOrders[target.Bill] = target.Order;
        config.CheckedSpots.Remove(target.Mark.Key);
        config.StoppedMarks.Remove(target.Mark.Key);
        save();
    }
}
