namespace PokerAnalyzer.Application.PreflopSolver;

public sealed record PreflopPopulationProfile(
    double SbContinueUnopenedVsBtn,
    double BbContinueUnopenedVsBtn,
    double SbContinueRangePercentileUnopenedVsBtn,
    double BbContinueRangePercentileUnopenedVsBtn,
    double RaiseRiskPenaltyFactor,
    double OffsuitBroadwayRealizationPenalty,
    double WeakOffsuitRealizationPenalty);

public static class PreflopPopulationProfiles
{
    public const string GtoLikeName = "GtoLike";
    public const string MicroStakesLoosePassiveName = "MicroStakesLoosePassive";
    public const string TightRegsName = "TightRegs";

    public static PreflopPopulationProfile GtoLike { get; } = new(
        SbContinueUnopenedVsBtn: 0.23d,
        BbContinueUnopenedVsBtn: 0.34d,
        SbContinueRangePercentileUnopenedVsBtn: 0.45d,
        BbContinueRangePercentileUnopenedVsBtn: 0.45d,
        RaiseRiskPenaltyFactor: 0.08d,
        OffsuitBroadwayRealizationPenalty: 0.00d,
        WeakOffsuitRealizationPenalty: 0.00d);

    public static PreflopPopulationProfile MicroStakesLoosePassive { get; } = new(
        SbContinueUnopenedVsBtn: 0.30d,
        BbContinueUnopenedVsBtn: 0.48d,
        SbContinueRangePercentileUnopenedVsBtn: 0.52d,
        BbContinueRangePercentileUnopenedVsBtn: 0.62d,
        RaiseRiskPenaltyFactor: 0.12d,
        OffsuitBroadwayRealizationPenalty: 0.03d,
        WeakOffsuitRealizationPenalty: 0.05d);

    public static PreflopPopulationProfile TightRegs { get; } = new(
        SbContinueUnopenedVsBtn: 0.20d,
        BbContinueUnopenedVsBtn: 0.30d,
        SbContinueRangePercentileUnopenedVsBtn: 0.36d,
        BbContinueRangePercentileUnopenedVsBtn: 0.40d,
        RaiseRiskPenaltyFactor: 0.07d,
        OffsuitBroadwayRealizationPenalty: 0.01d,
        WeakOffsuitRealizationPenalty: 0.02d);

    public static bool TryGetByName(string? name, out PreflopPopulationProfile profile)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? GtoLikeName : name.Trim();
        if (normalized.Equals(GtoLikeName, StringComparison.OrdinalIgnoreCase))
        {
            profile = GtoLike;
            return true;
        }

        if (normalized.Equals(MicroStakesLoosePassiveName, StringComparison.OrdinalIgnoreCase))
        {
            profile = MicroStakesLoosePassive;
            return true;
        }

        if (normalized.Equals(TightRegsName, StringComparison.OrdinalIgnoreCase))
        {
            profile = TightRegs;
            return true;
        }

        profile = GtoLike;
        return false;
    }
}

public interface IPreflopPopulationProfileProvider
{
    string ActiveProfileName { get; }
    PreflopPopulationProfile ActiveProfile { get; }
}

public sealed class NamedPreflopPopulationProfileProvider : IPreflopPopulationProfileProvider
{
    public NamedPreflopPopulationProfileProvider(string? profileName)
    {
        if (!PreflopPopulationProfiles.TryGetByName(profileName, out var profile))
            profileName = PreflopPopulationProfiles.GtoLikeName;

        ActiveProfileName = profileName ?? PreflopPopulationProfiles.GtoLikeName;
        ActiveProfile = profile;
    }

    public string ActiveProfileName { get; }
    public PreflopPopulationProfile ActiveProfile { get; }
}
