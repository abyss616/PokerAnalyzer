using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Engines;

public interface IFlopContinuationValueCalculator
{
    Task<FlopContinuationValueResult> ComputeAsync(
        IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> knownPlayers,
        HandState endOfPreflopState,
        int flopsToSample = 50_000,
        int? seed = null,
        CancellationToken ct = default);
}

public sealed record FlopContinuationValueResult(
    IReadOnlyDictionary<PlayerId, decimal> ChipEv,
    string Method,
    int FlopsSampled,
    int SeedUsed
);
