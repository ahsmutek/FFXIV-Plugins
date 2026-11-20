using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Commendable;

// Only what another player chose to do for you specifically; party-wide heals, mitigation and buffs stay out.
internal static class Registry
{
    public const byte StatusAppliedEffect = 14;
    public const byte StatusRemovedEffect = 16;

    private const uint Raise = 125, Resurrection = 173, Ascend = 3603, Verraise = 7523, Egeiro = 24287,
                       AngelWhisper = 18317, LostArise = 20730, VariantRaise = 29731, VariantRaiseII = 29734,
                       OccultRaise = 49070;
    private const uint PulseOfLife = 208, AngelFeathers = 4247, AstralStasis = 4248, TechneMakre = 24859;

    private const uint Cover = 27, Intervention = 7382, NascentFlash = 16464, BlackestNight = 7393, Oblation = 25754,
                       HeartOfStone = 16161, HeartOfCorundum = 25758, DivineBenison = 7432, Aquaveil = 25861,
                       Adloquium = 185, Manifestation = 37015, Excogitation = 7434, Protraction = 25867,
                       CelestialIntersection = 16556, Exaltation = 25873, EukrasianDiagnosis = 24291, Haima = 24305,
                       Taurochole = 24303, Krasis = 24317;

    private const uint Cure = 120, CureII = 135, Regen = 137, Benediction = 140, Tetragrammaton = 3570,
                       AfflatusSolace = 16531, Physick = 190, Lustrate = 189, Benefic = 3594, BeneficII = 3610,
                       AspectedBenefic = 3595, EssentialDignity = 3614, Diagnosis = 24284, Druochole = 24296,
                       Clemency = 3541, Aurora = 16151, ArcanistPhysick = 16230, Rekindle = 25830, Vercure = 7514,
                       PomCure = 18303;

    public const uint Esuna = 7568;
    private const uint WardensPaean = 3561, Rescue = 7571;
    private const uint ClosedPosition = 16006, Kardia = 24285, Synastry = 3612, Aetherpact = 7437;

    private const ushort RaiseStatus = 148, DutyRaiseStatus = 1140;
    private const ushort DancePartner = 1824, Kardion = 2605, SynastryBond = 846, FeyUnion = 1223;
    private const ushort TheBalance = 3887, TheArrow = 3888, TheSpear = 3889, TheBole = 3890, TheEwer = 3891,
                         TheSpire = 3892;

    private const uint Conjurer = 6, WhiteMage = 24, Scholar = 28, Astrologian = 33, Sage = 40;

    public static readonly FrozenDictionary<uint, Category> Actions = Map(
        (Category.Resurrection,
         [Raise, Resurrection, Ascend, Verraise, Egeiro, AngelWhisper, LostArise, VariantRaise, VariantRaiseII, OccultRaise]),
        (Category.LimitBreakRevival, [PulseOfLife, AngelFeathers, AstralStasis, TechneMakre]),
        (Category.Protection,
         [Cover, Intervention, NascentFlash, BlackestNight, Oblation, HeartOfStone, HeartOfCorundum, DivineBenison,
          Aquaveil, Adloquium, Manifestation, Excogitation, Protraction, CelestialIntersection, Exaltation,
          EukrasianDiagnosis, Haima, Taurochole, Krasis]),
        (Category.Healing,
         [Cure, CureII, Regen, Benediction, Tetragrammaton, AfflatusSolace, Physick, Lustrate, Benefic, BeneficII,
          AspectedBenefic, EssentialDignity, Diagnosis, Druochole, Clemency, Aurora, ArcanistPhysick, Rekindle, Vercure,
          PomCure]),
        (Category.Cleanse, [Esuna, WardensPaean]),
        (Category.Rescue, [Rescue]));

    // An upgrade and what it replaces share one summary line; the event log keeps which was used.
    public static readonly FrozenDictionary<uint, uint> ShownAs =
        new Dictionary<uint, uint> { [HeartOfStone] = HeartOfCorundum }.ToFrozenDictionary();

    public static readonly FrozenSet<ushort> RaiseStatuses = new[] { RaiseStatus, DutyRaiseStatus }.ToFrozenSet();

    // Read from the status a card puts on you, so it doesn't matter which Play action the game reports.
    public static readonly FrozenDictionary<ushort, bool> CardIsOffensive = new Dictionary<ushort, bool>
    {
        [TheBalance] = true,
        [TheSpear] = true,
        [TheArrow] = false,
        [TheBole] = false,
        [TheEwer] = false,
        [TheSpire] = false,
    }.ToFrozenDictionary();

    // The status sits on the chosen partner, with the player who chose them (or their fairy) as its source.
    public static readonly FrozenDictionary<ushort, (Category Category, uint ActionId)> PartnerStatuses =
        new Dictionary<ushort, (Category, uint)>
        {
            [DancePartner] = (Category.DancePartner, ClosedPosition),
            [Kardion] = (Category.Kardia, Kardia),
            [SynastryBond] = (Category.Synastry, Synastry),
            [FeyUnion] = (Category.Aetherpact, Aetherpact),
        }.ToFrozenDictionary();

    public static readonly FrozenSet<uint> HealerJobs =
        new[] { Conjurer, WhiteMage, Scholar, Astrologian, Sage }.ToFrozenSet();

    private static FrozenDictionary<uint, Category> Map(params (Category Category, uint[] Ids)[] groups) =>
        groups.SelectMany(g => g.Ids.Select(id => KeyValuePair.Create(id, g.Category))).ToFrozenDictionary();
}
