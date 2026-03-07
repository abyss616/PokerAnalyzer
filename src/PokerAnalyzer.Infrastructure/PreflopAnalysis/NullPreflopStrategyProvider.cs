using PokerAnalyzer.Application.PreflopAnalysis;

namespace PokerAnalyzer.Infrastructure.PreflopAnalysis;

public sealed class NullPreflopStrategyProvider : IPreflopStrategyProvider
{
    public Task<IReadOnlyDictionary<string, decimal>?> GetMixedStrategyAsync(string solverKey, CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, decimal>?>(null);
}
