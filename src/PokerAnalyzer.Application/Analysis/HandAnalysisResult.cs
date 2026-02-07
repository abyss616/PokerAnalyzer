namespace PokerAnalyzer.Application.Analysis;

public sealed record HandAnalysisResult(
    Guid HandId,
    IReadOnlyList<DecisionReview> Decisions
);
