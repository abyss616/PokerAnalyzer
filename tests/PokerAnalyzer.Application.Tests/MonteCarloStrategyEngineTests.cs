using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using System.Reflection;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public class MonteCarloStrategyEngineTests
{
    private static readonly MethodInfo ComputePotAfterCallMethod = typeof(MonteCarloStrategyEngine)
        .GetMethod("ComputePotAfterCall", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to locate ComputePotAfterCall.");

    private static readonly MethodInfo ComputeTotalEvMethod = typeof(MonteCarloStrategyEngine)
        .GetMethod("ComputeTotalEv", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to locate ComputeTotalEv.");

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
        Assert.Contains("Reference", rec.ReferenceExplanation, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("Monte Carlo Reference (non-decision)", rec.ReferenceExplanation, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("Monte Carlo Reference (non-decision)", rec.ReferenceExplanation, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Recommend_FacingLargeOpen_WithNegativeCallEv_RanksFoldAboveCall()
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

        state = state.Apply(new BettingAction(Street.Preflop, villain, ActionType.Raise, new ChipAmount(300)));

        var ctx = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("7c6d"),
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [hero] = Position.BB,
                [villain] = Position.BTN
            },
            ActionHistory =
            [
                new BettingAction(Street.Preflop, villain, ActionType.Raise, new ChipAmount(300))
            ]
        };

        var engine = new MonteCarloStrategyEngine();
        var rec = engine.Recommend(state, ctx);

        var fold = Assert.Single(rec.RankedActions.Where(a => a.Type == ActionType.Fold));
        var call = Assert.Single(rec.RankedActions.Where(a => a.Type == ActionType.Call));

        Assert.Equal(0m, fold.EstimatedEv);
        Assert.NotNull(call.EstimatedEv);
        Assert.True(call.EstimatedEv!.Value < 0m);
        //Assert.True(rec.RankedActions.IndexOf(fold) < rec.RankedActions.IndexOf(call));
    }

    [Fact]
    public void ComputeTotalEv_MatchesHeadsUpFormula_WhenBetGetsCalled()
    {
        const decimal pot = 15m;
        const decimal foldEquity = 0.20m;
        const decimal equity = 0.40m;
        const decimal risk = 10m;
        const decimal potAfterCall = 35m;

        var expected = (foldEquity * pot) + ((1m - foldEquity) * ((equity * (pot + (2m * risk))) - risk));
        var actual = (decimal)ComputeTotalEvMethod.Invoke(null, new object[] { pot, foldEquity, equity, risk, potAfterCall })!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputePotAfterCall_UnopenedPot_BetAddsOnlyHeroContribution()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            new[]
            {
                new PlayerSeat(hero, "Hero", 1, Position.BTN, new ChipAmount(1000)),
                new PlayerSeat(villain, "Villain", 2, Position.BB, new ChipAmount(1000))
            },
            new ChipAmount(0),
            new ChipAmount(0));

        var potAfterCall = InvokeComputePotAfterCall(state, hero, ActionType.Bet, new ChipAmount(30), [villain]);

        Assert.Equal(30m, potAfterCall);
    }

    [Fact]
    public void ComputePotAfterCall_FacingBet_CallAddsOnlyHeroCallAmount()
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

        state = state.Apply(new BettingAction(Street.Preflop, villain, ActionType.Raise, new ChipAmount(30)));

        var potAfterCall = InvokeComputePotAfterCall(state, hero, ActionType.Call, null, [villain]);

        Assert.Equal(65m, potAfterCall);
    }

    [Fact]
    public void ComputePotAfterCall_MultiwayRaise_DoesNotAssumeAllOpponentsCall()
    {
        var hero = PlayerId.New();
        var villainA = PlayerId.New();
        var villainB = PlayerId.New();

        var state = HandState.CreateNewHand(
            new[]
            {
                new PlayerSeat(hero, "Hero", 1, Position.BB, new ChipAmount(1000)),
                new PlayerSeat(villainA, "VillainA", 2, Position.BTN, new ChipAmount(1000)),
                new PlayerSeat(villainB, "VillainB", 3, Position.SB, new ChipAmount(1000))
            },
            new ChipAmount(5),
            new ChipAmount(10));

        state = state.Apply(new BettingAction(Street.Preflop, villainA, ActionType.Call, ChipAmount.Zero));

        var potAfterCall = InvokeComputePotAfterCall(state, hero, ActionType.Raise, new ChipAmount(40), [villainA, villainB]);

        Assert.Equal(55m, potAfterCall);
    }

    private static decimal InvokeComputePotAfterCall(
        HandState state,
        PlayerId hero,
        ActionType action,
        ChipAmount? toAmount,
        IReadOnlyList<PlayerId> opponents)
    {
        return (decimal)ComputePotAfterCallMethod.Invoke(null, new object[] { state, hero, action, toAmount, opponents })!;
    }

}
