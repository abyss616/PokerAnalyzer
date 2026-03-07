using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class InMemoryPolicyProviderTests
{
    [Fact]
    public void TryGetPolicy_FiltersToLegalActions_AndNormalizesToOne()
    {
        var provider = new InMemoryPolicyProvider();
        var infoSetKey = "street=Preflop|position=SB|hero=AKo|history=|pot=3|bet=2|toCall=1";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(6));

        provider.Store(infoSetKey, new Dictionary<LegalAction, double>
        {
            [fold] = 2d,
            [call] = 3d,
            [raise] = 5d
        });

        var found = provider.TryGetPolicy(infoSetKey, new[] { fold, call }, out var policy);

        Assert.True(found);
        Assert.Equal(2d / 5d, policy[fold], 10);
        Assert.Equal(3d / 5d, policy[call], 10);
        Assert.Equal(1d, policy.Values.Sum(), 10);
    }

    [Fact]
    public void TryGetPolicy_WhenFilteredTotalIsNotPositive_ReturnsFalse()
    {
        var provider = new InMemoryPolicyProvider();
        var infoSetKey = "street=Preflop|position=SB|hero=AKo|history=|pot=3|bet=2|toCall=1";
        var fold = new LegalAction(ActionType.Fold);

        provider.Store(infoSetKey, new Dictionary<LegalAction, double>
        {
            [fold] = 0d
        });

        var found = provider.TryGetPolicy(infoSetKey, new[] { fold }, out _);

        Assert.False(found);
    }
}
