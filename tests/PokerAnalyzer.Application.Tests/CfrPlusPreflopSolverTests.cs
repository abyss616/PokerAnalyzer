using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.PreflopSolver;
using Xunit;

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
        var builder = new PreflopGameTreeBuilder();
        var nodes = builder.Build(new PreflopSolverConfig(20, 100m, Rake, playerCount, RaiseSizingAbstraction.Default));

        var firstActor = playerCount == 2 ? Position.BTN : PreflopGameTreeBuilder.GetTablePositions(playerCount)[0];
        Assert.Contains(nodes, n => n.InfoSet.HistorySig == "UNOPENED" && n.InfoSet.ActingPosition == firstActor);
    }

    [Fact]
    public void HeadsUp_ActionOrder_IsBtnThenBb()
    {
        var nodes = new PreflopGameTreeBuilder().Build(new PreflopSolverConfig(20, 100m, Rake, 2, RaiseSizingAbstraction.Default));
        Assert.Contains(nodes, n => n.InfoSet.HistorySig == "UNOPENED" && n.InfoSet.ActingPosition == Position.BTN);
        Assert.Contains(nodes, n => n.InfoSet.HistorySig == "OPEN" && n.InfoSet.ActingPosition == Position.BB);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TreeContains_VsOpenAndVs3BetResponses(int playerCount)
    {
        var nodes = new PreflopGameTreeBuilder().Build(new PreflopSolverConfig(30, 100m, Rake, playerCount, RaiseSizingAbstraction.Default));
        Assert.Contains(nodes, n => n.InfoSet.HistorySig is "OPEN" or "OPEN_CALL");
        Assert.Contains(nodes, n => n.InfoSet.HistorySig.Contains("3BET", StringComparison.Ordinal));
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

        var engine = new CfrPlusPreflopStrategyEngine(new MonteCarloStrategyEngine());
        var rec = engine.Recommend(state, new HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = HoleCards.Parse("AsAh"),
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.BTN, [villain] = Position.BB },
            ActionHistory = []
        });

        Assert.NotNull(rec.PrimaryEV);
        Assert.NotNull(rec.ReferenceEV);
        Assert.NotEmpty(rec.RankedActions);
    }
}
