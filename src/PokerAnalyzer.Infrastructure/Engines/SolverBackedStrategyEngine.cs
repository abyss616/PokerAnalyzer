using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class SolverBackedStrategyEngine : IStrategyEngine
{
    private readonly IPreflopSolverService _solver;
    private readonly DummyStrategyEngine _fallback = new();

    public SolverBackedStrategyEngine(IPreflopSolverService solver) => _solver = solver;

    public Recommendation Recommend(HandState state, HeroContext hero)
    {
        if (state.Street != Street.Preflop)
            return _fallback.Recommend(state, hero) with { Explanation = "Postflop fallback: solver is preflop-only." };

        var hand = HoleCards.Parse("AsKh");
        var node = new PreflopNodeState("root", Position.BTN, Position.SB, state.Stacks[hero.HeroId].Value / (decimal)hero.BigBlind.Value, state.Pot.Value / (decimal)hero.BigBlind.Value, true);
        var query = _solver.QueryStrategy(node, hand);
        var ranked = query.Frequencies.OrderByDescending(kv => kv.Value).Select(kv => new RecommendedAction(Parse(kv.Key), null, query.EstimatedEv)).ToList();
        return new Recommendation(ranked, $"CFR+ preflop approx | node={node.NodeId} | best={query.BestAction} | ev={query.EstimatedEv:F2}bb");
    }

    private static ActionType Parse(string action)
        => action switch
        {
            var a when a.StartsWith("raise") || a.StartsWith("threebet") || a.StartsWith("fourbet") || a.StartsWith("iso") => ActionType.Raise,
            "limp" => ActionType.Call,
            "check" => ActionType.Check,
            "call" => ActionType.Call,
            "fold" => ActionType.Fold,
            "jam" => ActionType.AllIn,
            _ => ActionType.Check
        };
}
