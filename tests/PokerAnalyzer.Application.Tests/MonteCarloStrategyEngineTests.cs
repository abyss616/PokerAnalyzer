using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public class MonteCarloStrategyEngineTests
{
    [Fact]
    public void Recommend_FallsBackToDummy_WhenHeroCardsMissing()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            new[]
            {
                new PlayerSeat(hero, "Hero", 1, Position.BB, new ChipAmount(1000)),
                new PlayerSeat(villain, "Villain", 2, Position.BTN, new ChipAmount(1000))
            },
            new ChipAmount(5),
            new ChipAmount(10));

        state = state.Apply(new BettingAction(Street.Preflop, villain, ActionType.Call, ChipAmount.Zero));

        var ctx = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10));
        var engine = new MonteCarloStrategyEngine();

        var rec = engine.Recommend(state, ctx);

        Assert.NotEmpty(rec.RankedActions);
        Assert.Contains("fallback", rec.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recommend_ReturnsEstimatedEv_WithBasicRangeInputs()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            new[]
            {
                new PlayerSeat(hero, "Hero", 1, Position.BB, new ChipAmount(1000)),
                new PlayerSeat(villain, "Villain", 2, Position.BTN, new ChipAmount(1000))
            },
            new ChipAmount(5),
            new ChipAmount(10));

        state = state.Apply(new BettingAction(Street.Preflop, villain, ActionType.Call, ChipAmount.Zero));

        var ctx = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("AsKh"),
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [hero] = Position.BB,
                [villain] = Position.BTN
            },
            ActionHistory =
            [
                new BettingAction(Street.Preflop, villain, ActionType.Call, ChipAmount.Zero)
            ]
        };

        var engine = new MonteCarloStrategyEngine();
        var rec = engine.Recommend(state, ctx);

        Assert.NotEmpty(rec.RankedActions);
        Assert.All(rec.RankedActions, a => Assert.NotNull(a.EstimatedEv));
        Assert.Contains("preflop policy", rec.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recommend_PreflopPolicy_PrefersAggression_WithPremiumHandInBigBlindOption()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            new[]
            {
                new PlayerSeat(hero, "Hero", 1, Position.BB, new ChipAmount(1000)),
                new PlayerSeat(villain, "Villain", 2, Position.BTN, new ChipAmount(1000))
            },
            new ChipAmount(5),
            new ChipAmount(10));

        state = state.Apply(new BettingAction(Street.Preflop, villain, ActionType.Call, ChipAmount.Zero));

        var ctx = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("AsAh"),
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [hero] = Position.BB,
                [villain] = Position.BTN
            },
            ActionHistory =
            [
                new BettingAction(Street.Preflop, villain, ActionType.Call, ChipAmount.Zero)
            ]
        };

        var engine = new MonteCarloStrategyEngine();
        var rec = engine.Recommend(state, ctx);

        Assert.NotEmpty(rec.RankedActions);
        Assert.Equal(ActionType.Bet, rec.RankedActions[0].Type);
        Assert.Contains("preflop policy", rec.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recommend_PreflopPolicy_RecordsCalibrationSample()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            new[]
            {
                new PlayerSeat(hero, "Hero", 1, Position.BB, new ChipAmount(1000)),
                new PlayerSeat(villain, "Villain", 2, Position.BTN, new ChipAmount(1000))
            },
            new ChipAmount(5),
            new ChipAmount(10));

        state = state.Apply(new BettingAction(Street.Preflop, villain, ActionType.Call, ChipAmount.Zero));

        var ctx = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("KdQd"),
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [hero] = Position.BB,
                [villain] = Position.BTN
            },
            ActionHistory =
            [
                new BettingAction(Street.Preflop, villain, ActionType.Call, ChipAmount.Zero)
            ]
        };

        var engine = new MonteCarloStrategyEngine();
        _ = engine.Recommend(state, ctx);

        var samples = MonteCarloStrategyEngine.PreflopCalibrationLog.Snapshot();
        Assert.NotEmpty(samples);
        Assert.Contains(samples, s => s.Spot == MonteCarloStrategyEngine.PreflopSpot.BigBlindOptionVsLimp);
    }

}
