using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Analysis;

public sealed record DecisionReview(
    int ActionIndex,
    Street Street,
    BettingAction ActualAction,
    Recommendation Recommendation,
    DecisionSeverity Severity,
    string? Notes = null
);
