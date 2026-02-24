using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Engines;

public sealed record Recommendation(
    IReadOnlyList<RecommendedAction> RankedActions,
    string? Explanation = null,
    RecommendedAction? PrimaryAction = null,
    decimal? PrimaryEV = null,
    decimal? ReferenceEV = null,
    string? PrimaryExplanation = null,
    string? ReferenceExplanation = null
)
{
    public RecommendedAction? EffectivePrimaryAction => PrimaryAction ?? RankedActions.FirstOrDefault();
    public string? EffectivePrimaryExplanation => PrimaryExplanation ?? Explanation;
}

public sealed record RecommendedAction(
    ActionType Type,
    ChipAmount? ToAmount = null,
    decimal? EstimatedEv = null
);
