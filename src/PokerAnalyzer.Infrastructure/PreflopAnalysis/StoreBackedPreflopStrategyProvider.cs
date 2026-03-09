using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;

namespace PokerAnalyzer.Infrastructure.PreflopAnalysis;

public sealed class StoreBackedPreflopStrategyProvider : IPreflopStrategyProvider
{
    private readonly IPreflopStrategyQueryService _queryService;

    public StoreBackedPreflopStrategyProvider(IPreflopStrategyQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public Task<PreflopStrategyResultDto?> GetStrategyResultAsync(PreflopStrategyRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.LegalActions.Count == 0)
            return Task.FromResult<PreflopStrategyResultDto?>(null);

        var result = _queryService.GetStrategyResult(request.SolverKey, request.LegalActions);
        return Task.FromResult<PreflopStrategyResultDto?>(result with { StrategySource = "StoreBacked" });
    }
}
