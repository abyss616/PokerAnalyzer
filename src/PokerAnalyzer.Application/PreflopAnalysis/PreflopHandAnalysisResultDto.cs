namespace PokerAnalyzer.Application.PreflopAnalysis;

public sealed record PreflopHandAnalysisResultDto(
    string HandNumber,
    string CanonicalPreflopSolverNode,
    string HeroLegalActions,
    string CurrentMixedStrategy,
    string ActualActionVsRecommendation)
{
    public const string NotYetImplemented = "NOT YET IMPLEMENTED";
}
