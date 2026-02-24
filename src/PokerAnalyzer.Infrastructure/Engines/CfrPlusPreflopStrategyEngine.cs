using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopSolver;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class CfrPlusPreflopStrategyEngine : IStrategyEngine
{
    private readonly IMonteCarloReferenceEngine _monteCarloReference;
    private readonly PreflopSolverCache _store;
    private readonly CfrPlusPreflopSolver _solver;
    private readonly PreflopSolverConfig _config;

    public CfrPlusPreflopStrategyEngine(IMonteCarloReferenceEngine monteCarloReference)
    {
        _monteCarloReference = monteCarloReference;
        _solver = new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        _store = new PreflopSolverCache(_solver);
        _config = new PreflopSolverConfig(140, 100m, new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true), 2, RaiseSizingAbstraction.Default);
    }

    public Recommendation Recommend(HandState state, HeroContext hero)
    {
        var reference = _monteCarloReference.EvaluateReference(state, hero);
        if (state.Street != Street.Preflop || hero.HeroHoleCards is null)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var key = PreflopLiveStateMapper.TryMap(state, hero);
        if (key is null)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var playerCount = hero.PlayerPositions?.Count ?? state.ActivePlayers.Count;
        var solved = _store.GetOrSolve(_config with { PlayerCount = playerCount });
        var query = _solver.QueryStrategy(solved, key, hero.HeroHoleCards.Value.ToString());

        var ranked = query.ActionFrequencies.OrderByDescending(k => k.Value)
            .Select(k => new RecommendedAction(k.Key, null, (decimal)k.Value)).ToList();

        return new Recommendation(
            ranked,
            ranked.FirstOrDefault(),
            query.EstimatedEvBb,
            reference.ReferenceEV,
            $"CFR+ preflop solver key={query.InfoSet.HistorySig}/{query.InfoSet.ActingPosition}, best={query.BestAction}, ev={query.EstimatedEvBb:0.###}bb",
            reference.ReferenceExplanation);
    }

    public PreflopSolveResult SolvePreflop(PreflopSolverConfig config) => _store.GetOrSolve(config);

    public StrategyQueryResult QueryStrategy(PreflopInfoSetKey key, string heroHand) => _store.Lookup(key, heroHand);

    private static Recommendation BuildUnsupportedRecommendation(Recommendation reference, string reason)
        => new([], null, null, reference.ReferenceEV, reason, reference.ReferenceExplanation);
}

public static class PreflopLiveStateMapper
{
    public static PreflopInfoSetKey? TryMap(HandState state, HeroContext hero)
    {
        if (hero.PlayerPositions is null || !hero.PlayerPositions.TryGetValue(hero.HeroId, out var heroPos))
            return null;

        var history = hero.ActionHistory?.Where(a => a.Street == Street.Preflop).Select(a => a.Type).ToList() ?? [];
        var playerCount = hero.PlayerPositions.Count;
        var toCallBb = hero.BigBlind.Value <= 0 ? 0 : (int)Math.Round(state.GetToCall(hero.HeroId).Value / (decimal)hero.BigBlind.Value);
        var raises = history.Count(a => a is ActionType.Raise or ActionType.Bet or ActionType.AllIn);
        var lastRaiseBb = raises == 0 ? 0 : Math.Max(2, toCallBb);
        var eff = (int)Math.Round(state.Stacks[hero.HeroId].Value / (decimal)hero.BigBlind.Value);

        return new PreflopInfoSetKey(playerCount, heroPos, PreflopHistorySignature.Build(history), toCallBb, lastRaiseBb, eff);
    }
}
