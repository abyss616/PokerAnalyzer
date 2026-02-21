namespace PokerAnalyzer.Application.PreflopSolver;

public interface IContinuationValueProvider
{
    decimal EstimateFlopContinuationEvBb(Combo heroCombo, IReadOnlyDictionary<Combo, decimal> villainRange, PreflopNode node, int seed);
}

/// <summary>Approximate continuation model (placeholder; not a full postflop solver).</summary>
public sealed class ApproxMonteCarloContinuationValueProvider : IContinuationValueProvider
{
    public decimal EstimateFlopContinuationEvBb(Combo heroCombo, IReadOnlyDictionary<Combo, decimal> villainRange, PreflopNode node, int seed)
    {
        var heroStrength = ComboStrength.ByChenApprox(heroCombo);
        var vil = villainRange.Sum(kv => kv.Value * ComboStrength.ByChenApprox(kv.Key));
        var positionEdge = node.HeroInPosition ? 0.12m : -0.12m;
        return (heroStrength - vil) * 0.08m * node.PotBb + positionEdge;
    }
}

public static class ComboStrength
{
    public static decimal ByChenApprox(Combo c)
    {
        var r1 = (int)c.C1.Rank + 2;
        var r2 = (int)c.C2.Rank + 2;
        var hi = Math.Max(r1, r2);
        var lo = Math.Min(r1, r2);
        var pair = r1 == r2;
        var suited = c.C1.Suit == c.C2.Suit;
        var gap = hi - lo - 1;
        var baseVal = pair ? hi * 1.6m : hi;
        if (suited) baseVal += 1.8m;
        baseVal -= Math.Max(0, gap - 1) * 0.7m;
        return Math.Max(1m, baseVal);
    }
}

public sealed class TerminalUtilityEvaluator
{
    private readonly IContinuationValueProvider _continuation;
    private readonly RakeConfig _rake;
    private readonly Dictionary<string, decimal> _equityCache = new();

    public TerminalUtilityEvaluator(IContinuationValueProvider continuation, RakeConfig rake)
    {
        _continuation = continuation;
        _rake = rake;
    }

    public decimal Evaluate(
        TerminalType terminalType,
        Combo hero,
        IReadOnlyDictionary<Combo, decimal> villainRange,
        PreflopNode node,
        int commonSeed)
    {
        return terminalType switch
        {
            TerminalType.Fold => node.PotBb * 0.5m,
            TerminalType.AllIn => EvaluateAllIn(hero, villainRange, node, commonSeed),
            TerminalType.FlopCall => _continuation.EstimateFlopContinuationEvBb(hero, villainRange, node, commonSeed),
            _ => 0m
        };
    }

    private decimal EvaluateAllIn(Combo hero, IReadOnlyDictionary<Combo, decimal> villainRange, PreflopNode node, int commonSeed)
    {
        var key = $"{hero}|{RangeKey(villainRange)}|{node.EffectiveStackBb}|{node.PotBb}|{_rake.Percent}|{_rake.CapBb}|{_rake.NoFlopNoDrop}";
        if (_equityCache.TryGetValue(key, out var ev)) return ev;

        // approximate equity from relative combo-strength with common random number perturbation.
        var heroS = ComboStrength.ByChenApprox(hero);
        var vilS = villainRange.Sum(x => x.Value * ComboStrength.ByChenApprox(x.Key));
        var rawEquity = Math.Clamp(0.5m + (heroS - vilS) / 40m, 0.05m, 0.95m);
        var noise = ((commonSeed % 100) - 50) / 5000m;
        var equity = Math.Clamp(rawEquity + noise, 0.01m, 0.99m);
        var gross = equity * node.PotBb - (1m - equity) * node.EffectiveStackBb;
        var rake = _rake.NoFlopNoDrop ? 0m : Math.Min(_rake.CapBb, node.PotBb * _rake.Percent);
        ev = gross - rake;
        _equityCache[key] = ev;
        return ev;
    }

    private static string RangeKey(IReadOnlyDictionary<Combo, decimal> range)
        => string.Join(";", range.OrderBy(k => k.Key.ToString()).Take(20).Select(kv => $"{kv.Key}:{Math.Round(kv.Value, 4)}"));
}
