using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record PreflopSolverCacheKey(int PlayerCount, int EffectiveStackBb, RakeConfig Rake, string SizingFingerprint);

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
        var key = new PreflopSolverCacheKey(config.PlayerCount, (int)Math.Round(config.EffectiveStackBb), config.Rake, Fingerprint(sizing));
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
            var result = _solver.QueryStrategy(solved!, key, heroHand);
            if (result.ActionFrequencies.Count > 0)
                return result;
        }

        return new StrategyQueryResult(new Dictionary<PokerAnalyzer.Domain.Game.ActionType, double>(), null, 0m, key);
    }

    private static string Fingerprint(PreflopSizingConfig s)
        => $"O:{string.Join(',', s.OpenSizesBb)}|3:{string.Join(',', s.ThreeBetSizeMultipliers)}|4:{string.Join(',', s.FourBetSizeMultipliers)}|J:{s.JamThresholdStackBb}|AJ:{s.AllowExplicitJam}";
}
