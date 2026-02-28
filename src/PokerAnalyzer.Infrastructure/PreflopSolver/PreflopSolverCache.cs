using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record PreflopSolverCacheKey(int CacheVersion, int PlayerCount, int EffectiveStackBb, RakeConfig Rake, string SizingFingerprint);

public sealed class PreflopSolverCacheOptions
{
    public int MaxEntries { get; init; } = 64;
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan TrimInterval { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed class PreflopSolverCache : IPreflopStrategyStore
{
    private const int CurrentCacheVersion = 2;

    private readonly CfrPlusPreflopSolver _solver;
    private readonly ILogger<PreflopSolverCache> _logger;
    private readonly PreflopSolverCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<PreflopSolverCacheKey, CacheEntry> _cache = new();
    private readonly object _trimGate = new();
    private long _nextTrimUtcTicks;

    public PreflopSolverCache(CfrPlusPreflopSolver solver)
        : this(
            solver,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PreflopSolverCache>.Instance,
            new PreflopSolverCacheOptions(),
            TimeProvider.System)
    {
    }

    public PreflopSolverCache(CfrPlusPreflopSolver solver, ILogger<PreflopSolverCache> logger)
        : this(solver, logger, new PreflopSolverCacheOptions(), TimeProvider.System)
    {
    }

    [ActivatorUtilitiesConstructor]
    public PreflopSolverCache(
        CfrPlusPreflopSolver solver,
        ILogger<PreflopSolverCache> logger,
        PreflopSolverCacheOptions options,
        TimeProvider timeProvider)
    {
        _solver = solver;
        _logger = logger;
        _options = options;
        _timeProvider = timeProvider;
        _nextTrimUtcTicks = _timeProvider.GetUtcNow().Add(_options.TrimInterval).Ticks;
    }

    private int _solveCount;
    public int SolveCount => _solveCount;
    public int CacheEntries => _cache.Count;

    public bool ContainsKey(PreflopSolverCacheKey key) => _cache.ContainsKey(key);

    public static PreflopSolverCacheKey BuildCacheKey(PreflopSolverConfig config)
    {
        var sizing = config.ResolveSizing();
        return new PreflopSolverCacheKey(CurrentCacheVersion, config.PlayerCount, (int)Math.Round(config.EffectiveStackBb), config.Rake, Fingerprint(sizing));
    }

    [Obsolete("Use GetOrSolveAsync; synchronous calls are not supported.")]
    public PreflopSolveResult GetOrSolve(PreflopSolverConfig config)
        => throw new NotSupportedException("Use GetOrSolveAsync; synchronous calls are not supported.");

    public async Task<PreflopSolveResult> GetOrSolveAsync(PreflopSolverConfig config, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        var sizing = config.ResolveSizing();
        var key = BuildCacheKey(config);
        var hadEntry = _cache.ContainsKey(key);
        _logger.LogInformation("Solve requested. CacheKey={CacheKey}, AlreadySolving={AlreadySolving}, CacheHit={CacheHit}, CacheMiss={CacheMiss}", key, hadEntry, hadEntry, !hadEntry);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var now = _timeProvider.GetUtcNow();
        var entry = _cache.GetOrAdd(key, _ => new CacheEntry(now));
        entry.Touch(now);

        if (ShouldTrim(now))
            TrimCache(now);

        ct.ThrowIfCancellationRequested();
        var solveTask = entry.GetOrCreateSolveTask(() => Task.Run(() =>
        {
            Interlocked.Increment(ref _solveCount);
            return _solver.SolvePreflop(config with { Sizing = new RaiseSizingAbstraction(sizing.OpenSizesBb, sizing.ThreeBetSizeMultipliers, sizing.FourBetSizeMultipliers, sizing.JamThresholdStackBb) });
        }));

        var result = await solveTask.WaitAsync(ct);
        started.Stop();
        _logger.LogInformation("Solve finished. DurationMs={DurationMs}, SolveCount={SolveCount}, ConfigSummary={ConfigSummary}",
            started.ElapsedMilliseconds,
            _solveCount,
            $"P{config.PlayerCount}/Eff{config.EffectiveStackBb}/It{config.Iterations}/Depth{config.MaxTreeDepth}");

        return result;
    }

    public StrategyQueryResult Lookup(PreflopInfoSetKey key, string heroHand)
        => Lookup(key, heroHand, exactStateOnly: false);

    public StrategyQueryResult Lookup(PreflopInfoSetKey key, string heroHand, bool exactStateOnly = false)
    {
        var normalizedHeroHand = NormalizeHeroHand(heroHand);
        if (string.IsNullOrWhiteSpace(normalizedHeroHand))
            return new StrategyQueryResult(new Dictionary<PokerAnalyzer.Domain.Game.ActionType, double>(), null, 0m, 0m, key, false, "Missing hero hand class");

        var now = _timeProvider.GetUtcNow();
        var solvedResults = _cache.Values
            .Select(v =>
            {
                v.Touch(now);
                return v.TryGetCompletedResult();
            })
            .Where(v => v is not null)
            .ToList();

        if (ShouldTrim(now))
            TrimCache(now);

        foreach (var solved in solvedResults)
        {
            foreach (var candidate in BuildLookupCandidates(key, normalizedHeroHand, solved!, exactStateOnly))
            {
                var result = _solver.QueryStrategy(solved!, candidate, normalizedHeroHand);
                if (result.Supported)
                    return Normalize(result);
            }

            if (HasLegacyEntryWithoutHeroHandClass(key, solved!))
            {
                return new StrategyQueryResult(
                    new Dictionary<PokerAnalyzer.Domain.Game.ActionType, double>(),
                    null,
                    0m,
                    0m,
                    key,
                    false,
                    "No solved strategy for key (did you change key format? clear cache / rerun solve).");
            }
        }

        return new StrategyQueryResult(
            new Dictionary<PokerAnalyzer.Domain.Game.ActionType, double>(),
            null,
            0m,
            0m,
            key with { HeroHandClass = normalizedHeroHand },
            false,
            "No solved strategy for key (did you change key format? clear cache / rerun solve).");
    }

    private static IEnumerable<PreflopInfoSetKey> BuildLookupCandidates(PreflopInfoSetKey key, string normalizedHeroHand, PreflopSolveResult solved, bool exactStateOnly)
    {
        yield return key with { HeroHandClass = normalizedHeroHand };

        if (exactStateOnly)
            yield break;

        var normalizedHistory = NormalizeHistory(key.HistorySignature);
        if (!string.Equals(normalizedHistory, key.HistorySignature, StringComparison.OrdinalIgnoreCase))
            yield return key with { HistorySignature = normalizedHistory, HeroHandClass = normalizedHeroHand };

        var nearest = solved.NodeStrategies.Keys
            .Where(k => k.PlayerCount == key.PlayerCount &&
                        k.ActingPosition == key.ActingPosition &&
                        string.Equals(k.HeroHandClass, normalizedHeroHand, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(NormalizeHistory(k.HistorySignature), normalizedHistory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => Math.Abs(k.ToCallBb - key.ToCallBb))
            .ThenBy(k => Math.Abs(k.EffectiveStackBb - key.EffectiveStackBb))
            .FirstOrDefault();

        if (nearest is not null)
            yield return nearest;

        var relaxedNearest = solved.NodeStrategies.Keys
            .Where(k => ArePositionsCompatible(k.ActingPosition, key.ActingPosition) &&
                        string.Equals(k.HeroHandClass, normalizedHeroHand, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(NormalizeHistory(k.HistorySignature), normalizedHistory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => Math.Abs(k.ToCallBb - key.ToCallBb))
            .ThenBy(k => Math.Abs(k.EffectiveStackBb - key.EffectiveStackBb))
            .ThenBy(k => Math.Abs(k.PlayerCount - key.PlayerCount))
            .FirstOrDefault();

        if (relaxedNearest is not null && relaxedNearest != nearest)
            yield return relaxedNearest;
    }

    private static bool ArePositionsCompatible(Position candidate, Position requested)
    {
        if (candidate == requested)
            return true;

        return (candidate, requested) is (Position.SB, Position.BTN) or (Position.BTN, Position.SB);
    }

    private static bool HasLegacyEntryWithoutHeroHandClass(PreflopInfoSetKey key, PreflopSolveResult solved)
    {
        var normalizedHistory = NormalizeHistory(key.HistorySignature);
        return solved.NodeStrategies.Keys.Any(k =>
            k.PlayerCount == key.PlayerCount &&
            k.ActingPosition == key.ActingPosition &&
            string.IsNullOrWhiteSpace(k.HeroHandClass) &&
            string.Equals(NormalizeHistory(k.HistorySignature), normalizedHistory, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeHeroHand(string heroHand)
    {
        if (string.IsNullOrWhiteSpace(heroHand))
            return string.Empty;

        var normalized = heroHand.Trim().ToUpperInvariant();
        if (normalized.Length == 4)
        {
            var first = normalized[0];
            var second = normalized[2];
            var suited = char.ToUpperInvariant(normalized[1]) == char.ToUpperInvariant(normalized[3]);
            var ordered = OrderRanks(first, second);
            return ordered.high == ordered.low ? $"{ordered.high}{ordered.low}" : $"{ordered.high}{ordered.low}{(suited ? "S" : "O")}";
        }

        return normalized;
    }

    private static (char high, char low) OrderRanks(char first, char second)
    {
        return RankValue(first) >= RankValue(second) ? (first, second) : (second, first);
    }

    private static int RankValue(char rank)
    {
        return char.ToUpperInvariant(rank) switch
        {
            'A' => 14,
            'K' => 13,
            'Q' => 12,
            'J' => 11,
            'T' => 10,
            '9' => 9,
            '8' => 8,
            '7' => 7,
            '6' => 6,
            '5' => 5,
            '4' => 4,
            '3' => 3,
            '2' => 2,
            _ => 0
        };
    }

    private static string NormalizeHistory(string history)
    {
        var trimmed = history.Trim().ToUpperInvariant();
        if (trimmed.StartsWith("VS_OPEN_", StringComparison.Ordinal))
            return "OPEN";
        if (trimmed.StartsWith("VS_3BET_", StringComparison.Ordinal))
            return "OPEN_3BET";
        if (trimmed.StartsWith("VS_4BET_", StringComparison.Ordinal))
            return "OPEN_3BET_4BET";

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

    private bool ShouldTrim(DateTimeOffset now) => now.Ticks >= Interlocked.Read(ref _nextTrimUtcTicks);

    private void TrimCache(DateTimeOffset now)
    {
        lock (_trimGate)
        {
            if (!ShouldTrim(now))
                return;

            var trimmedForTtl = 0;
            if (_options.Ttl > TimeSpan.Zero)
            {
                foreach (var (key, entry) in _cache)
                {
                    if (!entry.IsCompleted)
                        continue;

                    if (now - entry.LastAccessUtc <= _options.Ttl)
                        continue;

                    if (_cache.TryRemove(key, out _))
                        trimmedForTtl++;
                }
            }

            var trimmedForSize = 0;
            if (_options.MaxEntries > 0 && _cache.Count > _options.MaxEntries)
            {
                var overflow = _cache.Count - _options.MaxEntries;
                var evictionCandidates = _cache
                    .Where(kvp => kvp.Value.IsCompleted)
                    .OrderBy(kvp => kvp.Value.LastAccessUtc)
                    .Take(overflow)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in evictionCandidates)
                {
                    if (_cache.TryRemove(key, out _))
                        trimmedForSize++;
                }
            }

            var totalEvicted = trimmedForTtl + trimmedForSize;
            if (totalEvicted > 0)
            {
                _logger.LogInformation(
                    "Cache trim executed. EvictedCount={EvictedCount}, TtlEvicted={TtlEvicted}, SizeEvicted={SizeEvicted}, CacheEntries={CacheEntries}",
                    totalEvicted,
                    trimmedForTtl,
                    trimmedForSize,
                    _cache.Count);
            }

            Interlocked.Exchange(ref _nextTrimUtcTicks, now.Add(_options.TrimInterval).Ticks);
        }
    }

    private sealed class CacheEntry
    {
        private readonly Lazy<Task<PreflopSolveResult>> _solveTask;
        private long _lastAccessUtcTicks;
        private Func<Task<PreflopSolveResult>>? _taskFactory;

        public CacheEntry(DateTimeOffset createdUtc)
        {
            _lastAccessUtcTicks = createdUtc.Ticks;
            _solveTask = new Lazy<Task<PreflopSolveResult>>(() =>
            {
                var factory = Interlocked.Exchange(ref _taskFactory, null)
                    ?? throw new InvalidOperationException("Solve task factory was not initialized.");
                return factory();
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public DateTimeOffset LastAccessUtc => new(Interlocked.Read(ref _lastAccessUtcTicks), TimeSpan.Zero);
        public bool IsCompleted => _solveTask.IsValueCreated && _solveTask.Value.IsCompleted;

        public void Touch(DateTimeOffset now) => Interlocked.Exchange(ref _lastAccessUtcTicks, now.Ticks);

        public Task<PreflopSolveResult> GetOrCreateSolveTask(Func<Task<PreflopSolveResult>> taskFactory)
        {
            Interlocked.CompareExchange(ref _taskFactory, taskFactory, null);
            return _solveTask.Value;
        }

        public PreflopSolveResult? TryGetCompletedResult()
        {
            if (!_solveTask.IsValueCreated)
                return null;

            var task = _solveTask.Value;
            return task.IsCompletedSuccessfully ? task.Result : null;
        }

    }
}
