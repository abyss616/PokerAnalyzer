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
    public void TreeBuilds_UnopenedFirstIn_ForSupportedPlayerCounts(int playerCount)
    {
        var builder = new PreflopGameTreeBuilder(playerCount, 100m, 0.5m, 1m, Rake, SolverSizingConfig.Default);
        var nodes = builder.Build();

        var firstActor = PreflopGameTreeBuilder.GetTablePositions(playerCount)[0];
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature == "UNOPENED" && n.InfoSet.ActingPosition == firstActor);
    }

    [Fact]
    public void HeadsUp_ActionOrder_IsBtnThenBb()
    {
        var nodes = new PreflopGameTreeBuilder(2, 100m, 0.5m, 1m, Rake, SolverSizingConfig.Default).Build();
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature == "UNOPENED" && n.InfoSet.ActingPosition == Position.BTN);
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature == "OPEN" && n.InfoSet.ActingPosition == Position.BB);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TreeContains_VsOpen_Vs3Bet_Vs4Bet_AndAllIn(int playerCount)
    {
        var nodes = new PreflopGameTreeBuilder(playerCount, 100m, 0.5m, 1m, Rake, SolverSizingConfig.Default).Build();
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature is "OPEN" or "OPEN_CALL");
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature.Contains("3BET", StringComparison.Ordinal));
        Assert.Contains(nodes, n => n.InfoSet.HistorySignature.Contains("4BET", StringComparison.Ordinal));
        Assert.Contains(nodes, n => n.LegalActions.Contains(ActionType.AllIn));
    }

    [Fact]
    public void SolverCache_SolvesOnce_PerConfiguration()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var cache = new PreflopSolverCache(solver);
        var config = new PreflopSolverConfig(30, 100m, Rake, 6, RaiseSizingAbstraction.Default);

        var first = cache.GetOrSolve(config);
        var second = cache.GetOrSolve(config);

        Assert.Same(first, second);
        Assert.Equal(1, cache.SolveCount);
    }

    [Fact]
    public void Engine_UsesSolverWhenSupported_AndProvidesReferenceSeparately()
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

        var rec = engine.Recommend(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
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
        var query = engine.QueryStrategy(normalized, "AsAh");
        Assert.NotEmpty(query.ActionFrequencies);

        Assert.NotNull(rec.PrimaryEV);
        Assert.NotNull(rec.ReferenceEV);
        Assert.NotEmpty(rec.RankedActions);
    }

    [Fact]
    public void StateExtractor_HeadsUpSmallBlindAlias_MapsToButtonAndRoundsToCallUp()
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
    public void StateExtractor_ProducesUnifiedInfoSetKey()
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
    public void PreflopSizingNormalizer_OpenSize_BucketsToNearestConfiguredSize()
    {
        var normalizer = new PreflopSizingNormalizer();
        var sizing = new SolverSizingConfig(OpenSizesBb: [2.0m, 2.5m], ThreeBetSizeMultipliers: [3.0m], FourBetSizeMultipliers: [2.2m]);

        var normalized = normalizer.Normalize(Street.Preflop, PreflopSizingActionType.Open, 3.0m, 2.0m, 1.0m, 0m, sizing);

        Assert.Equal(2.5m, normalized.NormalizedSizeBb);
        Assert.Equal(2, normalized.NormalizedToCallBb);
        Assert.Contains("3", normalized.NormalizationNote, StringComparison.Ordinal);
    }

    [Fact]
    public void SolverCache_Lookup_ReturnsNormalizedMix_ForHeadsUpBbVsLimp()
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

        cache.GetOrSolve(config);

        var key = new PreflopInfoSetKey(2, Position.BB, "LIMPED", 0, 100);
        var query = cache.Lookup(key, "AsKh");

        Assert.NotEmpty(query.ActionFrequencies);
        var sum = query.ActionFrequencies.Values.Sum();
        Assert.InRange(sum, 0.999, 1.001);
        Assert.NotNull(query.BestAction);
    }

    [Fact]
    public void Solver_ProducesNormalizedStrategies_OnTinyDepthTree()
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
    public void Solver_LimpedBbVsBtnOpen_HasStableBestAction()
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
    public void EngineQueryStrategy_ReturnsUnsupported_WhenHeroHandClassMissing()
    {
        var engine = new CfrPlusPreflopStrategyEngine(
            new MonteCarloStrategyEngine(),
            new PreflopSolverCache(new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()))),
            new PreflopSolverConfig(30, 50m, Rake, 2, RaiseSizingAbstraction.Default),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance);

        var query = engine.QueryStrategy(new PreflopInfoSetKey(2, Position.BTN, "UNOPENED", 1, 50), string.Empty);

        Assert.False(query.Supported);
        Assert.Contains("Missing hero hand class", query.UnsupportedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(query.ActionFrequencies);
    }

    [Fact]
    public void EngineQueryStrategy_ReturnsUnsupported_WhenHandConditionedKeyMisses()
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
        cache.GetOrSolve(config);

        var engine = new CfrPlusPreflopStrategyEngine(
            new MonteCarloStrategyEngine(),
            cache,
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance);

        var missingKey = new PreflopInfoSetKey(2, Position.UTG, "UNOPENED", 1, 40);
        var query = engine.QueryStrategy(missingKey, "2c7d");

        Assert.False(query.Supported);
        Assert.Equal("No solved strategy for key (did you change key format? clear cache / rerun solve).", query.UnsupportedReason);
        Assert.Empty(query.ActionFrequencies);
    }

    [Fact]
    public void AddPokerAnalyzer_ResolvesCfrEngine_WithSixMaxRecommendationAndFiniteEv()
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

        var recommendation = engine.Recommend(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
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
    public void DecisionPointEv_UsesReachWeightedAverage_InHeadsUp()
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
    public void InfosetReachWeight_UsesFullInfosetProbabilityMass_InMultiway()
    {
        var reachWeight = CfrPlusPreflopSolver.ComputeInfosetReachWeight([0.5d, 0.25d, 0.2d]);

        Assert.Equal(0.025d, reachWeight, precision: 10);
    }

    [Fact]
    public void DecisionPointEv_IsZero_WhenReachProbabilitySumIsZero()
    {
        var decisionPointEv = CfrPlusPreflopSolver.ComputeDecisionPointEvBb(reachWeightedUtilitySum: 123d, reachProbabilitySum: 0d);

        Assert.Equal(0m, decisionPointEv);
    }

    [Fact]
    public void UnconditionalContribution_MatchesReachWeightedUtilitySum()
    {
        var contribution = CfrPlusPreflopSolver.ComputeUnconditionalContributionBb(reachWeightedUtilitySum: -4.25d);

        Assert.Equal(-4.25m, contribution);
    }

    [Fact]
    public void InfosetReachWeight_IncludesChanceMultiplierWhenEmbeddedInPlayerReach()
    {
        const double heroChance = 0.4d;
        var reachWeight = CfrPlusPreflopSolver.ComputeInfosetReachWeight([heroChance, 0.5d]);

        Assert.Equal(0.2d, reachWeight, precision: 10);
    }

    [Fact]
    public void Solver_TerminalCacheAndUncached_ProduceEquivalentStrategies()
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
    public void Solver_TerminalCache_WorksForParallelAndSingleThreadSolveModes()
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
    public void Solver_ParallelLocalMergeMode_StaysCloseToSingleThread()
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
    public void Solver_ParallelMergePathWithOneWorker_DoesNotLoseInfosets()
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

}
