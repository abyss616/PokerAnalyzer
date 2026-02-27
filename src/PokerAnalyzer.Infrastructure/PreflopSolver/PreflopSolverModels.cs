using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record RakeConfig(decimal Percent, decimal CapBb, bool NoFlopNoDrop);

public sealed record PreflopSizingConfig(
    IReadOnlyList<decimal> OpenSizesBb,
    IReadOnlyList<decimal> ThreeBetSizeMultipliers,
    IReadOnlyList<decimal> FourBetSizeMultipliers,
    decimal JamThresholdStackBb = 25m,
    bool AllowExplicitJam = true)
{
    public static PreflopSizingConfig Default { get; } = new(
        OpenSizesBb: [2.0m, 2.5m],
        ThreeBetSizeMultipliers: [3.0m],
        FourBetSizeMultipliers: [2.2m]);
}

public sealed record RaiseSizingAbstraction(
    IReadOnlyList<decimal> OpenSizesBb,
    IReadOnlyList<decimal> ThreeBetSizesBb,
    IReadOnlyList<decimal> FourBetSizesBb,
    decimal JamThresholdStackBb = 25m)
{
    public static RaiseSizingAbstraction Default { get; } = new([2.0m, 2.5m], [3m], [2.2m], 25m);

    public PreflopSizingConfig ToPreflopSizingConfig()
        => new(OpenSizesBb, ThreeBetSizesBb, FourBetSizesBb, JamThresholdStackBb, true);
}

public sealed record PreflopSolverConfig(
    int Iterations,
    decimal EffectiveStackBb,
    RakeConfig Rake,
    int PlayerCount = 2,
    RaiseSizingAbstraction? Sizing = null,
    bool EnableParallelSolve = false,
    int MaxDegreeOfParallelism = 0,
    int MaxTreeDepth = 24)
{
    public int ResolveMaxDegreeOfParallelism()
        => MaxDegreeOfParallelism <= 0 ? Environment.ProcessorCount : MaxDegreeOfParallelism;

    public PreflopSizingConfig ResolveSizing()
        => (Sizing ?? RaiseSizingAbstraction.Default).ToPreflopSizingConfig();
}

public sealed record PreflopInfoSetKey(
    int PlayerCount,
    Position ActingPosition,
    string HistorySignature,
    int ToCallBb,
    int EffectiveStackBb,
    string HeroHandClass = "");

public sealed record PreflopNodeState(
    PreflopInfoSetKey InfoSet,
    decimal PotBb,
    decimal ToCallBb,
    decimal HeroCommittedBb,
    decimal VillainCommittedBb,
    decimal EffectiveStackBb);

public sealed record NodeStrategyResult(
    PreflopInfoSetKey InfoSet,
    IReadOnlyDictionary<string, IReadOnlyDictionary<ActionType, double>> HandMix,
    IReadOnlyDictionary<ActionType, double> PopulationMix,
    decimal EstimatedEvBb);

public sealed record PreflopSolveResult(
    IReadOnlyDictionary<PreflopInfoSetKey, NodeStrategyResult> NodeStrategies)
{
    public IReadOnlyDictionary<ActionType, double> QueryStrategy(PreflopInfoSetKey key, string handClass)
    {
        if (!NodeStrategies.TryGetValue(key, out var node))
            return new Dictionary<ActionType, double>();

        return node.HandMix.TryGetValue(handClass.ToUpperInvariant(), out var mix)
            ? mix
            : new Dictionary<ActionType, double>();
    }
}

public sealed record StrategyQueryResult(
    IReadOnlyDictionary<ActionType, double> ActionFrequencies,
    ActionType? BestAction,
    decimal EstimatedEvBb,
    PreflopInfoSetKey InfoSet);

public interface IPreflopStrategyStore
{
    StrategyQueryResult Lookup(PreflopInfoSetKey key, string heroHand);
}

public interface IContinuationValueProvider
{
    decimal EstimateFlopContinuationValueBb(PreflopNodeState node, string heroHandClass, IReadOnlyDictionary<string, double> villainRange);
}
