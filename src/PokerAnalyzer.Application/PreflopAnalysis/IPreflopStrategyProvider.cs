namespace PokerAnalyzer.Application.PreflopAnalysis;

public interface IPreflopStrategyProvider
{
    Task<PreflopStrategyResultDto?> GetStrategyResultAsync(string solverKey, IReadOnlyList<string> legalActions, CancellationToken ct);
}
