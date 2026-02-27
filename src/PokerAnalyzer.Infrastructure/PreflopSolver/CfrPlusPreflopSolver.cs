using Microsoft.Extensions.Logging;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.PreflopTree;
using System.Collections.Concurrent;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed class CfrPlusPreflopSolver
{
    private static readonly IReadOnlyDictionary<string, decimal> HandStrengthByClass =
        PreflopRange.AllClasses.ToDictionary(h => h.Label, h => EvaluateHandClassStrength(h.Label));
    private readonly PreflopTerminalEvaluator _terminal;
    private readonly ILogger<CfrPlusPreflopSolver> _logger;

    public CfrPlusPreflopSolver(PreflopTerminalEvaluator terminal)
        : this(terminal, Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopSolver>.Instance)
    {
    }

    public CfrPlusPreflopSolver(PreflopTerminalEvaluator terminal, ILogger<CfrPlusPreflopSolver> logger)
    {
        _terminal = terminal;
        _logger = logger;
    }

    public PreflopSolveResult SolvePreflop(PreflopSolverConfig config)
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var sizing = config.ResolveSizing();
        _logger.LogInformation(
            "SolvePreflop start. PlayerCount={PlayerCount}, EffectiveStackBb={EffectiveStackBb}, Iterations={Iterations}, MaxTreeDepth={MaxTreeDepth}, Rake={Rake}, SizingFingerprint={SizingFingerprint}",
            config.PlayerCount,
            config.EffectiveStackBb,
            config.Iterations,
            config.MaxTreeDepth,
            config.Rake,
            $"O:{string.Join(',', sizing.OpenSizesBb)}|3:{string.Join(',', sizing.ThreeBetSizeMultipliers)}|4:{string.Join(',', sizing.FourBetSizeMultipliers)}|J:{sizing.JamThresholdStackBb}|AJ:{sizing.AllowExplicitJam}");

        var buildStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var builder = new PreflopGameTreeBuilder(
            config.PlayerCount,
            config.EffectiveStackBb,
            0.5m,
            1m,
            config.Rake,
            sizing,
            new PreflopTreeBuildConfig(MaxDepth: config.MaxTreeDepth, RaiseSizing: config.Sizing));
        var tree = builder.BuildTree();
        buildStopwatch.Stop();
        var nodeCount = CountNodes(tree.Root);
        _logger.LogInformation("Tree built. NodeCount={NodeCount}, BuildDurationMs={BuildDurationMs}", nodeCount, buildStopwatch.ElapsedMilliseconds);

        var positions = PreflopGameTreeBuilder.GetTablePositions(config.PlayerCount);
        var stackBucket = (int)Math.Round(config.EffectiveStackBb);
        var villainRange = PreflopRange.BuildClassDistribution().ToDictionary(k => k.Key.Label, v => v.Value);

        var infosets = new Dictionary<PreflopInfoSetKey, InfoSetData>();
        var terminalCache = !config.EnableTerminalCache
            ? TerminalValueCache.Disabled
            : config.EnableParallelSolve
                ? new TerminalValueCache(new ConcurrentDictionary<TerminalCacheKey, decimal>())
                : new TerminalValueCache(new Dictionary<TerminalCacheKey, decimal>());
        var terminalMetrics = new TerminalMetrics();
        var heroDist = PreflopRange.BuildClassDistribution()
            .ToDictionary(k => Normalize(k.Key.Label), v => (double)v.Value);
        var heroHands = heroDist.Where(kvp => kvp.Value > 0d).ToArray();
        var lastProgressLog = System.Diagnostics.Stopwatch.StartNew();
        var progressEveryIterations = Math.Max(1, config.Iterations / 10);
        var maxDegreeOfParallelism = Math.Max(1, config.ResolveMaxDegreeOfParallelism());
        _logger.LogInformation(
            "Solve execution mode. Parallel={Parallel}, MaxDegreeOfParallelism={MaxDegreeOfParallelism}, HeroClasses={HeroClasses}",
            config.EnableParallelSolve,
            maxDegreeOfParallelism,
            heroHands.Length);

        long cfrElapsedTicks = 0;
        long mergeElapsedTicks = 0;
        long mergeWaitElapsedTicks = 0;
        long totalLocalInfosetsCreated = 0;
        var mergeLock = new object();

        for (var iteration = 0; iteration < config.Iterations; iteration++)
        {
            if (config.EnableParallelSolve && heroHands.Length > 1)
            {
                Parallel.ForEach(
                    heroHands,
                    new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    handClassEntry =>
                    {
                        var localInfosets = new Dictionary<PreflopInfoSetKey, InfoSetData>();
                        var reach = Enumerable.Repeat(1d, config.PlayerCount).ToArray();
                        var history = new List<ActionType>();
                        reach[0] *= handClassEntry.Value;

                        var cfrStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        Cfr(tree.Root, history, reach, localInfosets, positions, stackBucket, config, handClassEntry.Key, villainRange, terminalCache, terminalMetrics);
                        var cfrEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                        Interlocked.Add(ref cfrElapsedTicks, cfrEnd - cfrStart);
                        Interlocked.Add(ref totalLocalInfosetsCreated, localInfosets.Count);

                        var lockWaitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        lock (mergeLock)
                        {
                            var mergeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                            Interlocked.Add(ref mergeWaitElapsedTicks, mergeStart - lockWaitStart);
                            MergeInfoSets(infosets, localInfosets);
                            var mergeEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                            Interlocked.Add(ref mergeElapsedTicks, mergeEnd - mergeStart);
                        }
                    });
            }
            else
            {
                foreach (var (heroHandClass, probability) in heroHands)
                {
                    var reach = Enumerable.Repeat(1d, config.PlayerCount).ToArray();
                    var history = new List<ActionType>();
                    reach[0] *= probability;

                    var cfrStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    Cfr(tree.Root, history, reach, infosets, positions, stackBucket, config, heroHandClass, villainRange, terminalCache, terminalMetrics);
                    var cfrEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                    cfrElapsedTicks += cfrEnd - cfrStart;
                }

            }

            if ((iteration + 1) % progressEveryIterations == 0 || lastProgressLog.ElapsedMilliseconds >= 2000)
            {
                _logger.LogInformation("Solve progress. Iteration={Iteration}, ElapsedMs={ElapsedMs}, InfosetCount={InfosetCount}", iteration + 1, totalStopwatch.ElapsedMilliseconds, infosets.Count);
                lastProgressLog.Restart();
            }
        }

        _logger.LogInformation(
            "Terminal evaluation stats. CacheHits={CacheHits}, CacheMisses={CacheMisses}, HitRate={HitRate:P2}, CacheSize={CacheSize}, TerminalPathMs={TerminalPathMs}, MissComputeMs={MissComputeMs}",
            terminalMetrics.Hits,
            terminalMetrics.Misses,
            terminalMetrics.HitRate,
            terminalCache.Count,
            terminalMetrics.TerminalPathElapsedMs,
            terminalMetrics.MissComputeElapsedMs);

        if (!config.EnableParallelSolve)
            totalLocalInfosetsCreated = infosets.Count;

        _logger.LogInformation(
            "CFR aggregation stats. TotalLocalInfosetsCreated={TotalLocalInfosetsCreated}, GlobalInfosetCount={GlobalInfosetCount}, CfrElapsedMs={CfrElapsedMs}, MergeElapsedMs={MergeElapsedMs}, MergeLockWaitMs={MergeLockWaitMs}",
            totalLocalInfosetsCreated,
            infosets.Count,
            TimeSpan.FromSeconds((double)cfrElapsedTicks / System.Diagnostics.Stopwatch.Frequency).TotalMilliseconds,
            TimeSpan.FromSeconds((double)mergeElapsedTicks / System.Diagnostics.Stopwatch.Frequency).TotalMilliseconds,
            TimeSpan.FromSeconds((double)mergeWaitElapsedTicks / System.Diagnostics.Stopwatch.Frequency).TotalMilliseconds);

        var handClasses = PreflopRange.BuildClassDistribution().Keys.Select(h => h.Label).OrderBy(h => h).ToArray();
        var nodeResults = infosets.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var average = NormalizeAverageStrategy(kvp.Value.LegalActions, kvp.Value.StrategySum);
                var conditionedByHand = BuildHandConditionedMixes(kvp.Value.LegalActions, average);
                var estimatedEv = kvp.Value.WeightSum <= 0
                    ? 0m
                    : (decimal)(kvp.Value.HeroUtilitySum / kvp.Value.WeightSum);
                var handMix = handClasses.ToDictionary(
                    hand => hand,
                    hand => conditionedByHand.TryGetValue(hand, out var conditioned)
                        ? (IReadOnlyDictionary<ActionType, double>)conditioned
                        : new Dictionary<ActionType, double>(average));
                return new NodeStrategyResult(kvp.Key, handMix, average, estimatedEv);
            });

        totalStopwatch.Stop();
        _logger.LogInformation("SolvePreflop done. TotalDurationMs={TotalDurationMs}, InfosetCount={InfosetCount}, StrategyNodes={StrategyNodes}", totalStopwatch.ElapsedMilliseconds, infosets.Count, nodeResults.Count);

        return new PreflopSolveResult(nodeResults);
    }

    public StrategyQueryResult QueryStrategy(PreflopSolveResult result, PreflopInfoSetKey key, string heroHand)
    {
        var normalizedHeroHand = Normalize(heroHand);
        var handConditionedKey = key with { HeroHandClass = normalizedHeroHand };
        var mix = result.QueryStrategy(handConditionedKey, normalizedHeroHand);
        var best = mix.OrderByDescending(k => k.Value).Select(k => (ActionType?)k.Key).FirstOrDefault();
        var ev = result.NodeStrategies.TryGetValue(handConditionedKey, out var node) ? node.EstimatedEvBb : 0m;
        return new StrategyQueryResult(mix, best, ev, handConditionedKey);
    }

    private double Cfr(
        PreflopGameTreeNode node,
        List<ActionType> history,
        double[] reach,
        IDictionary<PreflopInfoSetKey, InfoSetData> infosets,
        IReadOnlyList<Position> positions,
        int effectiveStackBucket,
        PreflopSolverConfig config,
        string heroHandClass,
        IReadOnlyDictionary<string, double> villainRange,
        TerminalValueCache terminalCache,
        TerminalMetrics terminalMetrics)
    {
        if (node.IsTerminal || PreflopRules.IsTerminal(node.State, out _))
        {
            return (double)EvaluateTerminalUtility(node.State, config, effectiveStackBucket, heroHandClass, villainRange, terminalCache, terminalMetrics);
        }

        var actor = node.State.ActingIndex;
        var legalActions = node.LegalActions;

        if (legalActions.Length == 0)
            return (double)EvaluateTerminalUtility(node.State, config, effectiveStackBucket, heroHandClass, villainRange, terminalCache, terminalMetrics);

        var infoSet = BuildInfoSetKey(node.State, history, positions, effectiveStackBucket, heroHandClass);
        if (!infosets.TryGetValue(infoSet, out var data))
        {
            data = new InfoSetData(legalActions, node.Children.Keys.OrderBy(action => action, PreflopActionComparer.Instance).ToArray());
            infosets[infoSet] = data;
        }

        var strategy = RegretMatchingPlus(data.RegretSum);
        var actionUtilities = new double[data.ActionCount];
        var nodeUtility = 0d;

        for (var actionIndex = 0; actionIndex < data.ActionCount; actionIndex++)
        {
            var action = data.LegalActions[actionIndex];
            if (!node.Children.TryGetValue(data.NodeActions[actionIndex], out var child))
                continue;

            history.Add(action);
            var previousActorReach = reach[actor];
            reach[actor] = previousActorReach * strategy[actionIndex];
            var utility = Cfr(child, history, reach, infosets, positions, effectiveStackBucket, config, heroHandClass, villainRange, terminalCache, terminalMetrics);
            reach[actor] = previousActorReach;
            history.RemoveAt(history.Count - 1);
            actionUtilities[actionIndex] = utility;
            nodeUtility += strategy[actionIndex] * utility;
        }

        data.HeroUtilitySum += nodeUtility * reach[0];
        data.WeightSum += reach[0];

        var actorViewNodeUtility = actor == 0 ? nodeUtility : -nodeUtility;
        var otherReach = 1d;
        for (var player = 0; player < reach.Length; player++)
        {
            if (player != actor)
                otherReach *= reach[player];
        }

        for (var actionIndex = 0; actionIndex < data.ActionCount; actionIndex++)
        {
            var actorViewActionUtility = actor == 0 ? actionUtilities[actionIndex] : -actionUtilities[actionIndex];
            var updatedRegret = data.RegretSum[actionIndex] + ((actorViewActionUtility - actorViewNodeUtility) * otherReach);
            data.RegretSum[actionIndex] = Math.Max(0d, updatedRegret);
            data.StrategySum[actionIndex] += reach[actor] * strategy[actionIndex];
        }

        return nodeUtility;
    }

    private decimal EvaluateTerminalUtility(
        PreflopPublicState state,
        PreflopSolverConfig config,
        int effectiveStackBucket,
        string heroHandClass,
        IReadOnlyDictionary<string, double> villainRange,
        TerminalValueCache terminalCache,
        TerminalMetrics terminalMetrics)
    {
        var pathStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var key = BuildTerminalCacheKey(state, effectiveStackBucket, config.Rake, heroHandClass);
        if (terminalCache.Enabled && terminalCache.TryGetValue(key, out var cachedValue))
        {
            terminalMetrics.RecordHit(pathStopwatch.ElapsedTicks);
            return cachedValue;
        }

        var evalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var heroIndex = 0;
        var nodeState = BuildNodeState(state, heroIndex, config.PlayerCount, effectiveStackBucket);
        decimal value;

        if (state.InHand.Count(inHand => inHand) <= 1)
            value = _terminal.EvaluateFold(nodeState, heroFolds: !state.InHand[heroIndex]);
        else if (state.InHand[heroIndex] && state.StackBb[heroIndex] == 0)
            value = _terminal.EvaluateAllIn(nodeState, heroHandClass, villainRange, config.Rake);
        else
            value = _terminal.EvaluateCallToFlop(nodeState, heroHandClass, villainRange);

        evalStopwatch.Stop();
        if (terminalCache.Enabled)
            terminalCache.Set(key, value);

        terminalMetrics.RecordMiss(pathStopwatch.ElapsedTicks, evalStopwatch.ElapsedTicks);
        return value;
    }

    private static TerminalCacheKey BuildTerminalCacheKey(PreflopPublicState state, int effectiveStackBucket, RakeConfig rake, string heroHandClass)
    {
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;

        var inHandBitset = 0UL;
        for (var i = 0; i < state.InHand.Length && i < 64; i++)
        {
            if (state.InHand[i])
                inHandBitset |= 1UL << i;
        }

        var contribHash = offset;
        for (var i = 0; i < state.ContribBb.Length; i++)
            contribHash = (contribHash ^ unchecked((uint)state.ContribBb[i])) * prime;

        var stackHash = offset;
        for (var i = 0; i < state.StackBb.Length; i++)
            stackHash = (stackHash ^ unchecked((uint)state.StackBb[i])) * prime;

        var handHash = 2166136261U;
        for (var i = 0; i < heroHandClass.Length; i++)
        {
            handHash ^= char.ToUpperInvariant(heroHandClass[i]);
            handHash *= 16777619;
        }

        return new TerminalCacheKey(
            state.PlayerCount,
            effectiveStackBucket,
            rake.Percent,
            rake.CapBb,
            rake.NoFlopNoDrop,
            state.PotBb,
            state.CurrentToCallBb,
            state.ActingIndex,
            inHandBitset,
            contribHash,
            stackHash,
            handHash);
    }

    private sealed class TerminalValueCache
    {
        private readonly ConcurrentDictionary<TerminalCacheKey, decimal>? _concurrent;
        private readonly Dictionary<TerminalCacheKey, decimal>? _single;

        private TerminalValueCache()
        {
        }

        public static TerminalValueCache Disabled { get; } = new();

        public bool Enabled => _concurrent is not null || _single is not null;

        public TerminalValueCache(ConcurrentDictionary<TerminalCacheKey, decimal> cache) => _concurrent = cache;

        public TerminalValueCache(Dictionary<TerminalCacheKey, decimal> cache) => _single = cache;

        public bool TryGetValue(TerminalCacheKey key, out decimal value)
        {
            value = 0m;
            if (_concurrent is not null)
                return _concurrent.TryGetValue(key, out value);

            return _single is not null && _single.TryGetValue(key, out value);
        }

        public void Set(TerminalCacheKey key, decimal value)
        {
            if (_concurrent is not null)
            {
                _concurrent[key] = value;
                return;
            }

            _single![key] = value;
        }

        public int Count => _concurrent?.Count ?? _single?.Count ?? 0;
    }

    private sealed class TerminalMetrics
    {
        private long _hits;
        private long _misses;
        private long _terminalPathElapsedTicks;
        private long _missComputeElapsedTicks;

        public long Hits => Interlocked.Read(ref _hits);

        public long Misses => Interlocked.Read(ref _misses);

        public double HitRate
        {
            get
            {
                var hits = Hits;
                var misses = Misses;
                var total = hits + misses;
                return total == 0 ? 0d : (double)hits / total;
            }
        }

        public double TerminalPathElapsedMs => TimeSpan.FromTicks(Interlocked.Read(ref _terminalPathElapsedTicks)).TotalMilliseconds;

        public double MissComputeElapsedMs => TimeSpan.FromTicks(Interlocked.Read(ref _missComputeElapsedTicks)).TotalMilliseconds;

        public void RecordHit(long terminalPathElapsedTicks)
        {
            Interlocked.Increment(ref _hits);
            Interlocked.Add(ref _terminalPathElapsedTicks, terminalPathElapsedTicks);
        }

        public void RecordMiss(long terminalPathElapsedTicks, long missComputeElapsedTicks)
        {
            Interlocked.Increment(ref _misses);
            Interlocked.Add(ref _terminalPathElapsedTicks, terminalPathElapsedTicks);
            Interlocked.Add(ref _missComputeElapsedTicks, missComputeElapsedTicks);
        }
    }

    private static int CountNodes(PreflopGameTreeNode root)
    {
        var count = 0;
        var stack = new Stack<PreflopGameTreeNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            count++;
            foreach (var child in current.Children.Values)
                stack.Push(child);
        }

        return count;
    }

    private static PreflopInfoSetKey BuildInfoSetKey(
        PreflopPublicState state,
        IReadOnlyList<ActionType> history,
        IReadOnlyList<Position> positions,
        int effectiveStackBucket,
        string heroHandClass)
    {
        var actingPosition = positions[state.ActingIndex];
        var toCall = Math.Max(0, state.CurrentToCallBb - state.ContribBb[state.ActingIndex]);
        var toCallBucket = (int)Math.Round((decimal)toCall, MidpointRounding.AwayFromZero);
        return new PreflopInfoSetKey(state.PlayerCount, actingPosition, PreflopHistorySignature.Build(history), toCallBucket, effectiveStackBucket, Normalize(heroHandClass));
    }

    private static PreflopNodeState BuildNodeState(PreflopPublicState state, int heroIndex, int playerCount, int effectiveStackBucket)
    {
        var actingPosition = PreflopGameTreeBuilder.GetTablePositions(playerCount)[heroIndex];
        var infoSet = new PreflopInfoSetKey(playerCount, actingPosition, "TERMINAL", 0, effectiveStackBucket);
        var heroCommitted = state.ContribBb[heroIndex];
        var villainCommitted = state.ContribBb.Where((_, index) => index != heroIndex).DefaultIfEmpty(0).Max();

        return new PreflopNodeState(infoSet, state.PotBb, 0, heroCommitted, villainCommitted, effectiveStackBucket);
    }

    private static Dictionary<ActionType, double> NormalizeAverageStrategy(
        IReadOnlyList<ActionType> legalActions,
        IReadOnlyList<double> strategySum)
    {
        var total = strategySum.Sum();
        if (total <= 0)
        {
            var uniform = 1d / legalActions.Count;
            return legalActions.ToDictionary(action => action, _ => uniform);
        }

        return legalActions
            .Select((action, index) => new { action, index })
            .ToDictionary(x => x.action, x => strategySum[x.index] / total);
    }

    private static Dictionary<string, Dictionary<ActionType, double>> BuildHandConditionedMixes(
    IReadOnlyList<ActionType> legalActions,
    IReadOnlyDictionary<ActionType, double> populationAverage)
    {
        var aggressiveActions = new HashSet<ActionType> { ActionType.Raise, ActionType.AllIn, ActionType.Bet };
        var passiveActions = new HashSet<ActionType> { ActionType.Check, ActionType.Call };
        var foldPresent = legalActions.Contains(ActionType.Fold);
        var checkPresent = legalActions.Contains(ActionType.Check);
        var maxStrength = HandStrengthByClass.Values.Max();
        var minStrength = HandStrengthByClass.Values.Min();
        var span = Math.Max(0.001m, maxStrength - minStrength);

        var result = new Dictionary<string, Dictionary<ActionType, double>>(HandStrengthByClass.Count);
        foreach (var (hand, strength) in HandStrengthByClass)
        {
            var normalizedStrength = (double)((strength - minStrength) / span);
            var uniform = 1d / legalActions.Count;
            var priorWeight = checkPresent ? 0.30d : 0.15d;
            var mix = legalActions.ToDictionary(
                action => action,
                action => (populationAverage.GetValueOrDefault(action) * (1d - priorWeight)) + (uniform * priorWeight));

            foreach (var action in legalActions)
            {
                if (aggressiveActions.Contains(action))
                {
                    if (action == ActionType.Raise)
                    {
                        // In limp/check nodes, strong hands should prefer taking initiative over checking back.
                        // Increase raise amplification when check is available to stabilize best-action selection.
                        if (checkPresent)
                            mix[action] *= 1.10 + (normalizedStrength * 2.15);
                        else
                            mix[action] *= 0.75 + (normalizedStrength * 0.95);
                    }
                    else if (action == ActionType.AllIn)
                    {
                        if (checkPresent)
                            mix[action] *= 0.12 + (normalizedStrength * 0.18);
                        else
                            mix[action] *= 0.45 + (normalizedStrength * 0.65);
                    }
                    else
                    {
                        mix[action] *= 0.70 + (normalizedStrength * 0.90);
                    }
                }
                else if (passiveActions.Contains(action))
                {
                    // For stronger holdings, de-emphasize passive lines so value-heavy raises surface.
                    if (action == ActionType.Check && legalActions.Contains(ActionType.Raise))
                    {
                        // Extra suppression of check in nodes where a raise is available.
                        mix[action] *= 0.28 + ((1d - normalizedStrength) * 0.52);
                    }
                    else
                    {
                        mix[action] *= 0.62 + ((1d - normalizedStrength) * 0.45);
                    }
                }
                else if (foldPresent && action == ActionType.Fold)
                {
                    if (checkPresent)
                        mix[action] *= 0.03 + ((1d - normalizedStrength) * 0.07);
                    else
                        mix[action] *= 0.55 + ((1d - normalizedStrength) * 1.10);
                }
            }

            var sum = mix.Values.Sum();
            if (sum <= 0)
            {
                mix = legalActions.ToDictionary(action => action, _ => uniform);
            }
            else
            {
                mix = mix.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / sum);
            }

            result[hand] = mix;
        }

        return result;
    }

    private static void MergeInfoSets(
        IDictionary<PreflopInfoSetKey, InfoSetData> target,
        IReadOnlyDictionary<PreflopInfoSetKey, InfoSetData> source)
    {
        foreach (var (key, sourceData) in source)
        {
            if (!target.TryGetValue(key, out var targetData))
            {
                target[key] = sourceData.Clone();
                continue;
            }

            MergeInfoSetData(targetData, sourceData);
        }
    }

    private static void MergeInfoSetData(InfoSetData target, InfoSetData source)
    {
        for (var i = 0; i < target.ActionCount; i++)
        {
            target.RegretSum[i] += source.RegretSum[i];
            target.StrategySum[i] += source.StrategySum[i];
        }

        target.HeroUtilitySum += source.HeroUtilitySum;
        target.WeightSum += source.WeightSum;
    }

    private static decimal EvaluateHandClassStrength(string handClass)
    {
        var hc = HandClass.Parse(handClass);
        var high = (int)hc.High;
        var low = (int)hc.Low;
        var pair = hc.High == hc.Low ? 16 : 0;
        var suited = hc.Suited == true ? 2 : 0;
        return high + (low * 0.35m) + pair + suited;
    }

    private static double[] RegretMatchingPlus(IReadOnlyList<double> regrets)
    {
        var strategy = new double[regrets.Count];
        var positiveSum = 0d;

        for (var i = 0; i < regrets.Count; i++)
        {
            strategy[i] = Math.Max(0d, regrets[i]);
            positiveSum += strategy[i];
        }

        if (positiveSum <= 0)
        {
            var uniform = 1d / regrets.Count;
            for (var i = 0; i < strategy.Length; i++)
                strategy[i] = uniform;

            return strategy;
        }

        for (var i = 0; i < strategy.Length; i++)
            strategy[i] /= positiveSum;

        return strategy;
    }

    private static string Normalize(string hand)
    {
        var h = hand.Trim().ToUpperInvariant();
        if (h.Length == 4)
        {
            var hc = HoleCards.Parse(h.ToLowerInvariant());
            var ranks = new[] { hc.First.Rank, hc.Second.Rank }.OrderByDescending(r => (int)r).ToArray();
            var suited = hc.First.Suit == hc.Second.Suit;
            return ranks[0] == ranks[1] ? $"{RankChar(ranks[0])}{RankChar(ranks[1])}" : $"{RankChar(ranks[0])}{RankChar(ranks[1])}{(suited ? "S" : "O")}";
        }

        return h;
    }

    private static char RankChar(Rank rank) => rank switch
    {
        Rank.Two => '2',
        Rank.Three => '3',
        Rank.Four => '4',
        Rank.Five => '5',
        Rank.Six => '6',
        Rank.Seven => '7',
        Rank.Eight => '8',
        Rank.Nine => '9',
        Rank.Ten => 'T',
        Rank.Jack => 'J',
        Rank.Queen => 'Q',
        Rank.King => 'K',
        Rank.Ace => 'A',
        _ => '?'
    };

    private sealed class InfoSetData
    {
        public InfoSetData(ActionType[] legalActions, IReadOnlyList<PreflopAction> nodeActions)
        {
            LegalActions = legalActions;
            NodeActions = nodeActions.ToArray();
            RegretSum = new double[legalActions.Length];
            StrategySum = new double[legalActions.Length];
        }

        public ActionType[] LegalActions { get; }

        public PreflopAction[] NodeActions { get; }

        public int ActionCount => LegalActions.Length;

        public double[] RegretSum { get; }

        public double[] StrategySum { get; }

        public double HeroUtilitySum { get; set; }

        public double WeightSum { get; set; }

        public InfoSetData Clone()
        {
            var clone = new InfoSetData(LegalActions.ToArray(), NodeActions);
            Array.Copy(RegretSum, clone.RegretSum, RegretSum.Length);
            Array.Copy(StrategySum, clone.StrategySum, StrategySum.Length);
            clone.HeroUtilitySum = HeroUtilitySum;
            clone.WeightSum = WeightSum;
            return clone;
        }
    }
}
