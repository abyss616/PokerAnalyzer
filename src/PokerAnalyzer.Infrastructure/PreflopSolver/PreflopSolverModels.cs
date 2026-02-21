using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record RakeConfig(decimal Percent, decimal CapBb, bool NoFlopNoDrop);

public sealed record PreflopSolverConfig(
    int Iterations,
    decimal EffectiveStackBb,
    RakeConfig Rake,
    bool UseLegacyMonteCarlo = false);

public sealed record PreflopNodeState(
    string NodeId,
    Position HeroPosition,
    Position VillainPosition,
    decimal PotBb,
    decimal ToCallBb,
    decimal HeroCommittedBb,
    decimal VillainCommittedBb,
    bool IsLimpedPot,
    bool IsThreeBetPot,
    bool IsFourBetPot,
    decimal EffectiveStackBb);

public sealed record NodeStrategyResult(
    string NodeId,
    IReadOnlyDictionary<string, IReadOnlyDictionary<ActionType, double>> HandMix,
    IReadOnlyDictionary<ActionType, double> PopulationMix,
    decimal EstimatedEvBb);

public sealed record PreflopSolveResult(
    IReadOnlyDictionary<string, NodeStrategyResult> NodeStrategies)
{
    public IReadOnlyDictionary<ActionType, double> QueryStrategy(string nodeId, string handClass)
    {
        if (!NodeStrategies.TryGetValue(nodeId, out var node))
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
    string NodeId);

public interface IContinuationValueProvider
{
    decimal EstimateFlopContinuationValueBb(PreflopNodeState node, string heroHandClass, IReadOnlyDictionary<string, double> villainRange);
}
