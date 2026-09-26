using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace ChocoboBarding;

/// <summary>
/// Macro lines for the game's own <c>/petglamour</c> and <c>/petsize</c> commands. Names come
/// from the game data, so the lines work in any client language.
/// </summary>
internal static class PetGlamour
{
    private const uint Arcanist = 26;
    private const uint Summoner = 27;
    private const uint Scholar = 28;

    // PetMirage row -> an icon that shows that look (the summon action for it). The glamour
    // sheet has no icons of its own; Selene's summon icon is a placeholder, so hers is the
    // old Summon II fairy.
    private static readonly Dictionary<uint, uint> LookIcons = new()
    {
        [4] = 516,  // Carbuncle
        [1] = 2754, // Emerald Carbuncle
        [2] = 517,  // Topaz Carbuncle
        [3] = 2753, // Ruby Carbuncle
        [5] = 2680, // Ifrit-Egi
        [6] = 2755, // Titan-Egi
        [7] = 2756, // Garuda-Egi
        [8] = 2824, // Selene
    };

    // (Pet row, the summon action that resummons it, the job it needs, PetMirage rows it can
    // be glamoured as). The pet's own look (row 0 for Eos) puts its normal look back.
    private static readonly (uint Pet, uint Summon, uint Job, uint[] Glamours)[] Pets =
    [
        (23, 25798, Arcanist, [4, 1, 2, 3, 5, 6, 7]),
        (6, 17215, Scholar, [0, 8]),
    ];

    // Summons /petsize can resize: (Pet row, icon of the action that summons it).
    private static readonly (uint Pet, uint Icon)[] ResizableSummons =
    [
        (10, 2691), // Demi-Bahamut
        (14, 2765), // Demi-Phoenix
        (46, 2782), // Solar Bahamut
        (30, 2772), // Ruby Ifrit
        (31, 2773), // Topaz Titan
        (32, 2774), // Emerald Garuda
    ];

    // /petsize takes these words as they are; the game's help lists them in English.
    public static readonly string[] Sizes = ["small", "medium", "large"];

    public readonly record struct Look(string Name, uint Icon, string Line);

    public sealed record Pet(string Name, uint Icon, string Resummon, IReadOnlyList<Look> Looks);

    /// <summary>A summon /petsize can resize; <c>Line(size)</c> is the macro line for one size.</summary>
    public sealed record Resize(string Name, uint Icon)
    {
        public string Line(string size) => $"/petsize \"{Name}\" {size}";
    }

    /// <summary>
    /// Pets whose look you can change: Carbuncle once you have Arcanist (or Summoner), Eos once
    /// you have Scholar. Empty while logged out.
    /// </summary>
    public static List<Pet> Glamourable(IDataManager data, IPlayerState player, IUnlockState unlocks)
    {
        var pets = new List<Pet>();
        if (!player.IsLoaded)
            return pets;

        var petSheet = data.GetExcelSheet<Lumina.Excel.Sheets.Pet>();
        var mirages = data.GetExcelSheet<PetMirage>();
        var actions = data.GetExcelSheet<LuminaAction>();

        foreach (var (petRow, summonRow, jobRow, glamours) in Pets)
        {
            if (!HasJob(data, player, unlocks, jobRow) || !petSheet.TryGetRow(petRow, out var pet) ||
                !actions.TryGetRow(summonRow, out var summon))
                continue;

            var name = pet.Name.ExtractText();
            var looks = new List<Look>();
            foreach (var glamourRow in glamours)
            {
                var glamour = glamourRow != 0 && mirages.TryGetRow(glamourRow, out var mirage) ? mirage.Name.ExtractText() : name;
                var icon = LookIcons.GetValueOrDefault(glamourRow, summon.Icon);
                // No glamour named: the command puts the normal look back.
                var line = glamour == name ? $"/petglamour \"{name}\"" : $"/petglamour \"{name}\" \"{glamour}\"";
                looks.Add(new Look(glamour, icon, line));
            }

            pets.Add(new Pet(name, summon.Icon, $"/ac \"{summon.Name.ExtractText()}\"", looks));
        }

        return pets;
    }

    /// <summary>The summons /petsize can resize, once you have Summoner. Empty otherwise.</summary>
    public static List<Resize> Resizable(IDataManager data, IPlayerState player, IUnlockState unlocks)
    {
        var list = new List<Resize>();
        if (!player.IsLoaded || !HasJob(data, player, unlocks, Summoner))
            return list;

        var petSheet = data.GetExcelSheet<Lumina.Excel.Sheets.Pet>();
        foreach (var (petRow, icon) in ResizableSummons)
        {
            if (petSheet.TryGetRow(petRow, out var pet))
                list.Add(new Resize(pet.Name.ExtractText(), icon));
        }

        return list;
    }

    // Arcanist counts once it has levels (Summoner and Scholar share them); the jobs
    // themselves count once unlocked.
    private static bool HasJob(IDataManager data, IPlayerState player, IUnlockState unlocks, uint jobRow) =>
        data.GetExcelSheet<ClassJob>().TryGetRow(jobRow, out var job) &&
        (jobRow == Arcanist ? player.GetClassJobLevel(job) > 0 : unlocks.IsClassJobUnlocked(job));
}
