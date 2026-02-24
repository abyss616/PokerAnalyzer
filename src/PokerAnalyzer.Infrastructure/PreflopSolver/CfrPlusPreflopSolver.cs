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
        var nodes = new PreflopGameTreeBuilder(config.PlayerCount, config.EffectiveStackBb, 0.5m, 1m, config.Rake, config.ResolveSizing()).Build();
        var result = new Dictionary<PreflopInfoSetKey, NodeStrategyResult>();
        foreach (var node in nodes)
            result[node.InfoSet] = SolveNode(node, config);
        return new PreflopSolveResult(result);
    }

    public StrategyQueryResult QueryStrategy(PreflopSolveResult result, PreflopInfoSetKey key, string heroHand)
    {
        var mix = result.QueryStrategy(key, Normalize(heroHand));
        var best = mix.OrderByDescending(k => k.Value).Select(k => (ActionType?)k.Key).FirstOrDefault();
        var ev = result.NodeStrategies.TryGetValue(key, out var node) ? node.EstimatedEvBb : 0m;
        return new StrategyQueryResult(mix, best, ev, key);
    }

    private NodeStrategyResult SolveNode(PreflopNode node, PreflopSolverConfig config)
    {
        var range = PreflopRange.BuildClassDistribution();
        var hands = range.Keys.Select(h => h.Label).OrderBy(x => x).ToArray();
        var regrets = hands.ToDictionary(h => h, _ => node.LegalActions.ToDictionary(a => a, _ => 0d));
        var strategySum = hands.ToDictionary(h => h, _ => node.LegalActions.ToDictionary(a => a, _ => 0d));

        for (var iter = 1; iter <= config.Iterations; iter++)
        {
            if (config.EnableParallelSolve)
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = config.ResolveMaxDegreeOfParallelism() };
                Parallel.ForEach(hands, options, hand => UpdateHandRegrets(node, hand, config, regrets[hand], strategySum[hand]));
            }
            else
            {
                foreach (var hand in hands)
                    UpdateHandRegrets(node, hand, config, regrets[hand], strategySum[hand]);
            }
        }

        var handMix = hands.ToDictionary(
            h => h,
            h => (IReadOnlyDictionary<ActionType, double>)node.LegalActions.ToDictionary(
                a => a,
                a =>
                {
                    var total = strategySum[h].Values.Sum();
                    return total <= 0 ? 1d / node.LegalActions.Count : strategySum[h][a] / total;
                }));
        var popMix = node.LegalActions.ToDictionary(a => a, a => handMix.Values.Average(m => m[a]));
        var ev = (decimal)hands.Average(h => node.LegalActions.Sum(a => handMix[h][a] * (double)EvaluateAction(node, h, a, config)));
        return new NodeStrategyResult(node.InfoSet, handMix, popMix, ev);
    }

    private void UpdateHandRegrets(
        PreflopNode node,
        string hand,
        PreflopSolverConfig config,
        Dictionary<ActionType, double> regrets,
        Dictionary<ActionType, double> strategySum)
    {
        var strat = RegretMatchingPlus(regrets);
        foreach (var action in node.LegalActions)
            strategySum[action] += strat[action];

        var actionUtil = node.LegalActions.ToDictionary(a => a, a => (double)EvaluateAction(node, hand, a, config));
        var nodeUtil = node.LegalActions.Sum(a => strat[a] * actionUtil[a]);
        foreach (var action in node.LegalActions)
            regrets[action] = Math.Max(0, regrets[action] + (actionUtil[action] - nodeUtil));
    }

    private decimal EvaluateAction(PreflopNode node, string hand, ActionType action, PreflopSolverConfig config)
    {
        var villainRange = PreflopRange.BuildClassDistribution().ToDictionary(k => k.Key.Label, v => v.Value);
        return action switch
        {
            ActionType.Fold => _terminal.EvaluateFold(node.State, true),
            ActionType.Check => _terminal.EvaluateCallToFlop(node.State with { ToCallBb = 0 }, hand, villainRange) * 0.95m,
            ActionType.Call => _terminal.EvaluateCallToFlop(node.State, hand, villainRange),
            ActionType.Raise => _terminal.EvaluateCallToFlop(node.State with { PotBb = node.State.PotBb + node.RaiseToByActionBb.GetValueOrDefault(ActionType.Raise, 0m) }, hand, villainRange),
            ActionType.AllIn => _terminal.EvaluateAllIn(node.State, hand, villainRange, config.Rake),
            _ => 0m
        };
    }

    private static Dictionary<ActionType, double> RegretMatchingPlus(IReadOnlyDictionary<ActionType, double> regrets)
    {
        var positive = regrets.ToDictionary(k => k.Key, v => Math.Max(0, v.Value));
        var sum = positive.Values.Sum();
        return sum <= 0 ? regrets.Keys.ToDictionary(a => a, _ => 1d / regrets.Count) : positive.ToDictionary(k => k.Key, v => v.Value / sum);
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
        Rank.Two => '2', Rank.Three => '3', Rank.Four => '4', Rank.Five => '5', Rank.Six => '6', Rank.Seven => '7',
        Rank.Eight => '8', Rank.Nine => '9', Rank.Ten => 'T', Rank.Jack => 'J', Rank.Queen => 'Q', Rank.King => 'K', Rank.Ace => 'A',
        _ => '?'
    };
}
