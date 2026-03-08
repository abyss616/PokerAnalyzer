using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopAnalysis;

public sealed class StoreBackedPreflopStrategyProvider : IPreflopStrategyProvider
{
    private readonly IPreflopStrategyQueryService _queryService;

    public StoreBackedPreflopStrategyProvider(IPreflopStrategyQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public Task<PreflopStrategyResultDto?> GetStrategyResultAsync(string solverKey, IReadOnlyList<string> legalActions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solverKey);
        ArgumentNullException.ThrowIfNull(legalActions);

        var mappedLegalActions = legalActions
            .Select(MapAction)
            .Where(a => a is not null)
            .Cast<LegalAction>()
            .ToArray();

        if (mappedLegalActions.Length == 0)
            return Task.FromResult<PreflopStrategyResultDto?>(null);

        var result = _queryService.GetStrategyResult(solverKey, mappedLegalActions);
        return Task.FromResult<PreflopStrategyResultDto?>(result);
    }

    private static LegalAction? MapAction(string action)
        => action.Trim().ToUpperInvariant() switch
        {
            "FOLD" => new LegalAction(ActionType.Fold),
            "CHECK" => new LegalAction(ActionType.Check),
            "CALL" => new LegalAction(ActionType.Call),
            "RAISE" => new LegalAction(ActionType.Raise),
            _ => null
        };
}
