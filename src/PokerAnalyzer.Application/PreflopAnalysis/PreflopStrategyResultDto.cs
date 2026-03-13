namespace PokerAnalyzer.Application.PreflopAnalysis;

public sealed record PreflopStrategyResultDto(
    string InfoSetKey,
    IReadOnlyDictionary<string, decimal> AverageStrategy,
    int IterationsCompleted,
    double RegretMagnitude,
    string StrategySource = "Unknown",
    long ElapsedMilliseconds = 0,
    string SolveMode = "None",
    PreflopLeafEvaluationDetailsDto? LeafEvaluationDetails = null,
    IReadOnlyList<PreflopActionDiagnosticDto>? ActionDiagnostics = null,
    string? ActionValueSupport = null,
    double? BestActionMargin = null,
    double? SeparationScore = null);

public sealed record PreflopActionDiagnosticDto(
    string ActionKey,
    decimal Frequency,
    double Regret,
    double PositiveRegret,
    bool IsBestByFrequency);

public sealed record PreflopLeafEvaluationDetailsDto(
    string HeroHand,
    bool UsedEquityEvaluator,
    bool UsedFallbackEvaluator,
    string EvaluatorType,
    string? AbstractionSource,
    int ActualActiveOpponentCount,
    int? AbstractedOpponentCount,
    string? SyntheticDefenderLabel,
    string? NodeFamily,
    string? HeroPosition,
    string? VillainPosition,
    bool IsHeadsUp,
    string? RangeDescription,
    string? RangeDetail,
    double? FoldProbability,
    double? ContinueProbability,
    string? RootActionType,
    double? ImmediateWinComponent,
    double? ContinueComponent,
    double? ContinueBranchUtility,
    int? FilteredCombos,
    double? HeroEquity,
    double? HeroUtility,
    double? EquityVsRangePercentile,
    string? HandClass,
    string? BlockerSummary,
    string? RationaleSummary,
    string? FallbackReason,
    string? DisplaySummary);
