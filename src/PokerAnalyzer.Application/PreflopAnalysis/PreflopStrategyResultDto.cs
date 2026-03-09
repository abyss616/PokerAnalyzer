namespace PokerAnalyzer.Application.PreflopAnalysis;

public sealed record PreflopStrategyResultDto(
    string InfoSetKey,
    IReadOnlyDictionary<string, decimal> AverageStrategy,
    int IterationsCompleted,
    double RegretMagnitude,
    string StrategySource = "Unknown",
    long ElapsedMilliseconds = 0,
    string SolveMode = "None");
