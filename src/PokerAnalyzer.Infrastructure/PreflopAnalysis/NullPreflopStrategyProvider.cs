using PokerAnalyzer.Application.PreflopAnalysis;

namespace PokerAnalyzer.Infrastructure.PreflopAnalysis;

public sealed class NullPreflopStrategyProvider : IPreflopStrategyProvider
{
    public Task<PreflopStrategyResultDto?> GetStrategyResultAsync(PreflopStrategyRequestDto request, CancellationToken ct)
        => Task.FromResult<PreflopStrategyResultDto?>(null);
}
