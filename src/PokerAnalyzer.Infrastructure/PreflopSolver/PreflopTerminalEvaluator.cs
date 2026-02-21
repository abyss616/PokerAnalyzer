using System.Collections.Concurrent;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed class PreflopTerminalEvaluator
{
    private readonly IContinuationValueProvider _continuation;
    private readonly ConcurrentDictionary<string, decimal> _equityCache = new();

    public PreflopTerminalEvaluator(IContinuationValueProvider continuation)
    {
        _continuation = continuation;
    }

    public decimal EvaluateFold(PreflopNodeState node, bool heroFolds)
        => heroFolds ? -node.HeroCommittedBb : node.PotBb - node.HeroCommittedBb;

    public decimal EvaluateAllIn(PreflopNodeState node, string heroClass, IReadOnlyDictionary<string, double> villainRange, RakeConfig rake)
    {
        var key = $"{heroClass}|{string.Join(',', villainRange.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value:F4}"))}|{node.EffectiveStackBb:F2}|{node.PotBb:F2}|{rake.Percent:F3}|{rake.CapBb:F2}|{rake.NoFlopNoDrop}";
        return _equityCache.GetOrAdd(key, _ =>
        {
            var heroStrength = HandStrength(heroClass);
            var weightedVillain = villainRange.Sum(v => HandStrength(v.Key) * (decimal)v.Value);
            var equity = heroStrength > weightedVillain ? 0.57m : heroStrength < weightedVillain ? 0.43m : 0.5m;
            var gross = (equity * (node.PotBb + (2 * node.EffectiveStackBb))) - node.EffectiveStackBb;
            var rakeBb = rake.NoFlopNoDrop ? 0m : Math.Min((rake.Percent * (node.PotBb + 2 * node.EffectiveStackBb)), rake.CapBb);
            return gross - rakeBb;
        });
    }

    public decimal EvaluateCallToFlop(PreflopNodeState node, string heroClass, IReadOnlyDictionary<string, double> villainRange)
        => _continuation.EstimateFlopContinuationValueBb(node, heroClass, villainRange);

    private static decimal HandStrength(string handClass)
    {
        var hc = HandClass.Parse(handClass);
        var high = (int)hc.High;
        var low = (int)hc.Low;
        var pair = hc.High == hc.Low ? 16 : 0;
        var suited = hc.Suited == true ? 2 : 0;
        return high + (low * 0.35m) + pair + suited;
    }
}
