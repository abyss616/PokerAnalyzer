using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopSolver;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class CfrPlusPreflopStrategyEngine : IStrategyEngine
{
    private readonly IMonteCarloReferenceEngine _monteCarloReference;
    private readonly CfrPlusPreflopSolver _solver;
    private readonly PreflopSolveResult _solved;

    public CfrPlusPreflopStrategyEngine(IMonteCarloReferenceEngine monteCarloReference)
    {
        _monteCarloReference = monteCarloReference;
        var config = new PreflopSolverConfig(220, 100m, new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true));
        _solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        _solved = _solver.SolvePreflop(config);
    }

    public Recommendation Recommend(HandState state, HeroContext hero)
    {
        var reference = _monteCarloReference.EvaluateReference(state, hero);

        if (state.Street != Street.Preflop || hero.HeroHoleCards is null)
            return BuildUnsupportedRecommendation(reference);

        var node = ResolveNode(state, hero);
        if (node is null)
            return BuildUnsupportedRecommendation(reference);

        var query = _solver.QueryStrategy(_solved, node, hero.HeroHoleCards.Value.ToString());
        var ranked = query.ActionFrequencies
            .OrderByDescending(k => k.Value)
            .Select(k => new RecommendedAction(k.Key, ResolveToAmount(state, hero.HeroId, k.Key, node), (decimal)k.Value))
            .ToList();

        var primary = ranked.FirstOrDefault();
        return new Recommendation(
            ranked,
            PrimaryAction: primary,
            PrimaryEV: (decimal)query.EstimatedEvBb,
            ReferenceEV: reference.ReferenceEV,
            PrimaryExplanation: $"CFR+ preflop solver node={query.NodeId}, hero={hero.HeroHoleCards.Value}, best={query.BestAction}, approxEV={query.EstimatedEvBb:0.###}bb",
            ReferenceExplanation: reference.ReferenceExplanation
        );
    }

    public PreflopSolveResult SolvePreflop(PreflopSolverConfig config) => _solver.SolvePreflop(config);

    public StrategyQueryResult QueryStrategy(PreflopNodeState state, string heroHand) => _solver.QueryStrategy(_solved, state, heroHand);

    private static Recommendation BuildUnsupportedRecommendation(Recommendation reference)
    {
        return new Recommendation(
            RankedActions: Array.Empty<RecommendedAction>(),
            PrimaryAction: null,
            PrimaryEV: null,
            ReferenceEV: reference.ReferenceEV,
            PrimaryExplanation: "CFR+ node unsupported — no solver output available.",
            ReferenceExplanation: reference.ReferenceExplanation
        );
    }

    private static ChipAmount? ResolveToAmount(HandState state, PlayerId heroId, ActionType action, PreflopNodeState node)
    {
        if (action == ActionType.Raise)
        {
            var target = node.NodeId switch
            {
                var id when id.StartsWith("OPEN_SB") => 300,
                var id when id.StartsWith("OPEN_") => 250,
                var id when id.StartsWith("VS_OPEN_SB") => 1050,
                var id when id.StartsWith("VS_OPEN") => state.GetToCall(heroId).Value > 200 ? 1000 : 900,
                var id when id.StartsWith("VS_3BET") => 2200,
                var id when id.StartsWith("BB_VS_SB_LIMP") => 450,
                _ => 250
            };
            return new ChipAmount(target);
        }

        if (action == ActionType.AllIn)
            return new ChipAmount(state.StreetContrib[heroId].Value + state.Stacks[heroId].Value);

        return null;
    }

    private static PreflopNodeState? ResolveNode(HandState state, HeroContext hero)
    {
        var history = hero.ActionHistory?.Where(a => a.Street == Street.Preflop).ToArray() ?? Array.Empty<BettingAction>();
        var raises = history.Count(a => a.Type is ActionType.Raise or ActionType.Bet or ActionType.AllIn);
        var limps = history.Count(a => a.Type == ActionType.Call);

        var heroPos = hero.PlayerPositions is not null && hero.PlayerPositions.TryGetValue(hero.HeroId, out var hp)
            ? hp
            : Position.Unknown;

        if (heroPos == Position.SB && raises == 0 && limps == 0 && state.GetToCall(hero.HeroId).Value == 50)
            return new PreflopNodeState("OPEN_SB", Position.SB, Position.BB, 1.5m, 0.5m, 0.5m, 1m, false, false, false, 100m);

        if (heroPos == Position.BTN && raises == 0)
            return new PreflopNodeState("OPEN_BTN", Position.BTN, Position.BB, 1.5m, 1m, 0m, 1m, false, false, false, 100m);

        if (heroPos == Position.BB && raises == 0 && limps > 0)
            return new PreflopNodeState("BB_VS_SB_LIMP", Position.BB, Position.SB, 2m, 0m, 1m, 1m, true, false, false, 100m);

        if (raises == 1)
            return new PreflopNodeState("VS_OPEN_BB_vs_BTN", heroPos, Position.BTN, 4m, 2.5m, 1m, 2.5m, false, false, false, 100m);

        if (raises == 2)
            return new PreflopNodeState("VS_3BET_BTN_vs_BB", heroPos, Position.BB, 14m, 7.5m, 2.5m, 10m, false, true, false, 100m);

        if (raises >= 3)
            return new PreflopNodeState("VS_4BET_BTN_vs_BB", heroPos, Position.BB, 35m, 12m, 10m, 22m, false, false, true, 100m);

        return null;
    }
}
