using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.HandHistory;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public class HandAnalyzerTests
{
    private sealed class AlwaysCheckEngine : IStrategyEngine
    {
        public Recommendation Recommend(HandState state, HeroContext hero)
            => new(new[] { new RecommendedAction(ActionType.Check) }, "Always-check test engine.");
    }

    [Fact]
    public void Analyzer_ReturnsDecisionForHeroActions()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var seats = new List<PlayerSeat>
        {
            new(hero, "Hero", 1, Position.BB, new ChipAmount(1000)),
            new(villain, "Villain", 2, Position.BTN, new ChipAmount(1000)),
        };

        var hand = new Domain.HandHistory.Hand(
            HandId: Guid.NewGuid(),
            SmallBlind: new ChipAmount(5),
            BigBlind: new ChipAmount(10),
            Seats: seats,
            HeroId: hero,
            HeroHoleCards: HoleCards.Parse("AsKh"),
            Board: new Board(),
            Actions: new List<BettingAction>
            {
                new(Street.Preflop, villain, ActionType.Call, ChipAmount.Zero),
                new(Street.Preflop, hero, ActionType.Check, ChipAmount.Zero)
            }
        );

        var analyzer = new HandAnalyzer(new AlwaysCheckEngine());
        var result = analyzer.Analyze(hand);

        Assert.Single(result.Decisions);
        Assert.Equal(DecisionSeverity.Ok, result.Decisions[0].Severity);
    }
}
