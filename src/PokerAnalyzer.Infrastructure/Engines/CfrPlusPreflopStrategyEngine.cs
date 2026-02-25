using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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
            new PreflopSolverConfig(140, 100m, new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true), 6, RaiseSizingAbstraction.Default),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
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
        _logger.LogInformation(
            "Extract solver key. PlayerCount={PlayerCount}, HeroPosNormalized={HeroPosNormalized}, HistorySignature={HistorySignature}, ToCallBb={ToCallBb}, EffectiveStackBb={EffectiveStackBb}",
            key?.PlayerCount,
            key?.ActingPosition,
            key?.HistorySignature,
            key?.ToCallBb,
            key?.EffectiveStackBb);
        if (key is null)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var normalizedKey = key with
        {
            PlayerCount = _config.PlayerCount,
            EffectiveStackBb = (int)Math.Round(_config.EffectiveStackBb)
        };
        _logger.LogInformation(
            "Normalize key. NormalizedPlayerCount={NormalizedPlayerCount}, NormalizedEffStackBb={NormalizedEffStackBb}, ConfigEffStackBb={ConfigEffStackBb}",
            normalizedKey.PlayerCount,
            normalizedKey.EffectiveStackBb,
            _config.EffectiveStackBb);

        _logger.LogInformation("Ensuring preflop strategy solved for {Players} players", _config.PlayerCount);
        _cache.GetOrSolve(_config);
        var cacheKey = new PreflopSolverCacheKey(_config.PlayerCount, (int)Math.Round(_config.EffectiveStackBb), _config.Rake, _config.ResolveSizing().Fingerprint());
        _logger.LogInformation(
            "Cache lookup. CacheKey={CacheKey}, CacheHit={CacheHit}, SolveCount={SolveCount}, CacheEntries={CacheEntries}",
            cacheKey,
            _cache.ContainsKey(cacheKey),
            _cache.SolveCount,
            _cache.CacheEntries);

        var query = _cache.Lookup(normalizedKey, hero.HeroHoleCards.Value.ToString());
        var topFrequencies = string.Join(", ", query.ActionFrequencies
            .OrderByDescending(k => k.Value)
            .Take(3)
            .Select(k => $"{k.Key}:{k.Value:0.###}"));
        _logger.LogInformation(
            "Query result. BestAction={BestAction}, TopFrequencies={TopFrequencies}, EstimatedEvBb={EstimatedEvBb}",
            query.BestAction,
            topFrequencies,
            query.EstimatedEvBb);

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

    private Recommendation BuildUnsupportedRecommendation(Recommendation reference, string reason)
    {
        _logger.LogWarning("Unsupported reason. Reason={Reason}", reason);
        return new Recommendation(
            RankedActions: [],
            ReferenceEV: reference.ReferenceEV,
            PrimaryExplanation: reason,
            ReferenceExplanation: reference.ReferenceExplanation);
    }
}

internal static class PreflopSizingConfigExtensions
{
    public static string Fingerprint(this PreflopSizingConfig s)
        => $"O:{string.Join(',', s.OpenSizesBb)}|3:{string.Join(',', s.ThreeBetSizeMultipliers)}|4:{string.Join(',', s.FourBetSizeMultipliers)}|J:{s.JamThresholdStackBb}|AJ:{s.AllowExplicitJam}";
}

public static class PreflopStateExtractor
{
    public static PreflopInfoSetKey? TryExtract(HandState state, HeroContext hero)
    {
        if (hero.PlayerPositions is null || !hero.PlayerPositions.TryGetValue(hero.HeroId, out var heroPos))
            return null;

        var history = hero.ActionHistory?.Where(a => a.Street == Street.Preflop).Select(a => a.Type).ToList() ?? [];
        var playerCount = hero.PlayerPositions.Count;
        var normalizedHeroPos = playerCount == 2 && heroPos == Position.SB ? Position.BTN : heroPos;
        var toCallBb = hero.BigBlind.Value <= 0
            ? 0
            : (int)Math.Round(
                state.GetToCall(hero.HeroId).Value / (decimal)hero.BigBlind.Value,
                0,
                MidpointRounding.AwayFromZero);
        var eff = (int)Math.Round(state.Stacks[hero.HeroId].Value / (decimal)hero.BigBlind.Value);

        return new PreflopInfoSetKey(playerCount, normalizedHeroPos, PreflopHistorySignature.Build(history), toCallBb, eff);
    }
}
