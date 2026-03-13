namespace PokerAnalyzer.Application.PreflopAnalysis;

public sealed record PreflopStrategyResultDto(
    string InfoSetKey,
    IReadOnlyDictionary<string, decimal> AverageStrategy,
    int IterationsCompleted,
    double RegretMagnitude,
    string StrategySource = "Unknown",
    long ElapsedMilliseconds = 0,
    string SolveMode = "None",
    PreflopLeafEvaluationDetailsDto? LeafEvaluationDetails = null);

public sealed record PreflopLeafEvaluationDetailsDto(
    bool UsedEquityEvaluator,
    bool UsedFallbackEvaluator,
    string EvaluatorType,
    string? NodeFamily,
    string? HeroPosition,
    string? VillainPosition,
    bool IsHeadsUp,
    string? RangeDescription,
    string? RangeDetail,
    int? FilteredCombos,
    double? HeroEquity,
    double? HeroUtility,
    string? FallbackReason,
    string? DisplaySummary);
