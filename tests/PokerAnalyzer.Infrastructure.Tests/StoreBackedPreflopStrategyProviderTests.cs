using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopAnalysis;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class StoreBackedPreflopStrategyProviderTests
{
    [Fact]
    public async Task GetStrategyResultAsync_ForwardsLegalActions_ToQueryService()
    {
        var query = new CapturingQueryService();
        var sut = new StoreBackedPreflopStrategyProvider(query);
        var request = new PreflopStrategyRequestDto(
            "v2/key",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        _ = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(query.CapturedLegalActions);
        Assert.Contains(query.CapturedLegalActions!, a => a.ActionType == ActionType.Call && a.Amount?.Value == 100);
        Assert.Contains(query.CapturedLegalActions!, a => a.ActionType == ActionType.Raise && a.Amount?.Value == 250);
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

    private sealed class CapturingQueryService : IPreflopStrategyQueryService
    {
        public IReadOnlyList<LegalAction>? CapturedLegalActions { get; private set; }

        public PreflopStrategyResultDto GetStrategyResult(string infoSetKey, IReadOnlyList<LegalAction> legalActions)
        {
            CapturedLegalActions = legalActions.ToArray();
            var strategy = legalActions.ToDictionary(a => a.ActionType.ToString(), _ => 1m / legalActions.Count);
            return new PreflopStrategyResultDto(infoSetKey, strategy, 0, 0d);
        }
    }
}
