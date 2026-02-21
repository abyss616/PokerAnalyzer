using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.HandHistory;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public class HandAnalyzerTests
{
    private sealed class CaptureStreetEngine : IStrategyEngine
    {
        public List<Street> SeenStreets { get; } = new();
        public Recommendation Recommend(HandState state, HeroContext hero)
        {
            SeenStreets.Add(state.Street);
            return new(new[] { new RecommendedAction(ActionType.Check) }, "Capture street engine.");
        }
    }

    [Fact]
    public void Analyzer_ReturnsDecisionForHeroActions_AndTracksStreetTransitions()
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
                new(Street.Preflop, hero, ActionType.Check, ChipAmount.Zero),
                new(Street.Flop, villain, ActionType.Check, ChipAmount.Zero),
                new(Street.Flop, hero, ActionType.Check, ChipAmount.Zero)
            }
        );

        var engine = new CaptureStreetEngine();
        var analyzer = new HandAnalyzer(engine);
        var result = analyzer.Analyze(hand);

        Assert.Equal(2, result.Decisions.Count);
        Assert.Equal(Street.Preflop, engine.SeenStreets[0]);
        Assert.Equal(Street.Flop, engine.SeenStreets[1]);
    }
}
