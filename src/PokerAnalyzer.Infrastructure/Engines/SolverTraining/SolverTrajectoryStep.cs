using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines.SolverTraining;

public sealed record SolverTrajectoryStep(
    string PreActionStateSignature,
    string InfoSetCanonicalKey,
    PlayerId ActingPlayerId,
    IReadOnlyList<LegalAction> LegalActions,
    LegalAction SampledAction,
    IReadOnlyList<double> StrategyProbabilities,
    bool UsedFallbackInfoSetKey);
