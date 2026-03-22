using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopStrategyQueryServiceTests
{
    [Fact]
    public void GetStrategyResult_UsesNormalizedAverageStrategy_NotRegretMatchedPolicy()
    {
        var infoSetKey = "hero_infoset";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(100));
        var legalActions = new[] { fold, call };

        var averageStrategyStore = new InMemoryAverageStrategyStore();
        averageStrategyStore.Add(infoSetKey, fold, 1d);
        averageStrategyStore.Add(infoSetKey, call, 3d);

        var regretStore = new InMemoryRegretStore();
        regretStore.Add(infoSetKey, fold, 10d);
        regretStore.Add(infoSetKey, call, 1d);

        var sut = new PreflopStrategyQueryService(averageStrategyStore, regretStore, new InMemoryPreflopTrainingProgressStore(), new InMemoryActionValueStore());

        var result = sut.GetStrategyResult(infoSetKey, legalActions);

        Assert.Equal(0.25m, result.AverageStrategy["Fold"]);
        Assert.Equal(0.75m, result.AverageStrategy["Call:1"]);
    }



    [Fact]
    public void GetStrategyResult_WhenAverageMassIsZero_FallsBackToUniformAcrossLegalActions()
    {
        var infoSetKey = "hero_infoset";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(100));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(400));
        var legalActions = new[] { fold, call, raise };

        var sut = new PreflopStrategyQueryService(new InMemoryAverageStrategyStore(), new InMemoryRegretStore(), new InMemoryPreflopTrainingProgressStore(), new InMemoryActionValueStore());

        var result = sut.GetStrategyResult(infoSetKey, legalActions);

        Assert.Equal(3, result.AverageStrategy.Count);
        Assert.Equal(Math.Round(1m / 3m, 15), Math.Round(result.AverageStrategy["Fold"], 15));
        Assert.Equal(Math.Round(1m / 3m, 15), Math.Round(result.AverageStrategy["Call:1"], 15));
        Assert.Equal(Math.Round(1m / 3m, 15), Math.Round(result.AverageStrategy["Raise:4"], 15));
    }






}
