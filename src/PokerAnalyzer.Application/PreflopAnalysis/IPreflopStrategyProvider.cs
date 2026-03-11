using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopAnalysis;

public sealed record PreflopStrategyRequestDto(
    string SolverKey,
    SolverHandState RootState,
    IReadOnlyList<LegalAction> LegalActions,
    bool UsePersistentTrainingState = false);

public interface IPreflopStrategyProvider
{
    Task<PreflopStrategyResultDto?> GetStrategyResultAsync(PreflopStrategyRequestDto request, CancellationToken ct);
}
