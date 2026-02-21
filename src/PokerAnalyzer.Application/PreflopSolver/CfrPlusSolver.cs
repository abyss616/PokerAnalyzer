namespace PokerAnalyzer.Application.PreflopSolver;

public sealed class CfrPlusSolver
{
    private readonly TerminalUtilityEvaluator _terminal;

    public CfrPlusSolver(TerminalUtilityEvaluator terminal) => _terminal = terminal;

    public PreflopStrategyTables Solve(PreflopNode root, PreflopSolverConfig config)
    {
        var infosets = new Dictionary<string, InfoSet>();
        var heroRange = HandRange.UniformDistribution(HandRange.AllCombos);
        var vilRange = HandRange.UniformDistribution(HandRange.AllCombos);

        for (var i = 1; i <= config.MaxIterations; i++)
        {
            Traverse(root, heroRange, vilRange, infosets, i);
        }

        var nodeHandAction = infosets.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>)kv.Value.AverageStrategyByClass(
                HandRange.AllClasses));
        var nodeEv = infosets.ToDictionary(kv => kv.Key, kv => kv.Value.LastNodeEv);
        return new PreflopStrategyTables(nodeHandAction, nodeEv);
    }

    private decimal Traverse(
        PreflopNode node,
        IReadOnlyDictionary<Combo, decimal> heroRange,
        IReadOnlyDictionary<Combo, decimal> villainRange,
        Dictionary<string, InfoSet> infosets,
        int iter)
    {
        if (node.TerminalType is not null)
        {
            var ev = heroRange.Sum(h => h.Value * _terminal.Evaluate(node.TerminalType.Value, h.Key, villainRange, node, iter));
            return ev;
        }

        if (!infosets.TryGetValue(node.Id, out var info))
        {
            info = new InfoSet(node.Actions);
            infosets[node.Id] = info;
        }

        var actionUtils = new Dictionary<string, decimal>();
        var strategyByClass = new Dictionary<string, IReadOnlyDictionary<string, decimal>>();

        foreach (var handClass in HandRange.AllClasses)
        {
            var combos = HandRange.ClassToCombos(handClass);
            var strategy = info.GetStrategy(handClass);
            strategyByClass[handClass] = strategy;
            foreach (var a in node.Actions)
            {
                var child = node.Children[a];
                actionUtils[a] = actionUtils.TryGetValue(a, out var av)
                    ? av + Traverse(child, heroRange, villainRange, infosets, iter) * combos.Count
                    : Traverse(child, heroRange, villainRange, infosets, iter) * combos.Count;
            }

            var nodeUtility = node.Actions.Sum(a => strategy[a] * actionUtils[a]);
            foreach (var a in node.Actions)
            {
                var regret = actionUtils[a] - nodeUtility;
                info.AddRegret(handClass, a, Math.Max(0m, regret));
                info.AddStrategySum(handClass, a, strategy[a]);
            }

            info.LastNodeEv = nodeUtility;
        }

        return info.LastNodeEv;
    }

    private sealed class InfoSet
    {
        private readonly IReadOnlyList<string> _actions;
        private readonly Dictionary<string, Dictionary<string, decimal>> _regret = new();
        private readonly Dictionary<string, Dictionary<string, decimal>> _sum = new();
        public decimal LastNodeEv { get; set; }

        public InfoSet(IReadOnlyList<string> actions) => _actions = actions;

        public IReadOnlyDictionary<string, decimal> GetStrategy(string handClass)
        {
            if (!_regret.TryGetValue(handClass, out var r))
            {
                r = _actions.ToDictionary(a => a, _ => 0m);
                _regret[handClass] = r;
            }

            var positive = _actions.Select(a => Math.Max(0m, r[a])).ToArray();
            var sum = positive.Sum();
            if (sum <= 0m) return _actions.ToDictionary(a => a, _ => 1m / _actions.Count);
            var outp = new Dictionary<string, decimal>(_actions.Count);
            for (var i = 0; i < _actions.Count; i++)
                outp[_actions[i]] = positive[i] / sum;
            return outp;
        }

        public void AddRegret(string hc, string action, decimal value)
        {
            if (!_regret.TryGetValue(hc, out var reg))
            {
                reg = _actions.ToDictionary(a => a, _ => 0m);
                _regret[hc] = reg;
            }
            reg[action] += value;
        }

        public void AddStrategySum(string hc, string action, decimal value)
        {
            if (!_sum.TryGetValue(hc, out var s))
            {
                s = _actions.ToDictionary(a => a, _ => 0m);
                _sum[hc] = s;
            }
            s[action] += value;
        }

        public Dictionary<string, IReadOnlyDictionary<string, decimal>> AverageStrategyByClass(IReadOnlyList<string> classes)
        {
            var outp = new Dictionary<string, IReadOnlyDictionary<string, decimal>>();
            foreach (var hc in classes)
            {
                if (!_sum.TryGetValue(hc, out var s))
                    s = _actions.ToDictionary(a => a, _ => 0m);
                var total = s.Values.Sum();
                outp[hc] = total <= 0m
                    ? _actions.ToDictionary(a => a, _ => 1m / _actions.Count)
                    : _actions.ToDictionary(a => a, a => s[a] / total);
            }

            return outp;
        }
    }
}
