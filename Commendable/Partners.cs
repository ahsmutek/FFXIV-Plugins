using System.Collections.Generic;

namespace Commendable;

// A partner status dropping (death, zoning) and coming back isn't a new choice; only a different partner is.
internal sealed class Partners
{
    private readonly Dictionary<(Category Category, PlayerKey Source), PlayerKey> chosen = [];

    public bool Chose(Category category, PlayerKey source, PlayerKey partner)
    {
        if (chosen.TryGetValue((category, source), out var previous) && previous == partner)
            return false;
        chosen[(category, source)] = partner;
        return true;
    }
}
