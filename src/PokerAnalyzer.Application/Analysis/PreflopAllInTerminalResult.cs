using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Analysis;

public sealed record PreflopAllInTerminalResult(
    Street Street,
    IReadOnlyList<PlayerId> Players,
    IReadOnlyDictionary<PlayerId, HoleCards?> HoleCardsKnown,
    decimal PotChipsAtAllIn,
    IReadOnlyDictionary<PlayerId, decimal> CommittedChipsAtAllIn,
    IReadOnlyDictionary<PlayerId, decimal> Equities,
    string Method,
    int? SamplesUsed,
    int? SeedUsed
);
