using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class InMemoryAverageStrategyStoreTests
{
    [Fact]
    public void GetAveragePolicy_NormalizesAccumulatedWeights()
    {
        var store = new InMemoryAverageStrategyStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var raise = new LegalAction(ActionType.RaiseTo, new ChipAmount(4));

        store.Add("infoset", fold, 3d);
        store.Add("infoset", call, 1d);
        store.Add("infoset", raise, 0d);

        var policy = store.GetAveragePolicy("infoset", new[] { fold, call, raise });

        Assert.Equal(0.75d, policy[fold], 10);
        Assert.Equal(0.25d, policy[call], 10);
        Assert.Equal(0d, policy[raise], 10);
    }

    [Fact]
    public void GetAveragePolicy_WhenNoAccumulatedWeight_ReturnsUniform()
    {
        var store = new InMemoryAverageStrategyStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        var policy = store.GetAveragePolicy("missing", new[] { fold, call });

        Assert.Equal(0.5d, policy[fold], 10);
        Assert.Equal(0.5d, policy[call], 10);
    }

    [Fact]
    public void Add_AccumulatesAcrossMultipleCalls()
    {
        var store = new InMemoryAverageStrategyStore();
        var fold = new LegalAction(ActionType.Fold);

        store.Add("infoset", fold, 0.2d);
        store.Add("infoset", fold, 0.3d);

        Assert.Equal(0.5d, store.Get("infoset", fold), 10);
    }
}
