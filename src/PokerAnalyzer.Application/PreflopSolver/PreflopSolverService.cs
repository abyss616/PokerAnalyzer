using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopSolver;

public interface IPreflopSolverService
{
    PreflopStrategyTables SolvePreflop(PreflopSolverConfig config);
    StrategyQueryResult QueryStrategy(PreflopNodeState state, HoleCards heroHand);
}

public sealed class PreflopSolverService : IPreflopSolverService
{
    private readonly PreflopGameTreeBuilder _treeBuilder = new();
    private readonly Dictionary<string, PreflopStrategyTables> _cache = new();

    public PreflopStrategyTables SolvePreflop(PreflopSolverConfig config)
    {
        var key = $"{config.MaxIterations}:{config.StartingStackBb}:{config.RakeConfig.Percent}:{config.RakeConfig.CapBb}:{config.RakeConfig.NoFlopNoDrop}";
        if (_cache.TryGetValue(key, out var tables)) return tables;

        var root = _treeBuilder.BuildDefaultSubgame(Position.BTN, Position.SB, config.StartingStackBb);
        var terminal = new TerminalUtilityEvaluator(new ApproxMonteCarloContinuationValueProvider(), config.RakeConfig);
        var solver = new CfrPlusSolver(terminal);
        tables = solver.Solve(root, config);
        _cache[key] = tables;
        return tables;
    }

    public StrategyQueryResult QueryStrategy(PreflopNodeState state, HoleCards heroHand)
    {
        var cfg = new PreflopSolverConfig(250, state.EffectiveStackBb);
        var tables = SolvePreflop(cfg);
        var nodeKey = tables.NodeHandActionFrequencies.Keys.FirstOrDefault(k => k.Contains("root")) ?? tables.NodeHandActionFrequencies.Keys.First();
        var handClass = HandRange.ComboToClass(new Combo(heroHand.First, heroHand.Second));
        var mix = tables.NodeHandActionFrequencies[nodeKey].TryGetValue(handClass, out var actions)
            ? actions
            : tables.NodeHandActionFrequencies[nodeKey].Values.First();

        var best = mix.OrderByDescending(kv => kv.Value).First();
        var ev = tables.NodeEvBb.GetValueOrDefault(nodeKey, 0m);
        return new StrategyQueryResult(mix, best.Key, ev);
    }
}
