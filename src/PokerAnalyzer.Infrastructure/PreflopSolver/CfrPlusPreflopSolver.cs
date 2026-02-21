using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed class CfrPlusPreflopSolver
{
    private readonly PreflopTerminalEvaluator _terminal;

    public CfrPlusPreflopSolver(PreflopTerminalEvaluator terminal)
    {
        _terminal = terminal;
    }

    public PreflopSolveResult SolvePreflop(PreflopSolverConfig config)
    {
        var nodes = PreflopGameTree.Build(config);
        var result = new Dictionary<string, NodeStrategyResult>();

        foreach (var node in nodes)
            result[node.NodeId] = SolveNode(node, config);

        return new PreflopSolveResult(result);
    }

    public StrategyQueryResult QueryStrategy(PreflopSolveResult result, PreflopNodeState state, string heroHand)
    {
        var mix = result.QueryStrategy(state.NodeId, Normalize(heroHand));
        var best = mix.OrderByDescending(k => k.Value).Select(k => (ActionType?)k.Key).FirstOrDefault();
        var ev = result.NodeStrategies.TryGetValue(state.NodeId, out var node) ? node.EstimatedEvBb : 0m;
        return new StrategyQueryResult(mix, best, ev, state.NodeId);
    }

    private NodeStrategyResult SolveNode(PreflopNode node, PreflopSolverConfig config)
    {
        var range = PreflopRange.BuildClassDistribution();
        var hands = range.Keys.Select(h => h.Label).OrderBy(x => x).ToArray();
        var regrets = hands.ToDictionary(h => h, _ => node.LegalActions.ToDictionary(a => a, _ => 0d));
        var strategySum = hands.ToDictionary(h => h, _ => node.LegalActions.ToDictionary(a => a, _ => 0d));

        for (var iter = 1; iter <= config.Iterations; iter++)
        {
            foreach (var hand in hands)
            {
                var strat = RegretMatchingPlus(regrets[hand]);
                foreach (var action in node.LegalActions)
                    strategySum[hand][action] += strat[action];

                var actionUtil = node.LegalActions.ToDictionary(a => a, a => (double)EvaluateAction(node, hand, a, config));
                var nodeUtil = node.LegalActions.Sum(a => strat[a] * actionUtil[a]);

                foreach (var action in node.LegalActions)
                {
                    var r = regrets[hand][action] + (actionUtil[action] - nodeUtil);
                    regrets[hand][action] = Math.Max(0, r);
                }
            }
        }

        var handMix = new Dictionary<string, IReadOnlyDictionary<ActionType, double>>();
        foreach (var hand in hands)
        {
            var total = strategySum[hand].Values.Sum();
            handMix[hand] = node.LegalActions.ToDictionary(a => a, a => total <= 0 ? 1d / node.LegalActions.Count : strategySum[hand][a] / total);
        }

        var popMix = node.LegalActions.ToDictionary(a => a, a => handMix.Values.Average(m => m[a]));
        var ev = (decimal)hands.Average(h => ExpectedUtility(node, h, handMix[h], config));

        return new NodeStrategyResult(node.NodeId, handMix, popMix, ev);
    }

    private double ExpectedUtility(PreflopNode node, string hand, IReadOnlyDictionary<ActionType, double> strategy, PreflopSolverConfig config)
        => node.LegalActions.Sum(a => strategy[a] * (double)EvaluateAction(node, hand, a, config));

    private decimal EvaluateAction(PreflopNode node, string hand, ActionType action, PreflopSolverConfig config)
    {
        var villainRange = PreflopRange.BuildClassDistribution().ToDictionary(k => k.Key.Label, v => v.Value);
        return action switch
        {
            ActionType.Fold => _terminal.EvaluateFold(node.State, heroFolds: true),
            ActionType.Check => _terminal.EvaluateCallToFlop(node.State with { ToCallBb = 0 }, hand, villainRange) * 0.92m,
            ActionType.Call => _terminal.EvaluateCallToFlop(node.State, hand, villainRange),
            ActionType.Raise => EvaluateRaise(node, hand, villainRange, config),
            ActionType.AllIn => _terminal.EvaluateAllIn(node.State, hand, villainRange, config.Rake),
            _ => 0m
        };
    }

    private decimal EvaluateRaise(PreflopNode node, string hand, IReadOnlyDictionary<string, double> villainRange, PreflopSolverConfig config)
    {
        var target = node.RaiseToByActionBb.TryGetValue(ActionType.Raise, out var toBb) ? toBb : 0m;
        var commitPct = config.EffectiveStackBb <= 0 ? 0 : target / config.EffectiveStackBb;
        if (config.EffectiveStackBb <= 30 || commitPct >= 0.6m)
            return _terminal.EvaluateAllIn(node.State with { EffectiveStackBb = config.EffectiveStackBb }, hand, villainRange, config.Rake);

        var foldEq = 0.28m + (HandClass.Parse(hand).High >= Rank.Jack ? 0.1m : 0m);
        var immediate = foldEq * (node.State.PotBb);
        var post = (1 - foldEq) * _terminal.EvaluateCallToFlop(node.State with { PotBb = node.State.PotBb + target }, hand, villainRange);
        return immediate + post;
    }

    private static Dictionary<ActionType, double> RegretMatchingPlus(IReadOnlyDictionary<ActionType, double> regrets)
    {
        var positive = regrets.ToDictionary(k => k.Key, v => Math.Max(0, v.Value));
        var sum = positive.Values.Sum();
        return sum <= 0
            ? regrets.Keys.ToDictionary(a => a, _ => 1d / regrets.Count)
            : positive.ToDictionary(k => k.Key, v => v.Value / sum);
    }

    private static string Normalize(string hand)
    {
        var h = hand.Trim().ToUpperInvariant();
        if (h.Length == 4)
        {
            var hc = HoleCards.Parse(h.ToLowerInvariant());
            var ranks = new[] { hc.First.Rank, hc.Second.Rank }.OrderByDescending(r => (int)r).ToArray();
            var suited = hc.First.Suit == hc.Second.Suit;
            return ranks[0] == ranks[1]
                ? $"{RankChar(ranks[0])}{RankChar(ranks[1])}"
                : $"{RankChar(ranks[0])}{RankChar(ranks[1])}{(suited ? "S" : "O")}";
        }

        return h;
    }

    private static char RankChar(Rank rank) => rank switch
    {
        Rank.Two => '2', Rank.Three => '3', Rank.Four => '4', Rank.Five => '5', Rank.Six => '6', Rank.Seven => '7',
        Rank.Eight => '8', Rank.Nine => '9', Rank.Ten => 'T', Rank.Jack => 'J', Rank.Queen => 'Q', Rank.King => 'K', Rank.Ace => 'A',
        _ => '?'
    };
}
