using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Analysis;

public sealed record PreflopToFlopTerminalResult(
    Street Street,
    IReadOnlyList<PlayerId> Players,
    IReadOnlyDictionary<PlayerId, HoleCards?> HoleCardsKnown,
    decimal PotChipsAtEndPreflop,
    IReadOnlyDictionary<PlayerId, decimal> StacksAtEndPreflop,
    IReadOnlyDictionary<PlayerId, decimal> ChipEv,
    string Method,
    int FlopsSampled,
    int SeedUsed
);
