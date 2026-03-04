using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class HandAnalyzerPreflopAllInTests
{
    private sealed class AlwaysCheckEngine : IStrategyEngine
    {
        public Recommendation Recommend(HandState state, HeroContext hero)
            => new(new[] { new RecommendedAction(ActionType.Check) }, "test");
    }

    private sealed class FixedEquityCalculator : IAllInEquityCalculator
    {
        public Task<AllInEquityResult> ComputePreflopAsync(IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> players, int? samples, int? seed, CancellationToken ct)
        {
            var eq = players.ToDictionary(x => x.PlayerId, _ => 0.5m);
            return Task.FromResult(new AllInEquityResult(eq, "Exact", null, null));
        }
    }

    private sealed class FixedFlopContinuationValueCalculator : IFlopContinuationValueCalculator
    {
        public Task<FlopContinuationValueResult> ComputeAsync(
            IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> knownPlayers,
            HandState endOfPreflopState,
            int flopsToSample = 50_000,
            int? seed = null,
            CancellationToken ct = default)
        {
            var chipEv = knownPlayers.ToDictionary(x => x.PlayerId, _ => 0m);
            return Task.FromResult(new FlopContinuationValueResult(chipEv, "MonteCarlo", flopsToSample, seed ?? 0));
        }
    }

    [Fact]
    public void Analyzer_Attaches_PreflopAllIn_Result_When_KnownCardsExist()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(hero, "Hero", 1, Position.BB, new ChipAmount(100)),
            new(villain, "Villain", 2, Position.BTN, new ChipAmount(100))
        };

        var hand = new Domain.HandHistory.Hand(
            Guid.NewGuid(),
            new ChipAmount(5),
            new ChipAmount(10),
            seats,
            hero,
            HoleCards.Parse("AsAh"),
            new Dictionary<PlayerId, HoleCards> { [villain] = HoleCards.Parse("KsKh") },
            new Board(),
            new List<BettingAction>
            {
                new(Street.Preflop, villain, ActionType.Raise, new ChipAmount(100)),
                new(Street.Preflop, hero, ActionType.AllIn, new ChipAmount(100))
            });

        var sut = new HandAnalyzer(new AlwaysCheckEngine(), new FixedEquityCalculator());
        var result = sut.Analyze(hand);

        Assert.NotNull(result.PreflopAllIn);
        Assert.Equal(Street.Preflop, result.PreflopAllIn!.Street);
        Assert.InRange(result.PreflopAllIn.Equities.Values.Sum(), 0.999m, 1.001m);
    }

    [Fact]
    public void Analyzer_DoesNotAttach_PreflopToFlop_When_PreflopAllIn_Exists()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();
        var thirdPlayer = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(hero, "Hero", 1, Position.BB, new ChipAmount(100)),
            new(villain, "Villain", 2, Position.BTN, new ChipAmount(100)),
            new(thirdPlayer, "Third", 3, Position.SB, new ChipAmount(200))
        };

        var hand = new Domain.HandHistory.Hand(
            Guid.NewGuid(),
            new ChipAmount(5),
            new ChipAmount(10),
            seats,
            hero,
            HoleCards.Parse("AsAh"),
            new Dictionary<PlayerId, HoleCards>
            {
                [villain] = HoleCards.Parse("KsKh"),
                [thirdPlayer] = HoleCards.Parse("QdQs")
            },
            new Board(),
            new List<BettingAction>
            {
                new(Street.Preflop, villain, ActionType.AllIn, new ChipAmount(100)),
                new(Street.Preflop, thirdPlayer, ActionType.Call, new ChipAmount(100)),
                new(Street.Preflop, hero, ActionType.AllIn, new ChipAmount(100))
            });

        var sut = new HandAnalyzer(new AlwaysCheckEngine(), new FixedEquityCalculator(), new FixedFlopContinuationValueCalculator());
        var result = sut.Analyze(hand);

        Assert.NotNull(result.PreflopAllIn);
        Assert.Null(result.PreflopToFlop);
    }
}
