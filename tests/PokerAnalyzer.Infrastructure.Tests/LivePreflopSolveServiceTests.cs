using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopAnalysis;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class LivePreflopSolveServiceTests
{
    [Fact]
    public async Task GetStrategyResultAsync_DefaultMode_DoesNotPersistTrainingState()
    {
        var sharedRegrets = new InMemoryRegretStore();
        var sharedAverage = new InMemoryAverageStrategyStore();
        var sharedProgress = new InMemoryPreflopTrainingProgressStore();
        var sut = new LivePreflopSolveService(sharedRegrets, sharedAverage, sharedProgress, new PreflopInfoSetMapper());

        var request = new PreflopStrategyRequestDto(
            "v2:test",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var first = await sut.GetStrategyResultAsync(request, CancellationToken.None);
        var second = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Fresh", first!.SolveMode);
        Assert.Equal("Fresh", second!.SolveMode);
        Assert.Equal(0, sharedProgress.TotalIterationsCompleted);

        foreach (var action in request.LegalActions)
        {
            Assert.Equal(0d, sharedRegrets.Get(request.SolverKey, action));
            Assert.Equal(0d, sharedAverage.Get(request.SolverKey, action));
        }
    }

    [Fact]
    public async Task GetStrategyResultAsync_PersistentMode_PersistsTrainingState()
    {
        var sharedRegrets = new InMemoryRegretStore();
        var sharedAverage = new InMemoryAverageStrategyStore();
        var sharedProgress = new InMemoryPreflopTrainingProgressStore();
        var sut = new LivePreflopSolveService(sharedRegrets, sharedAverage, sharedProgress, new PreflopInfoSetMapper());

        var request = new PreflopStrategyRequestDto(
            "v2:test",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))],
            UsePersistentTrainingState: true);

        var first = await sut.GetStrategyResultAsync(request, CancellationToken.None);
        var second = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Persistent", first!.SolveMode);
        Assert.Equal("Persistent", second!.SolveMode);
        Assert.True(sharedProgress.TotalIterationsCompleted > 0);

        var hasStoredState = request.LegalActions.Any(action =>
            sharedRegrets.Get(request.SolverKey, action) != 0d ||
            sharedAverage.Get(request.SolverKey, action) != 0d);

        Assert.True(hasStoredState);
    }


    [Fact]
    public async Task GetStrategyResultAsync_MapsLeafEvaluatorMetadata()
    {
        var sut = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), new InMemoryPreflopTrainingProgressStore(), new PreflopInfoSetMapper());

        var request = new PreflopStrategyRequestDto(
            "v2/UNOPENED/BTN/eff=100",
            CreateBtnThreeWayRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var result = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.LeafEvaluationDetails);
        Assert.Equal("AbstractedHeadsUp", result.LeafEvaluationDetails!.EvaluatorType);
        Assert.Equal("WeightedBlindsBTNUnopened", result.LeafEvaluationDetails.AbstractionSource);
        Assert.Equal(2, result.LeafEvaluationDetails.ActualActiveOpponentCount);
    }


    private static SolverHandState CreateBtnThreeWayRootState()
    {
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();

        return new SolverHandState(
            new GameConfig(3, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000)),
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: btnId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players:
            [
                new SolverPlayerState(btnId, 0, Position.BTN, new ChipAmount(10000), ChipAmount.Zero, ChipAmount.Zero, false, false),
                new SolverPlayerState(sbId, 1, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
                new SolverPlayerState(bbId, 2, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
            ],
            actionHistory:
            [
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            ]);
    }

    private static SolverHandState CreateRootState()
    {
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();

        return new SolverHandState(
            new GameConfig(2, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000)),
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: bbId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players:
            [
                new SolverPlayerState(sbId, 0, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
                new SolverPlayerState(bbId, 1, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
            ],
            actionHistory:
            [
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            ]);
    }
}
