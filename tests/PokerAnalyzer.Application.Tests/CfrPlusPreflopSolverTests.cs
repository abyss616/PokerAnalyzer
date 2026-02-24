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
        });

        Assert.NotNull(extracted);
        Assert.Equal(2, extracted!.PlayerCount);
        Assert.Equal(Position.BTN, extracted.ActingPosition);
        Assert.Equal("UNOPENED", extracted.HistorySignature);
        Assert.Equal(1, extracted.ToCallBb);

        var normalized = extracted with { PlayerCount = 2, EffectiveStackBb = 100 };
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

        var key = PreflopStateExtractor.TryExtract(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.SB, [villain] = Position.BB },
            ActionHistory = []
        });

        Assert.NotNull(key);
        Assert.Equal(Position.BTN, key!.ActingPosition);
        Assert.Equal(1, key.ToCallBb);
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

        var key = PreflopStateExtractor.TryExtract(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.UTG, [villain] = Position.BB },
            ActionHistory = []
        });

        Assert.NotNull(key);
        Assert.Equal("UNOPENED", key!.HistorySignature);
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

        Assert.InRange(query.ActionFrequencies.Values.Sum(), 0.999, 1.001);
        Assert.Equal(ActionType.Raise, query.BestAction);
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
}
