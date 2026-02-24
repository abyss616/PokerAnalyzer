using Microsoft.Extensions.Logging;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopSolver;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class CfrPlusPreflopStrategyEngine : IStrategyEngine
{
    private readonly IMonteCarloReferenceEngine _monteCarloReference;
    private readonly PreflopSolverCache _cache;
    private readonly PreflopSolverConfig _config;
    private readonly ILogger<CfrPlusPreflopStrategyEngine> _logger;

    public CfrPlusPreflopStrategyEngine(IMonteCarloReferenceEngine monteCarloReference)
        : this(
            monteCarloReference,
            new PreflopSolverCache(new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()))),
            new PreflopSolverConfig(140, 100m, new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true), 2, RaiseSizingAbstraction.Default),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance)
    {
    }

    public CfrPlusPreflopStrategyEngine(
        IMonteCarloReferenceEngine monteCarloReference,
        PreflopSolverCache cache,
        PreflopSolverConfig config,
        ILogger<CfrPlusPreflopStrategyEngine> logger)
    {
        _monteCarloReference = monteCarloReference;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public Recommendation Recommend(HandState state, HeroContext hero)
    {
        var reference = _monteCarloReference.EvaluateReference(state, hero);
        if (state.Street != Street.Preflop || hero.HeroHoleCards is null)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var key = PreflopStateExtractor.TryExtract(state, hero);
        if (key is null)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var normalizedKey = key with
        {
            PlayerCount = _config.PlayerCount,
            EffectiveStackBb = (int)Math.Round(_config.EffectiveStackBb)
        };

        _logger.LogInformation("Ensuring preflop strategy solved for {Players} players", _config.PlayerCount);
        _cache.GetOrSolve(_config);
        var query = _cache.Lookup(normalizedKey, hero.HeroHoleCards.Value.ToString());
        if (query.ActionFrequencies.Count == 0)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var ranked = query.ActionFrequencies.OrderByDescending(k => k.Value)
            .Select(k => new RecommendedAction(k.Key, null, (decimal)k.Value)).ToList();

        return new Recommendation(
            RankedActions: ranked,
            PrimaryAction: ranked.FirstOrDefault(),
            PrimaryEV: query.EstimatedEvBb,
            ReferenceEV: reference.ReferenceEV,
            PrimaryExplanation: $"CFR+ preflop solver key={query.InfoSet.HistorySignature}/{query.InfoSet.ActingPosition}, best={query.BestAction}, ev={query.EstimatedEvBb:0.###}bb",
            ReferenceExplanation: reference.ReferenceExplanation);
    }

    public PreflopSolveResult SolvePreflop(PreflopSolverConfig config) => _cache.GetOrSolve(config);

    public StrategyQueryResult QueryStrategy(PreflopInfoSetKey key, string heroHand)
    {
        _cache.GetOrSolve(_config);
        return _cache.Lookup(key, heroHand);
    }

    private static Recommendation BuildUnsupportedRecommendation(Recommendation reference, string reason)
        => new(
            RankedActions: [],
            ReferenceEV: reference.ReferenceEV,
            PrimaryExplanation: reason,
            ReferenceExplanation: reference.ReferenceExplanation);
}

public static class PreflopStateExtractor
{
    public static PreflopInfoSetKey? TryExtract(HandState state, HeroContext hero)
    {
        if (hero.PlayerPositions is null || !hero.PlayerPositions.TryGetValue(hero.HeroId, out var heroPos))
            return null;

        var history = hero.ActionHistory?.Where(a => a.Street == Street.Preflop).Select(a => a.Type).ToList() ?? [];
        var playerCount = hero.PlayerPositions.Count;
        var toCallBb = hero.BigBlind.Value <= 0 ? 0 : (int)Math.Round(state.GetToCall(hero.HeroId).Value / (decimal)hero.BigBlind.Value);
        var eff = (int)Math.Round(state.Stacks[hero.HeroId].Value / (decimal)hero.BigBlind.Value);

        return new PreflopInfoSetKey(playerCount, heroPos, PreflopHistorySignature.Build(history), toCallBb, eff);
    }
}
