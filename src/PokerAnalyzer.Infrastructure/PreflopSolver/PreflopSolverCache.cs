using System.Collections.Concurrent;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record PreflopSolverCacheKey(int PlayerCount, int EffectiveStackBb, RakeConfig Rake, string SizingFingerprint);

public sealed class PreflopSolverCache : IPreflopStrategyStore
{
    private readonly CfrPlusPreflopSolver _solver;
    private readonly ConcurrentDictionary<PreflopSolverCacheKey, PreflopSolveResult> _cache = new();

    public PreflopSolverCache(CfrPlusPreflopSolver solver)
    {
        _solver = solver;
    }

    private int _solveCount;
    public int SolveCount => _solveCount;

    public PreflopSolveResult GetOrSolve(PreflopSolverConfig config)
    {
        var sizing = config.Sizing ?? RaiseSizingAbstraction.Default;
        var key = new PreflopSolverCacheKey(config.PlayerCount, (int)Math.Round(config.EffectiveStackBb), config.Rake, Fingerprint(sizing));
        return _cache.GetOrAdd(key, _ =>
        {
            Interlocked.Increment(ref _solveCount);
            return _solver.SolvePreflop(config with { Sizing = sizing });
        });
    }

    public StrategyQueryResult Lookup(PreflopInfoSetKey key, string heroHand)
    {
        var match = _cache.Values.Select(v => _solver.QueryStrategy(v, key, heroHand)).FirstOrDefault(r => r.ActionFrequencies.Count > 0);
        return match ?? new StrategyQueryResult(new Dictionary<PokerAnalyzer.Domain.Game.ActionType, double>(), null, 0m, key);
    }

    private static string Fingerprint(RaiseSizingAbstraction s)
        => $"O:{string.Join(',', s.OpenSizesBb)}|3:{string.Join(',', s.ThreeBetSizesBb)}|4:{string.Join(',', s.FourBetSizesBb)}|J:{s.JamThresholdStackBb}";
}
