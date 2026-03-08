using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class RegretMatchingPolicyProviderTests
{
    [Fact]
    public void TryGetPolicy_MissingInfoset_ReturnsUniformOverLegalActions()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        var found = provider.TryGetPolicy("missing", new[] { fold, call }, out var policy);

        Assert.True(found);
        Assert.Equal(0.5d, policy[fold], 10);
        Assert.Equal(0.5d, policy[call], 10);
        Assert.Equal(1d, policy.Values.Sum(), 10);
    }

    [Fact]
    public void TryGetPolicy_MixedPositiveRegrets_UsesNormalizedPositiveRegrets()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(6));

        regrets.Add("infoset", fold, 3d);
        regrets.Add("infoset", call, -2d);
        regrets.Add("infoset", raise, 1d);

        var found = provider.TryGetPolicy("infoset", new[] { fold, call, raise }, out var policy);

        Assert.True(found);
        Assert.Equal(0.75d, policy[fold], 10);
        Assert.Equal(0.25d, policy[raise], 10);
        Assert.False(policy.ContainsKey(call));
        Assert.Equal(1d, policy.Values.Sum(), 10);
    }

    [Fact]
    public void TryGetPolicy_AllNonPositiveRegrets_UsesUniformFallback()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        regrets.Add("infoset", fold, -3d);
        regrets.Add("infoset", call, 0d);

        var found = provider.TryGetPolicy("infoset", new[] { fold, call }, out var policy);

        Assert.True(found);
        Assert.Equal(0.5d, policy[fold], 10);
        Assert.Equal(0.5d, policy[call], 10);
        Assert.Equal(1d, policy.Values.Sum(), 10);
    }

    [Fact]
    public void TryGetPolicy_ConsidersOnlyLegalActions_WhenNormalizing()
    {
        var regrets = new InMemoryRegretStore();
        var provider = new RegretMatchingPolicyProvider(regrets);
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(6));

        regrets.Add("infoset", fold, 2d);
        regrets.Add("infoset", call, 3d);
        regrets.Add("infoset", raise, 100d);

        var found = provider.TryGetPolicy("infoset", new[] { fold, call }, out var policy);

        Assert.True(found);
        Assert.Equal(2d / 5d, policy[fold], 10);
        Assert.Equal(3d / 5d, policy[call], 10);
        Assert.False(policy.ContainsKey(raise));
        Assert.Equal(1d, policy.Values.Sum(), 10);
    }
}
