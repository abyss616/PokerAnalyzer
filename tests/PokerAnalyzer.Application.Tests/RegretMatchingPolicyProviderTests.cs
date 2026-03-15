using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class RegretMatchingPolicyProviderTests
{
    [Fact]
    public void TryGetPolicy_MissingInfoset_ReturnsUniformOverLegalActions_WhenNoActionValues()
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
        var actionValues = new InMemoryActionValueStore();
        var provider = new RegretMatchingPolicyProvider(regrets, actionValues);
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
    public void TryGetPolicy_AllNonPositiveRegrets_UsesActionValueFallback_WhenValuesDiffer()
    {
        var regrets = new InMemoryRegretStore();
        var actionValues = new InMemoryActionValueStore();
        var provider = new RegretMatchingPolicyProvider(regrets, actionValues);
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        regrets.Add("infoset", fold, -3d);
        regrets.Add("infoset", call, 0d);
        actionValues.AddSamples("infoset", fold, 10d, 1);
        actionValues.AddSamples("infoset", call, 4d, 1);

        var found = provider.TryGetPolicy("infoset", new[] { fold, call }, out var policy);

        Assert.True(found);
        Assert.True(policy[fold] > policy[call]);
        Assert.NotEqual(0.5d, policy[fold], 10);
        Assert.Equal(1d, policy.Values.Sum(), 10);
    }

    [Fact]
    public void TryGetPolicy_AllNonPositiveRegrets_UsesNearUniformFallback_WhenActionValuesEqual()
    {
        var regrets = new InMemoryRegretStore();
        var actionValues = new InMemoryActionValueStore();
        var provider = new RegretMatchingPolicyProvider(regrets, actionValues);
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        regrets.Add("infoset", fold, -3d);
        regrets.Add("infoset", call, -1d);
        actionValues.AddSamples("infoset", fold, 5d, 1);
        actionValues.AddSamples("infoset", call, 5d, 1);

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
