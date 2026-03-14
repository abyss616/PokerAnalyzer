using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopAnalysis;

public sealed record PreflopStrategyRequestDto(
    string SolverKey,
    SolverHandState RootState,
    IReadOnlyList<LegalAction> LegalActions,
    bool UsePersistentTrainingState = false,
    string? PopulationProfileName = null);

public interface IPreflopStrategyProvider
{
    Task<PreflopStrategyResultDto?> GetStrategyResultAsync(PreflopStrategyRequestDto request, CancellationToken ct);
}
