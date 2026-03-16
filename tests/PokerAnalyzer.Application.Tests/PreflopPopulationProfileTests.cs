using PokerAnalyzer.Application.PreflopSolver;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopPopulationProfileTests
{
    [Theory]
    [InlineData(PreflopPopulationProfiles.GtoLikeName, 0.23d, 0.34d, 0.45d, 0.45d, 0.08d)]
    [InlineData(PreflopPopulationProfiles.MicroStakesLoosePassiveName, 0.30d, 0.48d, 0.52d, 0.62d, 0.12d)]
    [InlineData(PreflopPopulationProfiles.TightRegsName, 0.20d, 0.30d, 0.36d, 0.40d, 0.07d)]
    public void NamedProvider_SelectsExpectedProfile(string profileName, double sbContinue, double bbContinue, double sbPercentile, double bbPercentile, double raiseRiskFactor)
    {
        var provider = new NamedPreflopPopulationProfileProvider(profileName);

        Assert.Equal(profileName, provider.ActiveProfileName);
        Assert.Equal(sbContinue, provider.ActiveProfile.SbContinueUnopenedVsBtn, precision: 3);
        Assert.Equal(bbContinue, provider.ActiveProfile.BbContinueUnopenedVsBtn, precision: 3);
        Assert.Equal(sbPercentile, provider.ActiveProfile.SbContinueRangePercentileUnopenedVsBtn, precision: 3);
        Assert.Equal(bbPercentile, provider.ActiveProfile.BbContinueRangePercentileUnopenedVsBtn, precision: 3);
        Assert.Equal(raiseRiskFactor, provider.ActiveProfile.RaiseRiskPenaltyFactor, precision: 3);
    }

    [Fact]
    public void BuiltInProfiles_ExposeDistinctBtnUnopenedRangeComposition()
    {
        Assert.NotEqual(PreflopPopulationProfiles.GtoLike.SbContinueRangePercentileUnopenedVsBtn, PreflopPopulationProfiles.MicroStakesLoosePassive.SbContinueRangePercentileUnopenedVsBtn);
        Assert.NotEqual(PreflopPopulationProfiles.GtoLike.BbContinueRangePercentileUnopenedVsBtn, PreflopPopulationProfiles.MicroStakesLoosePassive.BbContinueRangePercentileUnopenedVsBtn);
        Assert.NotEqual(PreflopPopulationProfiles.TightRegs.SbContinueRangePercentileUnopenedVsBtn, PreflopPopulationProfiles.MicroStakesLoosePassive.SbContinueRangePercentileUnopenedVsBtn);
        Assert.NotEqual(PreflopPopulationProfiles.TightRegs.BbContinueRangePercentileUnopenedVsBtn, PreflopPopulationProfiles.MicroStakesLoosePassive.BbContinueRangePercentileUnopenedVsBtn);
    }

    [Fact]
    public void NamedProvider_UnknownProfile_FallsBackToGtoLike()
    {
        var provider = new NamedPreflopPopulationProfileProvider("unknown-profile");

        Assert.Equal(PreflopPopulationProfiles.GtoLikeName, provider.ActiveProfileName);
        Assert.Equal(PreflopPopulationProfiles.GtoLike, provider.ActiveProfile);
    }
}
