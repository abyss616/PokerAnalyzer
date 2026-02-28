using System.Threading;
using System.Threading.Tasks;
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
            new PreflopSolverConfig(140, 100m, new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true), 6, RaiseSizingAbstraction.Default, EnableParallelSolve: true, MaxDegreeOfParallelism: Math.Min(12, Environment.ProcessorCount)),
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

    public async Task<Recommendation> RecommendAsync(HandState state, HeroContext hero, CancellationToken ct = default)
    {
        var reference = _monteCarloReference.EvaluateReference(state, hero);
        if (state.Street != Street.Preflop || hero.HeroHoleCards is null)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var extraction = PreflopStateExtractor.TryExtract(state, hero, _config.ResolveSizing());
        var key = extraction?.Key;
        var context = extraction?.SpotContext;
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

        if (extraction is not null && context is not null && !context.IsSupported)
        {
            _logger.LogWarning(
                "Unsupported preflop spot classification. Reason={Reason}, RaiseDepth={RaiseDepth}, ToCallBb={ToCallBb}, LastRaiseSizeBb={LastRaiseSizeBb}, FacingPosition={FacingPosition}, ActingPosition={ActingPosition}, RawHistory={RawHistory}",
                context.UnsupportedReason,
                context.RaiseDepth,
                context.ToCallBb,
                context.LastRaiseSizeBb,
                context.FacingPosition,
                context.ActingPosition,
                string.Join(" | ", hero.ActionHistory?.Where(a => a.Street == Street.Preflop)
                    .Select(a => $"{a.ActorId}:{a.Type}:{a.Amount.Value}") ?? []));
            return BuildUnsupportedRecommendation(reference, context.UnsupportedReason ?? "Unsupported preflop state for solver abstraction");
        }

        if (context is not null)
        {
            if (key?.HistorySignature == "OPEN" && context.ToCallBb > 0)
                return BuildUnsupportedRecommendation(reference, "Invalid preflop signature OPEN with toCall > 0");
            if (key?.HistorySignature.StartsWith("VS_", StringComparison.Ordinal) == true && context.ToCallBb <= 0)
                return BuildUnsupportedRecommendation(reference, "Invalid preflop signature VS_* with toCall == 0");
            if (context.RaiseDepth is < 0 or > 3)
                return BuildUnsupportedRecommendation(reference, $"Unsupported raise depth {context.RaiseDepth}");
        }

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
        await _cache.GetOrSolveAsync(_config, ct);
        var cacheKey = PreflopSolverCache.BuildCacheKey(_config);
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
            "Query result. BestAction={BestAction}, TopFrequencies={TopFrequencies}, DecisionPointEvBb={DecisionPointEvBb}, UnconditionalContributionBb={UnconditionalContributionBb}",
            query.BestAction,
            topFrequencies,
            query.DecisionPointEvBb,
            query.UnconditionalContributionBb);

        if (!query.Supported)
        {
            _logger.LogWarning(
                "Lookup failed after normalization. LookupKey={LookupKey}, SupportedSizing={SupportedSizing}, Reason={Reason}",
                normalizedKey,
                _config.ResolveSizing().Fingerprint(),
                query.UnsupportedReason);
            return BuildUnsupportedRecommendation(reference, query.UnsupportedReason ?? "Unsupported preflop state for solver abstraction");
        }

        var ranked = query.ActionFrequencies.OrderByDescending(k => k.Value)
            .Select(k => new RecommendedAction(k.Key, null, (decimal)k.Value)).ToList();

        return new Recommendation(
            RankedActions: ranked,
            PrimaryAction: ranked.FirstOrDefault(),
            PrimaryEV: query.DecisionPointEvBb,
            ReferenceEV: reference.ReferenceEV,
            PrimaryExplanation: $"CFR+ preflop solver key={query.InfoSet.HistorySignature}/{query.InfoSet.ActingPosition}, best={query.BestAction}, decision-point-ev(given reached)={query.DecisionPointEvBb:0.###}bb",
            ReferenceExplanation: reference.ReferenceExplanation);
    }

    public Task<PreflopSolveResult> SolvePreflopAsync(PreflopSolverConfig config, CancellationToken ct = default) => _cache.GetOrSolveAsync(config, ct);

    public async Task<StrategyQueryResult> QueryStrategyAsync(PreflopInfoSetKey key, string heroHand, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(heroHand))
            return new StrategyQueryResult(new Dictionary<ActionType, double>(), null, 0m, 0m, key, false, "Missing hero hand class");

        await _cache.GetOrSolveAsync(_config, ct);
        var query = _cache.Lookup(key, heroHand);
        return query.Supported
            ? query
            : query with { UnsupportedReason = query.UnsupportedReason ?? "No solved strategy for key (did you change key format? clear cache / rerun solve)." };
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
