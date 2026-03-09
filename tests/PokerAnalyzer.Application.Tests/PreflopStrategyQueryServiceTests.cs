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

        var sut = new PreflopStrategyQueryService(averageStrategyStore, regretStore, new InMemoryPreflopTrainingProgressStore());

        var result = sut.GetStrategyResult(infoSetKey, legalActions);

        Assert.Equal(0.25m, result.AverageStrategy["Fold"]);
        Assert.Equal(0.75m, result.AverageStrategy["Call:1"]);
    }

    [Fact]
    public void GetStrategyResult_ComputesRegretMagnitude_AsSumOfPositiveRegrets()
    {
        var infoSetKey = "hero_infoset";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(100));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(400));
        var legalActions = new[] { fold, call, raise };

        var averageStrategyStore = new InMemoryAverageStrategyStore();
        var regretStore = new InMemoryRegretStore();
        regretStore.Add(infoSetKey, fold, 5d);
        regretStore.Add(infoSetKey, call, -3d);
        // raise missing on purpose

        var sut = new PreflopStrategyQueryService(averageStrategyStore, regretStore, new InMemoryPreflopTrainingProgressStore());

        var result = sut.GetStrategyResult(infoSetKey, legalActions);

        Assert.Equal(5d, result.RegretMagnitude, 10);
    }

    [Fact]
    public void GetStrategyResult_UsesTrainingProgressStore_ForIterationsCompleted()
    {
        var infoSetKey = "hero_infoset";
        var legalActions = new[] { new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)) };
        var progress = new InMemoryPreflopTrainingProgressStore();
        progress.IncrementIterations(17);

        var sut = new PreflopStrategyQueryService(new InMemoryAverageStrategyStore(), new InMemoryRegretStore(), progress);

        var result = sut.GetStrategyResult(infoSetKey, legalActions);

        Assert.Equal(17, result.IterationsCompleted);
    }

    [Fact]
    public void GetStrategyResult_WhenAverageMassIsZero_FallsBackToUniformAcrossLegalActions()
    {
        var infoSetKey = "hero_infoset";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(100));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(400));
        var legalActions = new[] { fold, call, raise };

        var sut = new PreflopStrategyQueryService(new InMemoryAverageStrategyStore(), new InMemoryRegretStore(), new InMemoryPreflopTrainingProgressStore());

        var result = sut.GetStrategyResult(infoSetKey, legalActions);

        Assert.Equal(3, result.AverageStrategy.Count);
        Assert.Equal(Math.Round(1m / 3m, 15), Math.Round(result.AverageStrategy["Fold"], 15));
        Assert.Equal(Math.Round(1m / 3m, 15), Math.Round(result.AverageStrategy["Call:1"], 15));
        Assert.Equal(Math.Round(1m / 3m, 15), Math.Round(result.AverageStrategy["Raise:4"], 15));
    }

    [Fact]
    public void GetStrategyResult_DoesNotMutateStores_OrTrainingBehavior()
    {
        var infoSetKey = "hero_infoset";
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(100));
        var legalActions = new[] { fold, call };

        var averages = new InMemoryAverageStrategyStore();
        averages.Add(infoSetKey, fold, 2d);
        averages.Add(infoSetKey, call, 2d);

        var regrets = new InMemoryRegretStore();
        regrets.Add(infoSetKey, fold, 3d);
        regrets.Add(infoSetKey, call, -1d);

        var progress = new InMemoryPreflopTrainingProgressStore();
        progress.IncrementIterations(4);

        var sut = new PreflopStrategyQueryService(averages, regrets, progress);

        _ = sut.GetStrategyResult(infoSetKey, legalActions);

        Assert.Equal(2d, averages.Get(infoSetKey, fold), 10);
        Assert.Equal(2d, averages.Get(infoSetKey, call), 10);
        Assert.Equal(3d, regrets.Get(infoSetKey, fold), 10);
        Assert.Equal(-1d, regrets.Get(infoSetKey, call), 10);
        Assert.Equal(4, progress.TotalIterationsCompleted);
    }
}
