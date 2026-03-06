using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines.SolverTraining;

public sealed record SolverStrategyRow(
    string InfoSetCanonicalKey,
    IReadOnlyList<LegalAction> LegalActions,
    IReadOnlyList<double> BehaviorProbabilities);
