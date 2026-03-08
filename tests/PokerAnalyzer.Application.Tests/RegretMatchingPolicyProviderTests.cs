using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class RegretMatchingPolicyProviderTests
{
    [Fact]
    public void TryGetPolicy_PositiveRegrets_AreNormalized()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var infoSetKey = "street=Preflop|position=SB|hero=AKo|history=|pot=3|bet=2|toCall=1";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(6));

        regrets.Add(infoSetKey, fold, 3d);
        regrets.Add(infoSetKey, call, 1d);
        regrets.Add(infoSetKey, raise, -5d);

        var found = provider.TryGetPolicy(infoSetKey, new[] { fold, call, raise }, out var policy);

        Assert.True(found);
        Assert.Equal(3d / 4d, policy[fold], 10);
        Assert.Equal(1d / 4d, policy[call], 10);
        Assert.Equal(0d, policy[raise], 10);
        Assert.Equal(1d, policy.Values.Sum(), 10);
    }

    [Fact]
    public void TryGetPolicy_WhenAllRegretsNonPositive_ReturnsUniformPolicy()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var infoSetKey = "street=Preflop|position=SB|hero=AKo|history=|pot=3|bet=2|toCall=1";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        regrets.Add(infoSetKey, fold, -1d);
        regrets.Add(infoSetKey, call, 0d);

        var found = provider.TryGetPolicy(infoSetKey, new[] { fold, call }, out var policy);

        Assert.True(found);
        Assert.Equal(0.5d, policy[fold], 10);
        Assert.Equal(0.5d, policy[call], 10);
    }


    [Fact]
    public void TryGetPolicy_MissingRegretEntries_AreTreatedAsZero()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var infoSetKey = "street=Preflop|position=SB|hero=AKo|history=|pot=3|bet=2|toCall=1";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(6));

        regrets.Add(infoSetKey, fold, 3d);

        var found = provider.TryGetPolicy(infoSetKey, new[] { fold, call, raise }, out var policy);

        Assert.True(found);
        Assert.Equal(1d, policy[fold], 10);
        Assert.Equal(0d, policy[call], 10);
        Assert.Equal(0d, policy[raise], 10);
    }

    [Fact]
    public void TryGetPolicy_WithNonEmptyLegalActions_ReturnsTrue()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var infoSetKey = "street=Preflop|position=SB|hero=AKo|history=|pot=3|bet=2|toCall=1";
        var fold = new LegalAction(ActionType.Fold);

        var found = provider.TryGetPolicy(infoSetKey, new[] { fold }, out var policy);

        Assert.True(found);
        Assert.Single(policy);
        Assert.Equal(1d, policy[fold], 10);
    }
    [Fact]
    public void TryGetPolicy_IncludesOnlyLegalActions()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var infoSetKey = "street=Preflop|position=SB|hero=AKo|history=|pot=3|bet=2|toCall=1";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var illegalRaise = new LegalAction(ActionType.Raise, new ChipAmount(8));

        regrets.Add(infoSetKey, fold, 2d);
        regrets.Add(infoSetKey, call, 2d);
        regrets.Add(infoSetKey, illegalRaise, 100d);

        var found = provider.TryGetPolicy(infoSetKey, new[] { fold, call }, out var policy);

        Assert.True(found);
        Assert.Equal(2, policy.Count);
        Assert.DoesNotContain(illegalRaise, policy.Keys);
    }
}
