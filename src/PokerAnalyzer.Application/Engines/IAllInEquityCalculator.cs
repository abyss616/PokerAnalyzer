using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Engines;

public interface IAllInEquityCalculator
{
    Task<AllInEquityResult> ComputePreflopAsync(
        IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> players,
        int? samples,
        int? seed,
        CancellationToken ct);
}

public sealed record AllInEquityResult(
    IReadOnlyDictionary<PlayerId, decimal> Equities,
    string Method,
    int? SamplesUsed,
    int? SeedUsed
);
