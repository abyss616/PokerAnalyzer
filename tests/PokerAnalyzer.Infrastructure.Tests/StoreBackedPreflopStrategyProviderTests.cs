using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopAnalysis;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class StoreBackedPreflopStrategyProviderTests
{
    [Fact]
    public async Task GetStrategyResultAsync_MapsBbSizedActionKeys_ToSolverChipScale()
    {
        var query = new CapturingQueryService();
        var sut = new StoreBackedPreflopStrategyProvider(query);

        _ = await sut.GetStrategyResultAsync("v2/key", ["Fold", "Call:1", "Raise:2.5"], CancellationToken.None);

        Assert.NotNull(query.CapturedLegalActions);
        Assert.Contains(query.CapturedLegalActions!, a => a.ActionType == ActionType.Call && a.Amount!.Value == 100);
        Assert.Contains(query.CapturedLegalActions!, a => a.ActionType == ActionType.Raise && a.Amount!.Value == 250);
    }

    private sealed class CapturingQueryService : IPreflopStrategyQueryService
    {
        public IReadOnlyList<LegalAction>? CapturedLegalActions { get; private set; }

        public PreflopStrategyResultDto GetStrategyResult(string infoSetKey, IReadOnlyList<LegalAction> legalActions)
        {
            CapturedLegalActions = legalActions.ToArray();
            var strategy = legalActions.ToDictionary(a => a.ActionType.ToString(), _ => 1m / legalActions.Count);
            return new PreflopStrategyResultDto(infoSetKey, strategy, 0, 0d);
        }
    }
}
