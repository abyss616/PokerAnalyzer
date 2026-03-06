using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines.SolverTraining;

public sealed record SolverTrajectory(
    IReadOnlyList<SolverTrajectoryStep> Steps,
    SolverHandState TerminalState,
    int ChanceSamplesTaken);
