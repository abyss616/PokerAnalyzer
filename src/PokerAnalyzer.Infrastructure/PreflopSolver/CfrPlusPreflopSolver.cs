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

        IDictionary<PreflopInfoSetKey, InfoSetData> infosets = config.EnableParallelSolve
                  ? new ConcurrentDictionary<PreflopInfoSetKey, InfoSetData>()
                  : new Dictionary<PreflopInfoSetKey, InfoSetData>();
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

        for (var iteration = 0; iteration < config.Iterations; iteration++)
        {
            if (config.EnableParallelSolve && heroHands.Length > 1)
            {
                Parallel.ForEach(
                    heroHands,
                    new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    handClassEntry =>
                    {
                        var reach = Enumerable.Repeat(1d, config.PlayerCount).ToArray();
                        reach[0] *= handClassEntry.Value;
                        Cfr(tree.Root, [], reach, infosets, positions, stackBucket, config, handClassEntry.Key);
                    });
            }
            else
            {
                foreach (var (heroHandClass, probability) in heroHands)
                {
                    var reach = Enumerable.Repeat(1d, config.PlayerCount).ToArray();
                    reach[0] *= probability;
                    Cfr(tree.Root, [], reach, infosets, positions, stackBucket, config, heroHandClass);
                }
            }

            if ((iteration + 1) % progressEveryIterations == 0 || lastProgressLog.ElapsedMilliseconds >= 2000)
            {
                _logger.LogInformation("Solve progress. Iteration={Iteration}, ElapsedMs={ElapsedMs}, InfosetCount={InfosetCount}", iteration + 1, totalStopwatch.ElapsedMilliseconds, infosets.Count);
                lastProgressLog.Restart();
            }
        }

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
        IReadOnlyList<ActionType> history,
        double[] reach,
        IDictionary<PreflopInfoSetKey, InfoSetData> infosets,
        IReadOnlyList<Position> positions,
        int effectiveStackBucket,
        PreflopSolverConfig config,
        string heroHandClass)
    {
        if (node.IsTerminal || PreflopRules.IsTerminal(node.State, out _))
        {
            return (double)EvaluateTerminalUtility(node.State, config, heroHandClass);
        }

        var actor = node.State.ActingIndex;
        var legalActions = node.LegalActions;

        if (legalActions.Length == 0)
            return (double)EvaluateTerminalUtility(node.State, config, heroHandClass);

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

            var nextHistory = new List<ActionType>(history.Count + 1);
            nextHistory.AddRange(history);
            nextHistory.Add(action);
            var nextReach = (double[])reach.Clone();
            nextReach[actor] *= strategy[actionIndex];
            var utility = Cfr(child, nextHistory, nextReach, infosets, positions, effectiveStackBucket, config, heroHandClass);
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

    private decimal EvaluateTerminalUtility(PreflopPublicState state, PreflopSolverConfig config, string heroHandClass)
    {
        var heroIndex = 0;
        var villainRange = PreflopRange.BuildClassDistribution().ToDictionary(k => k.Key.Label, v => v.Value);
        var nodeState = BuildNodeState(state, heroIndex, config.PlayerCount, (int)Math.Round(config.EffectiveStackBb));

        if (state.InHand.Count(inHand => inHand) <= 1)
        {
            return _terminal.EvaluateFold(nodeState, heroFolds: !state.InHand[heroIndex]);
        }

        if (state.InHand[heroIndex] && state.StackBb[heroIndex] == 0)
        {
            return _terminal.EvaluateAllIn(nodeState, heroHandClass, villainRange, config.Rake);
        }

        return _terminal.EvaluateCallToFlop(nodeState, heroHandClass, villainRange);
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
                target[key] = sourceData;
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
    }
}
