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
            new PreflopSolverConfig(140, 100m, new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true), 6, RaiseSizingAbstraction.Default, EnableParallelSolve: true, MaxDegreeOfParallelism: Math.Min(12, Environment.ProcessorCount), SolveMode: PreflopSolveMode.PopulationRange),
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

        var extraction = PreflopStateExtractor.TryExtract(state, hero, _config.ResolveSizing());
        var key = extraction?.Key;
        _logger.LogInformation(
            "Extract solver key. PlayerCount={PlayerCount}, HeroPosNormalized={HeroPosNormalized}, HistorySignature={HistorySignature}, RealToCallBb={RealToCallBb}, NormalizedToCallBb={NormalizedToCallBb}, EffectiveStackBb={EffectiveStackBb}, RealOpenBb={RealOpenBb}, Real3Bet={Real3Bet}, Real4Bet={Real4Bet}, NormalizedOpenBb={NormalizedOpenBb}, Normalized3Bet={Normalized3Bet}, Normalized4Bet={Normalized4Bet}, NormalizationNote={NormalizationNote}",
            key?.PlayerCount,
            key?.ActingPosition,
            key?.HistorySignature,
            extraction?.RealToCallBb,
            extraction?.NormalizedToCallBb,
            key?.EffectiveStackBb,
            extraction?.RealOpenSizeBb,
            extraction?.RealThreeBetSizeBb,
            extraction?.RealFourBetSizeBb,
            extraction?.NormalizedOpenSizeBb,
            extraction?.NormalizedThreeBetSizeBb,
            extraction?.NormalizedFourBetSizeBb,
            extraction?.NormalizationNote);
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
        var cacheKey = new PreflopSolverCacheKey(_config.PlayerCount, (int)Math.Round(_config.EffectiveStackBb), _config.Rake, _config.ResolveSizing().Fingerprint(), _config.SolveMode);
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
        {
            _logger.LogWarning("Lookup failed after normalization. LookupKey={LookupKey}, SupportedSizing={SupportedSizing}", normalizedKey, _config.ResolveSizing().Fingerprint());
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");
        }

        var ranked = query.ActionFrequencies.OrderByDescending(k => k.Value)
            .Select(k => new RecommendedAction(k.Key, null, (decimal)k.Value)).ToList();

        return new Recommendation(
            RankedActions: ranked,
            PrimaryAction: ranked.FirstOrDefault(),
            PrimaryEV: query.EstimatedEvBb,
            ReferenceEV: reference.ReferenceEV,
            PrimaryExplanation: $"CFR+ preflop solver key={query.InfoSet.HistorySignature}/{query.InfoSet.ActingPosition}, best={query.BestAction}, ev={query.EstimatedEvBb:0.###}bb ({(query.EvType == EvType.RangeEv ? "range" : "hand")})",
            EvType: query.EvType.ToString(),
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
