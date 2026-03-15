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
    IReadOnlyList<PreflopActionExplanationDto>? ActionExplanations = null,
    IReadOnlyList<PreflopActionDiagnosticDto>? ActionDiagnostics = null,
    string? ActionValueSupport = null,
    double? BestActionMargin = null,
    double? SeparationScore = null);

public sealed record PreflopActionExplanationDto(
    string ActionKey,
    PreflopActionValueSummaryDto? AggregatedActionValue,
    PreflopLeafEvaluationDetailsDto? LeafEvaluationDetails = null);

public sealed record PreflopActionDiagnosticDto(
    string ActionKey,
    decimal Frequency,
    decimal CurrentPolicyFrequency,
    double Regret,
    double PositiveRegret,
    double? AggregatedActionEv,
    bool IsBestByFrequency);

public sealed record PreflopActionValueSummaryDto(
    double AverageUtility,
    double TotalUtility,
    int Samples);

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
    string? DisplaySummary,
    string? RootEvaluatorMode = null,
    int? RootActiveOpponentCount = null,
    int? LeafActiveOpponentCount = null,
    int? SampledTrajectoryDepth = null,
    bool? UsedDirectAbstractionShortcut = null,
    long? TraversalMilliseconds = null,
    long? LeafEvaluationMilliseconds = null,
    string? ActivePopulationProfile = null);
