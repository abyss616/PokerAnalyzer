using System.Threading;
using System.Threading.Tasks;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Microsoft.Extensions.DependencyInjection;
using PokerAnalyzer.Infrastructure;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.PreflopSolver;
using Xunit;
using SolverSizingConfig = PokerAnalyzer.Infrastructure.PreflopSolver.PreflopSizingConfig;

namespace PokerAnalyzer.Application.Tests;

public class CfrPlusPreflopSolverTests
{
    private static readonly RakeConfig Rake = new(0.05m, 1m, true);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task TreeBuilds_UnopenedFirstIn_ForSupportedPlayerCounts(int playerCount)
    {
        var builder = new PreflopGameTreeBuilder(playerCount, 100m, 0.5m, 1m, Rake, SolverSizingConfig.Default);
        var nodes = builder.Build();

        var firstActor = PreflopGameTreeBuilder.GetTablePositions(playerCount)[0];
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature == "UNOPENED" && n.InfoSet.ActingPosition == firstActor);
    }

    [Fact]
    public async Task HeadsUp_ActionOrder_IsBtnThenBb()
    {
        var nodes = new PreflopGameTreeBuilder(2, 100m, 0.5m, 1m, Rake, SolverSizingConfig.Default).Build();
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature == "UNOPENED" && n.InfoSet.ActingPosition == Position.BTN);
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature.StartsWith("VS_OPEN_", StringComparison.Ordinal) && n.InfoSet.ActingPosition == Position.BB);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task TreeContains_VsOpen_Vs3Bet_Vs4Bet_AndAllIn(int playerCount)
    {
        var nodes = new PreflopGameTreeBuilder(playerCount, 100m, 0.5m, 1m, Rake, SolverSizingConfig.Default).Build();
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature is "UNOPENED" or "OPEN");
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature.Contains("3BET", StringComparison.Ordinal));
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature.Contains("4BET", StringComparison.Ordinal));
        Assert.Contains(nodes, n => n.LegalActions.Contains(ActionType.AllIn));
    }

    [Fact]
    public async Task SolverCache_SolvesOnce_PerConfiguration()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cache = new PreflopSolverCache(solver);
        var config = new PreflopSolverConfig(30, 100m, Rake, 6, RaiseSizingAbstraction.Default);

        var first = await cache.GetOrSolveAsync(config, CancellationToken.None);
        var second = await cache.GetOrSolveAsync(config, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, cache.SolveCount);
    }

    [Fact]
    public async Task Engine_UsesSolverWhenSupported_AndProvidesReferenceSeparately()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(hero, "Hero", 1, Position.BTN, new ChipAmount(10000)),
                new PlayerSeat(villain, "Villain", 2, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(5),
            new ChipAmount(10));

        var engine = new CfrPlusPreflopStrategyEngine(
            new MonteCarloStrategyEngine(),
            new PreflopSolverCache(new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()))),
            new PreflopSolverConfig(40, 100m, Rake, 2, RaiseSizingAbstraction.Default),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance);

        var rec = await engine.RecommendAsync(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("AsAh"),
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.BTN, [villain] = Position.BB },
            ActionHistory = []
        });

        var extracted = PreflopStateExtractor.TryExtract(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("AsAh"),
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.BTN, [villain] = Position.BB },
            ActionHistory = []
        }, SolverSizingConfig.Default);

        Assert.NotNull(extracted);
        Assert.Equal(2, extracted!.Key.PlayerCount);
        Assert.Equal(Position.BTN, extracted.Key.ActingPosition);
        Assert.Equal("UNOPENED", extracted.Key.HistorySignature);
        Assert.Equal(1, extracted.Key.ToCallBb);

        var normalized = extracted.Key with { PlayerCount = 2, EffectiveStackBb = 100 };
        var query = await engine.QueryStrategyAsync(normalized, "AsAh");
        Assert.NotEmpty(query.ActionFrequencies);

        Assert.NotNull(rec.PrimaryEV);
        Assert.NotNull(rec.ReferenceEV);
        Assert.NotEmpty(rec.RankedActions);
    }

    [Fact]
    public async Task StateExtractor_HeadsUpSmallBlindAlias_MapsToButtonAndRoundsToCallUp()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(hero, "Hero", 1, Position.BTN, new ChipAmount(10000)),
                new PlayerSeat(villain, "Villain", 2, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(5),
            new ChipAmount(10));

        var extraction = PreflopStateExtractor.TryExtract(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.SB, [villain] = Position.BB },
            ActionHistory = []
        }, SolverSizingConfig.Default);

        Assert.NotNull(extraction);
        Assert.Equal(Position.BTN, extraction!.Key.ActingPosition);
        Assert.Equal(1, extraction.Key.ToCallBb);
    }

    [Fact]
    public async Task StateExtractor_ProducesUnifiedInfoSetKey()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(hero, "Hero", 1, Position.UTG, new ChipAmount(10000)),
                new PlayerSeat(villain, "Villain", 2, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(5),
            new ChipAmount(10));

        var extraction = PreflopStateExtractor.TryExtract(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.UTG, [villain] = Position.BB },
            ActionHistory = []
        }, SolverSizingConfig.Default);

        Assert.NotNull(extraction);
        Assert.Equal("UNOPENED", extraction!.Key.HistorySignature);
    }

    [Fact]
  
    public void StateExtractor_SbFacingBtnOpen_UsesFacingOpenHistorySignature()
    {
        var btn = PlayerId.New();
        var hero = PlayerId.New();
        var bb = PlayerId.New();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(btn,  "BTN",  1, Position.BTN, new ChipAmount(10000)),
            new PlayerSeat(hero, "Hero", 2, Position.SB,  new ChipAmount(10000)),
            new PlayerSeat(bb,   "BB",   3, Position.BB,  new ChipAmount(10000))
            ],
            new ChipAmount(5),   // SB
            new ChipAmount(10))  // BB
                                 // BTN opens to 25 (2.5bb)
            .Apply(new BettingAction(Street.Preflop, btn, ActionType.Raise, new ChipAmount(25)));

        var ctx = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [btn] = Position.BTN,
                [hero] = Position.SB,
                [bb] = Position.BB
            },
            ActionHistory =
            [
                new BettingAction(Street.Preflop, hero, ActionType.PostSmallBlind, new ChipAmount(5)),
            new BettingAction(Street.Preflop, bb,   ActionType.PostBigBlind,   new ChipAmount(10)),
            new BettingAction(Street.Preflop, btn,  ActionType.Raise,          new ChipAmount(25))
            ]
        };

        var extraction = PreflopStateExtractor.TryExtract(state, ctx, SolverSizingConfig.Default);

        Assert.NotNull(extraction);

        // SB is now to act facing the open
        Assert.Equal(Position.SB, extraction!.Key.ActingPosition);

        // Exact math: after posting 0.5bb, facing a 2.5bb open -> to call 2.0bb
        // (raise to 25, current bet=25, SB contrib=5 => 20 chips; 20 / 10 = 2bb)
        Assert.Equal(2m, extraction.Key.ToCallBb);

        Assert.Equal("VS_OPEN", extraction.Key.HistorySignature);
        Assert.NotEqual("OPEN", extraction.Key.HistorySignature);
    }

    [Fact]
   
    public void StateExtractor_SbFacingBtn4Bet_UsesFacing4BetHistorySignature()
    {
        var btn = PlayerId.New();
        var hero = PlayerId.New();
        var bb = PlayerId.New();

        // Blinds: 5/10. Replicate the KK line:
        // BTN open 30 (3bb), SB 3bet 100 (10bb), BTN 4bet 230 (23bb),
        // now SB to act facing the 4bet.
        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(btn,  "BTN",  1, Position.BTN, new ChipAmount(10000)),
            new PlayerSeat(hero, "Hero", 2, Position.SB,  new ChipAmount(10000)),
            new PlayerSeat(bb,   "BB",   3, Position.BB,  new ChipAmount(10000))
            ],
            new ChipAmount(5),
            new ChipAmount(10))
            .Apply(new BettingAction(Street.Preflop, btn, ActionType.Raise, new ChipAmount(30)))   // open to 3bb
            .Apply(new BettingAction(Street.Preflop, hero, ActionType.Raise, new ChipAmount(100)))  // 3bet to 10bb
            .Apply(new BettingAction(Street.Preflop, btn, ActionType.Raise, new ChipAmount(230))); // 4bet to 23bb

        var ctx = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [btn] = Position.BTN,
                [hero] = Position.SB,
                [bb] = Position.BB
            },
            ActionHistory =
            [
                new BettingAction(Street.Preflop, hero, ActionType.PostSmallBlind, new ChipAmount(5)),
            new BettingAction(Street.Preflop, bb,   ActionType.PostBigBlind,   new ChipAmount(10)),
            new BettingAction(Street.Preflop, btn,  ActionType.Raise,          new ChipAmount(30)),
            new BettingAction(Street.Preflop, hero, ActionType.Raise,          new ChipAmount(100)),
            new BettingAction(Street.Preflop, btn,  ActionType.Raise,          new ChipAmount(230))
            ]
        };

        var extraction = PreflopStateExtractor.TryExtract(state, ctx, SolverSizingConfig.Default);

        Assert.NotNull(extraction);

        // Hero (SB) is to act facing a 4bet
        Assert.Equal(Position.SB, extraction!.Key.ActingPosition);

        // Current bet is 230; SB already put in 100 => to call 130 chips => 13bb
        Assert.Equal(13m, extraction.Key.ToCallBb);

        Assert.Equal("VS_4BET", extraction.Key.HistorySignature);
        Assert.NotEqual("OPEN", extraction.Key.HistorySignature);
        Assert.NotEqual("VS_OPEN", extraction.Key.HistorySignature);
    }

    [Fact]
    public void StateExtractor_UnopenedSmallBlindOption_UsesOpenSignature()
    {
        var hero = PlayerId.New();
        var bb = PlayerId.New();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(hero, "Hero", 1, Position.SB, new ChipAmount(10000)),
                new PlayerSeat(bb, "BB", 2, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(0),
            new ChipAmount(0));

        var extraction = PreflopStateExtractor.TryExtract(state, new HeroContext(hero, new ChipAmount(0), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [hero] = Position.SB,
                [bb] = Position.BB
            },
            ActionHistory = []
        }, SolverSizingConfig.Default);

        Assert.NotNull(extraction);
        Assert.Equal("OPEN", extraction!.Key.HistorySignature);
        Assert.DoesNotContain("VS_OPEN", extraction.Key.HistorySignature, StringComparison.Ordinal);
    }

    [Fact]
    public void PreflopKeyValidator_FailsFast_WhenOpenSignatureHasPositiveToCall()
    {
        var btn = PlayerId.New();
        var hero = PlayerId.New();
        var bb = PlayerId.New();
        var validator = new PreflopKeyValidator();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(btn, "BTN", 1, Position.BTN, new ChipAmount(10000)),
                new PlayerSeat(hero, "Hero", 2, Position.SB, new ChipAmount(10000)),
                new PlayerSeat(bb, "BB", 3, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(5),
            new ChipAmount(10))
            .Apply(new BettingAction(Street.Preflop, btn, ActionType.Raise, new ChipAmount(25)));

        var heroContext = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [btn] = Position.BTN,
                [hero] = Position.SB,
                [bb] = Position.BB
            },
            ActionHistory =
            [
                new BettingAction(Street.Preflop, hero, ActionType.PostSmallBlind, new ChipAmount(5)),
                new BettingAction(Street.Preflop, bb, ActionType.PostBigBlind, new ChipAmount(10)),
                new BettingAction(Street.Preflop, btn, ActionType.Raise, new ChipAmount(25))
            ]
        };

        var extraction = PreflopStateExtractor.TryExtract(state, heroContext, SolverSizingConfig.Default);
        Assert.NotNull(extraction);

        var forcedOpenKey = extraction!.Key with { HistorySignature = "OPEN" };
        var validation = validator.Validate(forcedOpenKey, extraction.SpotContext, state, heroContext);

        Assert.False(validation.IsValid);
        Assert.Contains("OPEN", validation.Reason, StringComparison.Ordinal);
        Assert.True(validation.Context.ToCallBb > 0);
    }

    [Fact]
    public void PreflopKeyValidator_FailsFast_WhenVsOpenSignatureHasZeroToCall()
    {
        var hero = PlayerId.New();
        var bb = PlayerId.New();
        var validator = new PreflopKeyValidator();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(hero, "Hero", 1, Position.SB, new ChipAmount(10000)),
                new PlayerSeat(bb, "BB", 2, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(0),
            new ChipAmount(0));

        var heroContext = new HeroContext(hero, new ChipAmount(0), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [hero] = Position.SB,
                [bb] = Position.BB
            },
            ActionHistory = []
        };

        var extraction = PreflopStateExtractor.TryExtract(state, heroContext, SolverSizingConfig.Default);
        Assert.NotNull(extraction);

        var forcedVsOpen = extraction!.Key with { HistorySignature = "VS_OPEN" };
        var validation = validator.Validate(forcedVsOpen, extraction.SpotContext, state, heroContext);

        Assert.False(validation.IsValid);
        Assert.Contains("VS_OPEN", validation.Reason, StringComparison.Ordinal);
        Assert.Equal(0m, validation.Context.ToCallBb);
    }


    [Fact]
    public void PreflopKeyValidator_FailsFast_WhenVs3BetSignatureHasInsufficientRaiseDepth()
    {
        var hero = PlayerId.New();
        var btn = PlayerId.New();
        var bb = PlayerId.New();
        var validator = new PreflopKeyValidator();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(btn, "BTN", 1, Position.BTN, new ChipAmount(10000)),
                new PlayerSeat(hero, "Hero", 2, Position.SB, new ChipAmount(10000)),
                new PlayerSeat(bb, "BB", 3, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(5),
            new ChipAmount(10))
            .Apply(new BettingAction(Street.Preflop, btn, ActionType.Raise, new ChipAmount(25)));

        var heroContext = new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [btn] = Position.BTN,
                [hero] = Position.SB,
                [bb] = Position.BB
            },
            ActionHistory =
            [
                new BettingAction(Street.Preflop, hero, ActionType.PostSmallBlind, new ChipAmount(5)),
                new BettingAction(Street.Preflop, bb, ActionType.PostBigBlind, new ChipAmount(10)),
                new BettingAction(Street.Preflop, btn, ActionType.Raise, new ChipAmount(25))
            ]
        };

        var extraction = PreflopStateExtractor.TryExtract(state, heroContext, SolverSizingConfig.Default);
        Assert.NotNull(extraction);

        var forcedVsThreeBet = extraction!.Key with { HistorySignature = "VS_3BET" };
        var validation = validator.Validate(forcedVsThreeBet, extraction.SpotContext, state, heroContext);

        Assert.False(validation.IsValid);
        Assert.Contains("RaiseDepth", validation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PreflopKeyValidator_Passes_ForUnopenedSmallBlindOpenSpot()
    {
        var hero = PlayerId.New();
        var bb = PlayerId.New();
        var validator = new PreflopKeyValidator();

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(hero, "Hero", 1, Position.SB, new ChipAmount(10000)),
                new PlayerSeat(bb, "BB", 2, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(0),
            new ChipAmount(0));

        var heroContext = new HeroContext(hero, new ChipAmount(0), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [hero] = Position.SB,
                [bb] = Position.BB
            },
            ActionHistory = []
        };

        var extraction = PreflopStateExtractor.TryExtract(state, heroContext, SolverSizingConfig.Default);
        Assert.NotNull(extraction);

        var validation = validator.Validate(extraction!.Key, extraction.SpotContext, state, heroContext);

        Assert.True(validation.IsValid);
        Assert.Equal("OPEN", validation.Context.HistorySignature);
        Assert.Equal(0m, validation.Context.ToCallBb);
    }


    [Fact]
    public async Task Engine_FailsFastValidation_DoesNotSolveOrQuery_WhenActingPositionUnknown()
    {
        var hero = PlayerId.New();
        var bb = PlayerId.New();
        var cache = new PreflopSolverCache(new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider())));
        var engine = new CfrPlusPreflopStrategyEngine(
            new MonteCarloStrategyEngine(),
            cache,
            new PreflopSolverConfig(30, 50m, Rake, 2, RaiseSizingAbstraction.Default),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance);

        var state = HandState.CreateNewHand(
            [
                new PlayerSeat(hero, "Hero", 1, Position.SB, new ChipAmount(10000)),
                new PlayerSeat(bb, "BB", 2, Position.BB, new ChipAmount(10000))
            ],
            new ChipAmount(0),
            new ChipAmount(0));

        var recommendation = await engine.RecommendAsync(state, new HeroContext(hero, new ChipAmount(0), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("AsAh"),
            PlayerPositions = new Dictionary<PlayerId, Position>
            {
                [hero] = Position.Unknown,
                [bb] = Position.BB
            },
            ActionHistory = []
        });

        Assert.Empty(recommendation.RankedActions);
        Assert.Contains("ActingPosition is Unknown", recommendation.PrimaryExplanation, StringComparison.Ordinal);
        Assert.Equal(0, cache.SolveCount);
    }

    [Fact]
    public async Task PreflopSizingNormalizer_OpenSize_BucketsToNearestConfiguredSize()
    {
        var normalizer = new PreflopSizingNormalizer();
        var sizing = new SolverSizingConfig(OpenSizesBb: [2.0m, 2.5m], ThreeBetSizeMultipliers: [3.0m], FourBetSizeMultipliers: [2.2m]);

        var normalized = normalizer.Normalize(Street.Preflop, PreflopSizingActionType.Open, 3.0m, 2.0m, 1.0m, 0m, sizing);

        Assert.Equal(2.5m, normalized.NormalizedSizeBb);
        Assert.Equal(2, normalized.NormalizedToCallBb);
        Assert.Contains("3", normalized.NormalizationNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolverCache_Lookup_ReturnsNormalizedMix_ForHeadsUpBbVsLimp()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cache = new PreflopSolverCache(solver);
        var config = new PreflopSolverConfig(
            Iterations: 80,
            EffectiveStackBb: 100m,
            Rake: Rake,
            PlayerCount: 2,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 8);

        await cache.GetOrSolveAsync(config, CancellationToken.None);

        var key = new PreflopInfoSetKey(2, Position.BB, "LIMPED", 0, 100);
        var query = cache.Lookup(key, "AsKh");

        Assert.NotEmpty(query.ActionFrequencies);
        var sum = query.ActionFrequencies.Values.Sum();
        Assert.InRange(sum, 0.999, 1.001);
        Assert.NotNull(query.BestAction);
    }


    [Fact]
    public async Task SolverCache_Lookup_RelaxesButtonSmallBlindAlias_WhenNotExactStateOnly()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cache = new PreflopSolverCache(solver);
        var config = new PreflopSolverConfig(
            Iterations: 80,
            EffectiveStackBb: 100m,
            Rake: Rake,
            PlayerCount: 2,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 8);

        await cache.GetOrSolveAsync(config, CancellationToken.None);

        var key = new PreflopInfoSetKey(2, Position.SB, "UNOPENED", 1, 100);
        var query = cache.Lookup(key, "KhKd");

        Assert.True(query.Supported);
        Assert.NotEmpty(query.ActionFrequencies);
        Assert.Equal(Position.BTN, query.InfoSet.ActingPosition);
    }

    [Fact]
    public async Task SolverCache_Lookup_ExactStateOnly_DoesNotUseHistoryOrNearestFallback()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cache = new PreflopSolverCache(solver);
        var config = new PreflopSolverConfig(
            Iterations: 80,
            EffectiveStackBb: 100m,
            Rake: Rake,
            PlayerCount: 2,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 8);

        await cache.GetOrSolveAsync(config, CancellationToken.None);

        var key = new PreflopInfoSetKey(2, Position.BB, "VS_OPEN_2.5", 2, 100);
        var nonExact = cache.Lookup(key, "AsKh");
        var exact = cache.Lookup(key, "AsKh", exactStateOnly: true);

        Assert.True(nonExact.Supported);
        Assert.False(exact.Supported);
        Assert.Contains("No solved strategy", exact.UnsupportedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Solver_ProducesNormalizedStrategies_OnTinyDepthTree()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var config = new PreflopSolverConfig(
            Iterations: 50,
            EffectiveStackBb: 20m,
            Rake: Rake,
            PlayerCount: 2,
            Sizing: new RaiseSizingAbstraction([2m], [6m], [14m], 20m),
            MaxTreeDepth: 3);

        var solved = solver.SolvePreflop(config);

        Assert.NotEmpty(solved.NodeStrategies);
        Assert.Contains(solved.NodeStrategies.Values, node => node.PopulationMix.Count > 0);

        foreach (var node in solved.NodeStrategies.Values.Where(n => n.PopulationMix.Count > 0))
        {
            var sum = node.PopulationMix.Values.Sum();
            Assert.InRange(sum, 0.999, 1.001);
        }
    }


    [Fact]
    public async Task Solver_LimpedBbVsBtnOpen_HasStableBestAction()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var config = new PreflopSolverConfig(
            Iterations: 80,
            EffectiveStackBb: 100m,
            Rake: Rake,
            PlayerCount: 2,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 8);

        var solved = solver.SolvePreflop(config);
        var key = new PreflopInfoSetKey(2, Position.BB, "LIMPED", 0, 100);
        var query = solver.QueryStrategy(solved, key, "AsKh");

        Assert.True(query.Supported);
        Assert.InRange(query.ActionFrequencies.Values.Sum(), 0.999, 1.001);
        Assert.NotNull(query.BestAction);
    }

    [Fact]
    public async Task EngineQueryStrategy_ReturnsUnsupported_WhenHeroHandClassMissing()
    {
        var engine = new CfrPlusPreflopStrategyEngine(
            new MonteCarloStrategyEngine(),
            new PreflopSolverCache(new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()))),
            new PreflopSolverConfig(30, 50m, Rake, 2, RaiseSizingAbstraction.Default),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance);

        var query = await engine.QueryStrategyAsync(new PreflopInfoSetKey(2, Position.BTN, "UNOPENED", 1, 50), string.Empty);

        Assert.False(query.Supported);
        Assert.Contains("Missing hero hand class", query.UnsupportedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(query.ActionFrequencies);
    }

    [Fact]
    public async Task EngineQueryStrategy_ReturnsUnsupported_WhenHandConditionedKeyMisses()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cache = new PreflopSolverCache(solver);
        var config = new PreflopSolverConfig(
            Iterations: 30,
            EffectiveStackBb: 40m,
            Rake: Rake,
            PlayerCount: 2,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 4);
        await cache.GetOrSolveAsync(config, CancellationToken.None);

        var engine = new CfrPlusPreflopStrategyEngine(
            new MonteCarloStrategyEngine(),
            cache,
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance);

        var missingKey = new PreflopInfoSetKey(2, Position.UTG, "UNOPENED", 1, 40);
        var query = await engine.QueryStrategyAsync(missingKey, "2c7d");

        Assert.False(query.Supported);
        Assert.Equal("No solved strategy for key (did you change key format? clear cache / rerun solve).", query.UnsupportedReason);
        Assert.Empty(query.ActionFrequencies);
    }

    [Fact]
    public async Task AddPokerAnalyzer_ResolvesCfrEngine_WithSixMaxRecommendationAndFiniteEv()
    {
        var services = new ServiceCollection();
        services.AddPokerAnalyzer();
        using var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<PreflopSolverConfig>();
        Assert.Equal(6, config.PlayerCount);

        var engine = provider.GetRequiredService<IStrategyEngine>();
        var hero = PlayerId.New();
        var players = new[]
        {
            new { Id = hero, Name = "Hero", Seat = 1, Position = Position.UTG },
            new { Id = PlayerId.New(), Name = "CO", Seat = 2, Position = Position.CO },
            new { Id = PlayerId.New(), Name = "BTN", Seat = 3, Position = Position.BTN },
            new { Id = PlayerId.New(), Name = "SB", Seat = 4, Position = Position.SB },
            new { Id = PlayerId.New(), Name = "BB", Seat = 5, Position = Position.BB },
            new { Id = PlayerId.New(), Name = "HJ", Seat = 6, Position = Position.HJ }
        };

        var state = HandState.CreateNewHand(
            players.Select(p => new PlayerSeat(p.Id, p.Name, p.Seat, p.Position, new ChipAmount(10_000))).ToList(),
            new ChipAmount(5),
            new ChipAmount(10));

        var recommendation = await engine.RecommendAsync(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("Ah5h"),
            PlayerPositions = players.ToDictionary(p => p.Id, p => p.Position),
            ActionHistory = []
        });

        Assert.NotEmpty(recommendation.RankedActions);
        Assert.NotNull(recommendation.PrimaryAction);
        Assert.NotNull(recommendation.PrimaryEV);
        Assert.True(double.IsFinite((double)recommendation.PrimaryEV!.Value));
    }


    [Fact]
    public async Task DecisionPointEv_UsesReachWeightedAverage_InHeadsUp()
    {
        var highUtilityReach = CfrPlusPreflopSolver.ComputeInfosetReachWeight([1d, 0.1d]);
        var lowUtilityReach = CfrPlusPreflopSolver.ComputeInfosetReachWeight([1d, 1d]);

        var reachWeightedUtilitySum = (10d * highUtilityReach) + (-1d * lowUtilityReach);
        var reachProbabilitySum = highUtilityReach + lowUtilityReach;

        var decisionPointEv = CfrPlusPreflopSolver.ComputeDecisionPointEvBb(reachWeightedUtilitySum, reachProbabilitySum);
        var naiveEstimatedEv = (10m + (-1m)) / 2m;

        Assert.Equal(0m, decisionPointEv);
        Assert.NotEqual(naiveEstimatedEv, decisionPointEv);
    }

    [Fact]
    public async Task InfosetReachWeight_UsesFullInfosetProbabilityMass_InMultiway()
    {
        var reachWeight = CfrPlusPreflopSolver.ComputeInfosetReachWeight([0.5d, 0.25d, 0.2d]);

        Assert.Equal(0.025d, reachWeight, precision: 10);
    }

    [Fact]
    public async Task DecisionPointEv_IsZero_WhenReachProbabilitySumIsZero()
    {
        var decisionPointEv = CfrPlusPreflopSolver.ComputeDecisionPointEvBb(reachWeightedUtilitySum: 123d, reachProbabilitySum: 0d);

        Assert.Equal(0m, decisionPointEv);
    }

    [Fact]
    public async Task UnconditionalContribution_MatchesReachWeightedUtilitySum()
    {
        var contribution = CfrPlusPreflopSolver.ComputeUnconditionalContributionBb(reachWeightedUtilitySum: -4.25d);

        Assert.Equal(-4.25m, contribution);
    }

    [Fact]
    public async Task InfosetReachWeight_IncludesChanceMultiplierWhenEmbeddedInPlayerReach()
    {
        const double heroChance = 0.4d;
        var reachWeight = CfrPlusPreflopSolver.ComputeInfosetReachWeight([heroChance, 0.5d]);

        Assert.Equal(0.2d, reachWeight, precision: 10);
    }

    [Fact]
    public async Task Solver_TerminalCacheAndUncached_ProduceEquivalentStrategies()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cachedConfig = new PreflopSolverConfig(
            Iterations: 40,
            EffectiveStackBb: 30m,
            Rake: Rake,
            PlayerCount: 2,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 5,
            EnableTerminalCache: true);
        var uncachedConfig = cachedConfig with { EnableTerminalCache = false };

        var cached = solver.SolvePreflop(cachedConfig);
        var uncached = solver.SolvePreflop(uncachedConfig);

        Assert.Equal(uncached.NodeStrategies.Count, cached.NodeStrategies.Count);

        foreach (var (key, uncachedNode) in uncached.NodeStrategies)
        {
            Assert.True(cached.NodeStrategies.TryGetValue(key, out var cachedNode));
            Assert.InRange(Math.Abs(cachedNode.DecisionPointEvBb - uncachedNode.DecisionPointEvBb), 0m, 0.0001m);

            foreach (var action in uncachedNode.PopulationMix.Keys)
            {
                Assert.True(cachedNode.PopulationMix.ContainsKey(action));
                Assert.InRange(Math.Abs(cachedNode.PopulationMix[action] - uncachedNode.PopulationMix[action]), 0d, 0.0001d);
            }
        }
    }

    [Fact]
    public async Task Solver_TerminalCache_WorksForParallelAndSingleThreadSolveModes()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var singleThreadConfig = new PreflopSolverConfig(
            Iterations: 35,
            EffectiveStackBb: 20m,
            Rake: Rake,
            PlayerCount: 3,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 4,
            EnableParallelSolve: false,
            EnableTerminalCache: true);
        var parallelConfig = singleThreadConfig with { EnableParallelSolve = true, MaxDegreeOfParallelism = 2 };

        var singleThread = solver.SolvePreflop(singleThreadConfig);
        var parallel = solver.SolvePreflop(parallelConfig);

        Assert.NotEmpty(singleThread.NodeStrategies);
        Assert.NotEmpty(parallel.NodeStrategies);

        foreach (var node in singleThread.NodeStrategies.Values.Concat(parallel.NodeStrategies.Values))
        {
            if (node.PopulationMix.Count == 0)
                continue;

            Assert.InRange(node.PopulationMix.Values.Sum(), 0.999, 1.001);
        }
    }

    [Fact]
    public async Task Solver_ParallelLocalMergeMode_StaysCloseToSingleThread()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var singleThreadConfig = new PreflopSolverConfig(
            Iterations: 45,
            EffectiveStackBb: 25m,
            Rake: Rake,
            PlayerCount: 2,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 4,
            EnableParallelSolve: false,
            EnableTerminalCache: true);
        var parallelConfig = singleThreadConfig with { EnableParallelSolve = true, MaxDegreeOfParallelism = 2 };

        var singleThread = solver.SolvePreflop(singleThreadConfig);
        var parallel = solver.SolvePreflop(parallelConfig);

        Assert.Equal(singleThread.NodeStrategies.Count, parallel.NodeStrategies.Count);

        foreach (var (key, singleNode) in singleThread.NodeStrategies)
        {
            Assert.True(parallel.NodeStrategies.TryGetValue(key, out var parallelNode));

            if (singleNode.PopulationMix.Count > 0)
                Assert.InRange(singleNode.PopulationMix.Values.Sum(), 0.999, 1.001);

            if (parallelNode.PopulationMix.Count > 0)
                Assert.InRange(parallelNode.PopulationMix.Values.Sum(), 0.999, 1.001);

            foreach (var (action, singleFrequency) in singleNode.PopulationMix)
            {
                Assert.True(parallelNode.PopulationMix.ContainsKey(action));
                Assert.InRange(Math.Abs(parallelNode.PopulationMix[action] - singleFrequency), 0d, 0.06d);
            }

            Assert.InRange(Math.Abs(parallelNode.DecisionPointEvBb - singleNode.DecisionPointEvBb), 0m, 0.20m);
        }
    }

    [Fact]
    public async Task Solver_ParallelMergePathWithOneWorker_DoesNotLoseInfosets()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var singleThreadConfig = new PreflopSolverConfig(
            Iterations: 30,
            EffectiveStackBb: 30m,
            Rake: Rake,
            PlayerCount: 3,
            Sizing: RaiseSizingAbstraction.Default,
            MaxTreeDepth: 4,
            EnableParallelSolve: false,
            EnableTerminalCache: true);
        var mergePathConfig = singleThreadConfig with { EnableParallelSolve = true, MaxDegreeOfParallelism = 1 };

        var singleThread = solver.SolvePreflop(singleThreadConfig);
        var mergePath = solver.SolvePreflop(mergePathConfig);

        Assert.Equal(singleThread.NodeStrategies.Count, mergePath.NodeStrategies.Count);
        Assert.True(mergePath.NodeStrategies.Keys.All(singleThread.NodeStrategies.ContainsKey));
    }


    [Fact]
    public async Task SolverCache_Cancellation_CancelsWaiterWithoutCancelingSharedSolve()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cache = new PreflopSolverCache(solver);
        var config = new PreflopSolverConfig(300, 100m, Rake, 6, RaiseSizingAbstraction.Default);

        var sharedSolve = cache.GetOrSolveAsync(config, CancellationToken.None);
        await Task.Delay(25);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.GetOrSolveAsync(config, cts.Token));

        var solved = await sharedSolve;
        var cached = await cache.GetOrSolveAsync(config, CancellationToken.None);
        Assert.Same(solved, cached);
        Assert.Equal(1, cache.SolveCount);
    }

    [Fact]
    public async Task SolverCache_ConcurrentCallers_ShareSingleSolve()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cache = new PreflopSolverCache(solver);
        var config = new PreflopSolverConfig(80, 100m, Rake, 2, RaiseSizingAbstraction.Default);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => cache.GetOrSolveAsync(config, CancellationToken.None))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, cache.SolveCount);
        Assert.True(tasks.Skip(1).All(task => ReferenceEquals(tasks[0].Result, task.Result)));
    }

    [Fact]
    public async Task SolverCache_MaxEntries_EvictsLeastRecentlyUsedCompletedEntries()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var cache = new PreflopSolverCache(
            solver,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PreflopSolverCache>.Instance,
            new PreflopSolverCacheOptions { MaxEntries = 2, Ttl = TimeSpan.FromHours(1), TrimInterval = TimeSpan.Zero },
            clock);

        var config1 = new PreflopSolverConfig(10, 30m, Rake, 2, RaiseSizingAbstraction.Default, MaxTreeDepth: 3);
        var config2 = config1 with { EffectiveStackBb = 40m };
        var config3 = config1 with { EffectiveStackBb = 50m };

        await cache.GetOrSolveAsync(config1, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        await cache.GetOrSolveAsync(config2, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        await cache.GetOrSolveAsync(config3, CancellationToken.None);

        Assert.True(cache.CacheEntries <= 2);
        Assert.False(cache.ContainsKey(PreflopSolverCache.BuildCacheKey(config1)));
    }

    [Fact]
    public async Task SolverCache_Ttl_EvictsExpiredEntries()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var cache = new PreflopSolverCache(
            solver,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PreflopSolverCache>.Instance,
            new PreflopSolverCacheOptions { MaxEntries = 10, Ttl = TimeSpan.FromMinutes(1), TrimInterval = TimeSpan.Zero },
            clock);

        var oldConfig = new PreflopSolverConfig(10, 30m, Rake, 2, RaiseSizingAbstraction.Default, MaxTreeDepth: 3);
        var freshConfig = oldConfig with { EffectiveStackBb = 35m };

        await cache.GetOrSolveAsync(oldConfig, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        await cache.GetOrSolveAsync(freshConfig, CancellationToken.None);

        Assert.False(cache.ContainsKey(PreflopSolverCache.BuildCacheKey(oldConfig)));
        Assert.True(cache.ContainsKey(PreflopSolverCache.BuildCacheKey(freshConfig)));
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);
    }

}
