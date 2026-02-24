using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

/// <summary>
/// Deterministic approximation model for continuation EV.
/// Monte Carlo reference is intentionally not used inside CFR loops.
/// </summary>
public sealed class ApproxMonteCarloContinuationValueProvider : IContinuationValueProvider
{
    public decimal EstimateFlopContinuationValueBb(PreflopNodeState node, string heroHandClass, IReadOnlyDictionary<string, double> villainRange)
    {
        var hero = HandClass.Parse(heroHandClass);
        var heroStrength = Strength(hero);
        var villainStrength = villainRange.Sum(v => Strength(HandClass.Parse(v.Key)) * (decimal)v.Value);
        var equity = heroStrength > villainStrength ? 0.6m : heroStrength < villainStrength ? 0.4m : 0.5m;
        var rake = node.PotBb * 0.01m;
        return (equity * node.PotBb) - (node.ToCallBb * 0.35m) - rake;
    }

    private static decimal Strength(HandClass hc)
    {
        var high = (int)hc.High;
        var low = (int)hc.Low;
        var pairBonus = hc.High == hc.Low ? 18 : 0;
        var suitedBonus = hc.Suited == true ? 3 : 0;
        return high + (low * 0.4m) + pairBonus + suitedBonus;
    }
}
