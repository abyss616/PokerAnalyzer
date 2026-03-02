using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopSolver;

public sealed record RakeConfig(decimal Percent, decimal CapBb, bool NoFlopNoDrop);

public sealed record PreflopSolverConfig(
    int MaxIterations = 200,
    decimal StartingStackBb = 100m,
    RakeConfig? Rake = null)
{
    public RakeConfig RakeConfig { get; init; } = Rake ?? new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true);
}

public sealed record PreflopNodeState(
    string NodeId,
    Position HeroPosition,
    Position VillainPosition,
    decimal EffectiveStackBb,
    decimal PotBb,
    bool IsHeroInPosition);

public sealed record ActionMix(string Action, decimal Frequency, decimal? EstimatedEv = null);

public sealed record StrategyQueryResult(
    IReadOnlyDictionary<string, decimal> Frequencies,
    string BestAction,
    decimal EstimatedEv);

public readonly record struct Combo(Card C1, Card C2)
{
    public override string ToString() => $"{C1}{C2}";

    public bool SharesCardWith(Combo other)
        => C1.Equals(other.C1) || C1.Equals(other.C2) || C2.Equals(other.C1) || C2.Equals(other.C2);
}

public sealed record PreflopStrategyTables(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>> NodeHandActionFrequencies,
    IReadOnlyDictionary<string, decimal> NodeEvBb);
