using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record PreflopSolverCacheKey(int PlayerCount, int EffectiveStackBb, RakeConfig Rake, string SizingFingerprint, PreflopSolveMode SolveMode);

public sealed class PreflopSolverCache : IPreflopStrategyStore
{
    private readonly CfrPlusPreflopSolver _solver;
    private readonly ILogger<PreflopSolverCache> _logger;
    private readonly ConcurrentDictionary<PreflopSolverCacheKey, Lazy<Task<PreflopSolveResult>>> _cache = new();

    public PreflopSolverCache(CfrPlusPreflopSolver solver)
        : this(solver, Microsoft.Extensions.Logging.Abstractions.NullLogger<PreflopSolverCache>.Instance)
    {
    }

    public PreflopSolverCache(CfrPlusPreflopSolver solver, ILogger<PreflopSolverCache> logger)
    {
        _solver = solver;
        _logger = logger;
    }

    private int _solveCount;
    public int SolveCount => _solveCount;
    public int CacheEntries => _cache.Count;

    public bool ContainsKey(PreflopSolverCacheKey key) => _cache.ContainsKey(key);

    public PreflopSolveResult GetOrSolve(PreflopSolverConfig config)
        => GetOrSolveAsync(config, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<PreflopSolveResult> GetOrSolveAsync(PreflopSolverConfig config, CancellationToken ct)
    {
        var sizing = config.ResolveSizing();
        var key = new PreflopSolverCacheKey(config.PlayerCount, (int)Math.Round(config.EffectiveStackBb), config.Rake, Fingerprint(sizing), config.SolveMode);
        var hadEntry = _cache.ContainsKey(key);
        _logger.LogInformation("Solve requested. CacheKey={CacheKey}, AlreadySolving={AlreadySolving}, CacheHit={CacheHit}, CacheMiss={CacheMiss}", key, hadEntry, hadEntry, !hadEntry);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<PreflopSolveResult>>(() => Task.Run(() =>
        {
            Interlocked.Increment(ref _solveCount);
            return _solver.SolvePreflop(config with { Sizing = new RaiseSizingAbstraction(sizing.OpenSizesBb, sizing.ThreeBetSizeMultipliers, sizing.FourBetSizeMultipliers, sizing.JamThresholdStackBb) });
        }, CancellationToken.None)));

        var result = await lazy.Value.WaitAsync(ct);
        started.Stop();
        _logger.LogInformation("Solve finished. DurationMs={DurationMs}, SolveCount={SolveCount}, ConfigSummary={ConfigSummary}",
            started.ElapsedMilliseconds,
            _solveCount,
            $"P{config.PlayerCount}/Eff{config.EffectiveStackBb}/It{config.Iterations}/Depth{config.MaxTreeDepth}");

        return result;
    }

    public StrategyQueryResult Lookup(PreflopInfoSetKey key, string heroHand)
    {
        var solvedResults = _cache.Values.Where(v => v.IsValueCreated).Select(v => v.Value.IsCompletedSuccessfully ? v.Value.Result : null).Where(v => v is not null).ToList();
        foreach (var solved in solvedResults)
        {
            foreach (var candidate in BuildLookupCandidates(key, solved!))
            {
                var result = _solver.QueryStrategy(solved!, candidate, heroHand);
                if (result.ActionFrequencies.Count > 0)
                    return Normalize(result);
            }
        }

        return new StrategyQueryResult(new Dictionary<PokerAnalyzer.Domain.Game.ActionType, double>(), null, 0m, key, EvType.RangeEv, MixType.ApproximateHandMix);
    }

    private static IEnumerable<PreflopInfoSetKey> BuildLookupCandidates(PreflopInfoSetKey key, PreflopSolveResult solved)
    {
        yield return key;

        var normalizedHistory = NormalizeHistory(key.HistorySignature);
        if (!string.Equals(normalizedHistory, key.HistorySignature, StringComparison.OrdinalIgnoreCase))
            yield return key with { HistorySignature = normalizedHistory };

        var nearest = solved.NodeStrategies.Keys
            .Where(k => k.PlayerCount == key.PlayerCount &&
                        k.ActingPosition == key.ActingPosition &&
                        string.Equals(NormalizeHistory(k.HistorySignature), normalizedHistory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => Math.Abs(k.ToCallBb - key.ToCallBb))
            .ThenBy(k => Math.Abs(k.EffectiveStackBb - key.EffectiveStackBb))
            .FirstOrDefault();

        if (nearest is not null)
            yield return nearest;
    }

    private static string NormalizeHistory(string history)
    {
        var trimmed = history.Trim().ToUpperInvariant();
        return trimmed switch
        {
            "LIMP" => "LIMPED",
            "UNOPEN" => "UNOPENED",
            _ => trimmed
        };
    }

    private static StrategyQueryResult Normalize(StrategyQueryResult query)
    {
        var sum = query.ActionFrequencies.Values.Sum();
        if (sum <= 0)
            return query;

        var normalized = query.ActionFrequencies.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / sum);
        var best = normalized.OrderByDescending(kvp => kvp.Value).Select(kvp => (PokerAnalyzer.Domain.Game.ActionType?)kvp.Key).FirstOrDefault();
        return query with { ActionFrequencies = normalized, BestAction = best };
    }

    private static string Fingerprint(PreflopSizingConfig s)
        => $"O:{string.Join(',', s.OpenSizesBb)}|3:{string.Join(',', s.ThreeBetSizeMultipliers)}|4:{string.Join(',', s.FourBetSizeMultipliers)}|J:{s.JamThresholdStackBb}|AJ:{s.AllowExplicitJam}";
}
