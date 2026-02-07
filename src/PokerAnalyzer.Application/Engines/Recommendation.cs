using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Engines;

public sealed record Recommendation(
    IReadOnlyList<RecommendedAction> RankedActions,
    string? Explanation = null
);

public sealed record RecommendedAction(
    ActionType Type,
    ChipAmount? ToAmount = null,
    decimal? EstimatedEv = null
);
