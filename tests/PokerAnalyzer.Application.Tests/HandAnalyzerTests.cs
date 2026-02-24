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

    [Fact]
    public void Analyzer_IgnoresForcedBlindPosts()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var seats = new List<PlayerSeat>
        {
            new(hero, "Hero", 1, Position.SB, new ChipAmount(1000)),
            new(villain, "Villain", 2, Position.BB, new ChipAmount(1000)),
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
                new(Street.Preflop, hero, ActionType.PostSmallBlind, new ChipAmount(5)),
                new(Street.Preflop, villain, ActionType.PostBigBlind, new ChipAmount(10)),
                new(Street.Preflop, hero, ActionType.Call, new ChipAmount(10))
            }
        );

        var analyzer = new HandAnalyzer(new AlwaysCheckEngine());
        var result = analyzer.Analyze(hand);

        Assert.Single(result.Decisions);
        Assert.Equal(ActionType.Call, result.Decisions[0].ActualAction.Type);
    }

    [Fact]
    public void Analyzer_TransitionsStreetBeforePostflopDecision()
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

        var analyzer = new HandAnalyzer(new AlwaysCheckEngine());
        var result = analyzer.Analyze(hand);

        Assert.Equal(2, result.Decisions.Count);
        Assert.Equal(Street.Flop, result.Decisions[1].Street);
    }

    [Fact]
    public void Analyzer_AllowsHeadsUpSmallBlindVsBigBlind()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var hand = new Domain.HandHistory.Hand(
            Guid.NewGuid(),
            new ChipAmount(5),
            new ChipAmount(10),
            new List<PlayerSeat>
            {
                new(hero, "Hero", 1, Position.SB, new ChipAmount(1000)),
                new(villain, "Villain", 2, Position.BB, new ChipAmount(1000)),
            },
            hero,
            HoleCards.Parse("AsKh"),
            new Board(),
            new List<BettingAction>
            {
                new(Street.Preflop, hero, ActionType.Call, new ChipAmount(10))
            });

        var analyzer = new HandAnalyzer(new AlwaysCheckEngine());
        var result = analyzer.Analyze(hand);

        Assert.Single(result.Decisions);
    }

    [Fact]
    public void Analyzer_AllowsSixMaxUtgVsBigBlind()
    {
        var hero = PlayerId.New();
        var bb = PlayerId.New();
        var hj = PlayerId.New();
        var co = PlayerId.New();
        var btn = PlayerId.New();
        var sb = PlayerId.New();

        var hand = new Domain.HandHistory.Hand(
            Guid.NewGuid(),
            new ChipAmount(5),
            new ChipAmount(10),
            new List<PlayerSeat>
            {
                new(hero, "Hero", 1, Position.UTG, new ChipAmount(1000)),
                new(hj, "HJ", 2, Position.HJ, new ChipAmount(1000)),
                new(co, "CO", 3, Position.CO, new ChipAmount(1000)),
                new(btn, "BTN", 4, Position.BTN, new ChipAmount(1000)),
                new(sb, "SB", 5, Position.SB, new ChipAmount(1000)),
                new(bb, "BB", 6, Position.BB, new ChipAmount(1000)),
            },
            hero,
            HoleCards.Parse("AsKh"),
            new Board(),
            new List<BettingAction>
            {
                new(Street.Preflop, hero, ActionType.Raise, new ChipAmount(25))
            });

        var analyzer = new HandAnalyzer(new AlwaysCheckEngine());
        var result = analyzer.Analyze(hand);

        Assert.Single(result.Decisions);
    }

    [Fact]
    public void Analyzer_ThrowsWhenHeroAndVillainSharePosition()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var hand = new Domain.HandHistory.Hand(
            Guid.NewGuid(),
            new ChipAmount(5),
            new ChipAmount(10),
            new List<PlayerSeat>
            {
                new(hero, "Hero", 1, Position.UTG, new ChipAmount(1000)),
                new(villain, "Villain", 2, Position.UTG, new ChipAmount(1000)),
            },
            hero,
            HoleCards.Parse("AsKh"),
            new Board(),
            new List<BettingAction>());

        var analyzer = new HandAnalyzer(new AlwaysCheckEngine());

        var ex = Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(hand));
        Assert.Contains("Hero and Villain cannot have the same position", ex.Message);
    }
}
