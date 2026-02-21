using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

/// <summary>
/// Approximate continuation model (not a postflop solver).
/// Uses lightweight Monte Carlo rollouts vs sampled villain classes.
/// </summary>
public sealed class ApproxMonteCarloContinuationValueProvider : IContinuationValueProvider
{
    private readonly Random _random = new(17);

    public decimal EstimateFlopContinuationValueBb(PreflopNodeState node, string heroHandClass, IReadOnlyDictionary<string, double> villainRange)
    {
        const int iterations = 120;
        var hero = HandClass.Parse(heroHandClass);
        var heroStrength = Strength(hero);

        decimal equitySum = 0;
        for (var i = 0; i < iterations; i++)
        {
            var villain = SampleClass(villainRange);
            var villainStrength = Strength(HandClass.Parse(villain));
            var equity = heroStrength > villainStrength ? 0.62m : heroStrength < villainStrength ? 0.38m : 0.5m;
            equitySum += equity;
        }

        var avgEquity = equitySum / iterations;
        var rake = node.PotBb * 0.5m * (decimal)Math.Min((double)node.PotBb * 0.01, 0.05);
        return (avgEquity * node.PotBb) - (node.ToCallBb * 0.35m) - rake;
    }

    private string SampleClass(IReadOnlyDictionary<string, double> range)
    {
        var p = _random.NextDouble();
        double cum = 0;
        foreach (var kvp in range)
        {
            cum += kvp.Value;
            if (p <= cum)
                return kvp.Key;
        }

        return range.Keys.FirstOrDefault() ?? "72O";
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
