using PokerAnalyzer.Application.PreflopSolver;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopPopulationProfileTests
{
    [Theory]
    [InlineData(PreflopPopulationProfiles.GtoLikeName, 0.23d, 0.34d, 0.08d)]
    [InlineData(PreflopPopulationProfiles.MicroStakesLoosePassiveName, 0.30d, 0.48d, 0.12d)]
    [InlineData(PreflopPopulationProfiles.TightRegsName, 0.20d, 0.30d, 0.07d)]
    public void NamedProvider_SelectsExpectedProfile(string profileName, double sbContinue, double bbContinue, double raiseRiskFactor)
    {
        var provider = new NamedPreflopPopulationProfileProvider(profileName);

        Assert.Equal(profileName, provider.ActiveProfileName);
        Assert.Equal(sbContinue, provider.ActiveProfile.SbContinueUnopenedVsBtn, precision: 3);
        Assert.Equal(bbContinue, provider.ActiveProfile.BbContinueUnopenedVsBtn, precision: 3);
        Assert.Equal(raiseRiskFactor, provider.ActiveProfile.RaiseRiskPenaltyFactor, precision: 3);
    }

    [Fact]
    public void NamedProvider_UnknownProfile_FallsBackToGtoLike()
    {
        var provider = new NamedPreflopPopulationProfileProvider("unknown-profile");

        Assert.Equal(PreflopPopulationProfiles.GtoLikeName, provider.ActiveProfileName);
        Assert.Equal(PreflopPopulationProfiles.GtoLike, provider.ActiveProfile);
    }
}
