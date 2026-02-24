using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.PreflopTree;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed class CfrPlusPreflopSolver
{
    private const string DefaultHeroHandClass = "AKO";
    private readonly PreflopTerminalEvaluator _terminal;

    public CfrPlusPreflopSolver(PreflopTerminalEvaluator terminal)
    {
        _terminal = terminal;
    }

    public PreflopSolveResult SolvePreflop(PreflopSolverConfig config)
    {
        var sizing = config.ResolveSizing();
        var builder = new PreflopGameTreeBuilder(
            config.PlayerCount,
            config.EffectiveStackBb,
            0.5m,
            1m,
            config.Rake,
            sizing,
            new PreflopTreeBuildConfig(MaxDepth: config.MaxTreeDepth, RaiseSizing: config.Sizing));
        var tree = builder.BuildTree();
        var positions = PreflopGameTreeBuilder.GetTablePositions(config.PlayerCount);
        var stackBucket = (int)Math.Round(config.EffectiveStackBb);

        var infosets = new Dictionary<PreflopInfoSetKey, InfoSetData>();
        var initialReach = Enumerable.Repeat(1d, config.PlayerCount).ToArray();

        for (var iteration = 0; iteration < config.Iterations; iteration++)
        {
            Cfr(tree.Root, [], initialReach, infosets, positions, stackBucket, config);
        }

        var handClasses = PreflopRange.BuildClassDistribution().Keys.Select(h => h.Label).OrderBy(h => h).ToArray();
        var nodeResults = infosets.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var average = NormalizeAverageStrategy(kvp.Value.LegalActions, kvp.Value.StrategySum);
                var handMix = handClasses.ToDictionary(
                    hand => hand,
                    _ => (IReadOnlyDictionary<ActionType, double>)new Dictionary<ActionType, double>(average));
                return new NodeStrategyResult(kvp.Key, handMix, average, 0m);
            });

        return new PreflopSolveResult(nodeResults);
    }

    public StrategyQueryResult QueryStrategy(PreflopSolveResult result, PreflopInfoSetKey key, string heroHand)
    {
        var mix = result.QueryStrategy(key, Normalize(heroHand));
        var best = mix.OrderByDescending(k => k.Value).Select(k => (ActionType?)k.Key).FirstOrDefault();
        var ev = result.NodeStrategies.TryGetValue(key, out var node) ? node.EstimatedEvBb : 0m;
        return new StrategyQueryResult(mix, best, ev, key);
    }

    private double Cfr(
        PreflopGameTreeNode node,
        IReadOnlyList<ActionType> history,
        double[] reach,
        IDictionary<PreflopInfoSetKey, InfoSetData> infosets,
        IReadOnlyList<Position> positions,
        int effectiveStackBucket,
        PreflopSolverConfig config)
    {
        if (node.IsTerminal || PreflopRules.IsTerminal(node.State, out _))
        {
            return (double)EvaluateTerminalUtility(node.State, config);
        }

        var actor = node.State.ActingIndex;
        var legalActions = node.Children.Keys
            .OrderBy(action => action, PreflopActionComparer.Instance)
            .Select(ConvertAction)
            .ToArray();

        if (legalActions.Length == 0)
            return (double)EvaluateTerminalUtility(node.State, config);

        var infoSet = BuildInfoSetKey(node.State, history, positions, effectiveStackBucket);
        if (!infosets.TryGetValue(infoSet, out var data))
        {
            data = new InfoSetData(legalActions);
            infosets[infoSet] = data;
        }

        var strategy = RegretMatchingPlus(data.LegalActions, data.RegretSum);
        var actionUtilities = new Dictionary<ActionType, double>(data.LegalActions.Length);
        var nodeUtility = 0d;

        foreach (var childEntry in node.Children.OrderBy(k => k.Key, PreflopActionComparer.Instance))
        {
            var action = ConvertAction(childEntry.Key);
            var nextHistory = new List<ActionType>(history.Count + 1);
            nextHistory.AddRange(history);
            nextHistory.Add(action);
            var nextReach = (double[])reach.Clone();
            nextReach[actor] *= strategy[action];
            var utility = Cfr(childEntry.Value, nextHistory, nextReach, infosets, positions, effectiveStackBucket, config);
            actionUtilities[action] = utility;
            nodeUtility += strategy[action] * utility;
        }

        var actorViewNodeUtility = actor == 0 ? nodeUtility : -nodeUtility;
        var otherReach = 1d;
        for (var player = 0; player < reach.Length; player++)
        {
            if (player != actor)
                otherReach *= reach[player];
        }

        foreach (var action in data.LegalActions)
        {
            var actorViewActionUtility = actor == 0 ? actionUtilities[action] : -actionUtilities[action];
            var updatedRegret = data.RegretSum[action] + ((actorViewActionUtility - actorViewNodeUtility) * otherReach);
            data.RegretSum[action] = Math.Max(0d, updatedRegret);
            data.StrategySum[action] += reach[actor] * strategy[action];
        }

        return nodeUtility;
    }

    private decimal EvaluateTerminalUtility(PreflopPublicState state, PreflopSolverConfig config)
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
            return _terminal.EvaluateAllIn(nodeState, DefaultHeroHandClass, villainRange, config.Rake);
        }

        return _terminal.EvaluateCallToFlop(nodeState, DefaultHeroHandClass, villainRange);
    }

    private static PreflopInfoSetKey BuildInfoSetKey(
        PreflopPublicState state,
        IReadOnlyList<ActionType> history,
        IReadOnlyList<Position> positions,
        int effectiveStackBucket)
    {
        var actingPosition = positions[state.ActingIndex];
        var toCall = Math.Max(0, state.CurrentToCallBb - state.ContribBb[state.ActingIndex]);
        var toCallBucket = (int)Math.Round((decimal)toCall, MidpointRounding.AwayFromZero);
        return new PreflopInfoSetKey(state.PlayerCount, actingPosition, PreflopHistorySignature.Build(history), toCallBucket, effectiveStackBucket);
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
        IReadOnlyDictionary<ActionType, double> strategySum)
    {
        var total = legalActions.Sum(action => strategySum.GetValueOrDefault(action));
        if (total <= 0)
        {
            var uniform = 1d / legalActions.Count;
            return legalActions.ToDictionary(action => action, _ => uniform);
        }

        return legalActions.ToDictionary(action => action, action => strategySum.GetValueOrDefault(action) / total);
    }

    private static Dictionary<ActionType, double> RegretMatchingPlus(
        IReadOnlyList<ActionType> legalActions,
        IReadOnlyDictionary<ActionType, double> regrets)
    {
        var positives = legalActions.ToDictionary(action => action, action => Math.Max(0d, regrets.GetValueOrDefault(action)));
        var positiveSum = positives.Values.Sum();
        if (positiveSum <= 0)
        {
            var uniform = 1d / legalActions.Count;
            return legalActions.ToDictionary(action => action, _ => uniform);
        }

        return legalActions.ToDictionary(action => action, action => positives[action] / positiveSum);
    }

    private static ActionType ConvertAction(PreflopAction action) => action.Type switch
    {
        PreflopActionType.Fold => ActionType.Fold,
        PreflopActionType.Check => ActionType.Check,
        PreflopActionType.Call => ActionType.Call,
        PreflopActionType.RaiseTo => ActionType.Raise,
        PreflopActionType.AllIn => ActionType.AllIn,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action.Type, "Unsupported preflop action.")
    };

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
        public InfoSetData(IReadOnlyList<ActionType> legalActions)
        {
            LegalActions = legalActions;
            RegretSum = legalActions.ToDictionary(action => action, _ => 0d);
            StrategySum = legalActions.ToDictionary(action => action, _ => 0d);
        }

        public IReadOnlyList<ActionType> LegalActions { get; }

        public Dictionary<ActionType, double> RegretSum { get; }

        public Dictionary<ActionType, double> StrategySum { get; }
    }
}
