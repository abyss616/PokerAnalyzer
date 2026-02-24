using System.Collections.Concurrent;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record PreflopSolverCacheKey(int PlayerCount, int EffectiveStackBb, RakeConfig Rake, string SizingFingerprint);

public sealed class PreflopSolverCache : IPreflopStrategyStore
{
    private readonly CfrPlusPreflopSolver _solver;
    private readonly ConcurrentDictionary<PreflopSolverCacheKey, Lazy<Task<PreflopSolveResult>>> _cache = new();

    public PreflopSolverCache(CfrPlusPreflopSolver solver)
    {
        _solver = solver;
    }

    private int _solveCount;
    public int SolveCount => _solveCount;

    public PreflopSolveResult GetOrSolve(PreflopSolverConfig config)
        => GetOrSolveAsync(config, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<PreflopSolveResult> GetOrSolveAsync(PreflopSolverConfig config, CancellationToken ct)
    {
        var sizing = config.ResolveSizing();
        var key = new PreflopSolverCacheKey(config.PlayerCount, (int)Math.Round(config.EffectiveStackBb), config.Rake, Fingerprint(sizing));
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<PreflopSolveResult>>(() => Task.Run(() =>
        {
            Interlocked.Increment(ref _solveCount);
            return _solver.SolvePreflop(config with { Sizing = new RaiseSizingAbstraction(sizing.OpenSizesBb, sizing.ThreeBetSizeMultipliers, sizing.FourBetSizeMultipliers, sizing.JamThresholdStackBb) });
        }, CancellationToken.None)));

        return await lazy.Value.WaitAsync(ct);
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
