using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.PreflopSolver;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public class CfrPlusPreflopSolverTests
{
    private static readonly PreflopSolverConfig Config = new(150, 100m, new RakeConfig(0.05m, 1m, true));

    [Fact]
    public void UnopenedBtn_AA_RaisesMoreThan_72o()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var solved = solver.SolvePreflop(Config);

        var aa = solved.QueryStrategy("OPEN_BTN", "AA");
        var sevenTwo = solved.QueryStrategy("OPEN_BTN", "72O");

        Assert.True(aa[ActionType.Raise] > sevenTwo[ActionType.Raise]);
    }

    [Fact]
    public void BbVsSbLimp_StrongHandsHaveIsoFrequency()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var solved = solver.SolvePreflop(Config);

        var aks = solved.QueryStrategy("BB_VS_SB_LIMP", "AKS");
        Assert.True(aks[ActionType.Raise] > 0.05d);
    }

    [Fact]
    public void HeadsUpMirror_SubgameOrderingIsConsistent()
    {
        var solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        var solved = solver.SolvePreflop(Config);

        var sbOpenEv = solved.NodeStrategies["OPEN_SB"].EstimatedEvBb;
        var bbVsLimpEv = solved.NodeStrategies["BB_VS_SB_LIMP"].EstimatedEvBb;

        Assert.True(sbOpenEv >= bbVsLimpEv - 0.5m);
    }

    [Fact]
    public void Regression_PostflopNotLabeledAsPreflopPolicy()
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
            new ChipAmount(10)).TransitionToStreet(Street.Flop);

        var engine = new MonteCarloStrategyEngine();
        var rec = engine.Recommend(state, new PokerAnalyzer.Application.Engines.HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = PokerAnalyzer.Domain.Cards.HoleCards.Parse("AsKh"),
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.BB, [villain] = Position.BTN },
            ActionHistory = [new BettingAction(Street.Preflop, villain, ActionType.Raise, new ChipAmount(25))]
        });

        Assert.DoesNotContain("preflop policy", rec.ReferenceExplanation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Engine_UsesSolverForPreflopRecommendation()
    {
        var hero = PlayerId.New();
        var villain = PlayerId.New();

        var state = HandState.CreateNewHand(
            new[]
            {
                new PlayerSeat(hero, "Hero", 1, Position.BTN, new ChipAmount(10000)),
                new PlayerSeat(villain, "Villain", 2, Position.BB, new ChipAmount(10000))
            },
            new ChipAmount(5),
            new ChipAmount(10));

        var engine = new CfrPlusPreflopStrategyEngine(new MonteCarloStrategyEngine());
        var rec = engine.Recommend(state, new PokerAnalyzer.Application.Engines.HeroContext(hero, new ChipAmount(5), new ChipAmount(10))
        {
            HeroHoleCards = PokerAnalyzer.Domain.Cards.HoleCards.Parse("AsAh"),
            PlayerPositions = new Dictionary<PlayerId, Position> { [hero] = Position.BTN, [villain] = Position.BB },
            ActionHistory = []
        });

        Assert.Contains("CFR+ preflop solver", rec.PrimaryExplanation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(rec.RankedActions);
    }
}
