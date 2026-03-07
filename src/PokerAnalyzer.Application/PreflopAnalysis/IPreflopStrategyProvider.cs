namespace PokerAnalyzer.Application.PreflopAnalysis;

public interface IPreflopStrategyProvider
{
    Task<IReadOnlyDictionary<string, decimal>?> GetMixedStrategyAsync(string solverKey, CancellationToken ct);
}
