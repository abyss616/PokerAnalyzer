using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Domain.HandHistory;

public sealed record Hand(
    Guid HandId,
    ChipAmount SmallBlind,
    ChipAmount BigBlind,
    IReadOnlyList<PlayerSeat> Seats,
    PlayerId HeroId,
    HoleCards? HeroHoleCards,
    IReadOnlyDictionary<PlayerId, HoleCards>? RevealedHoleCards,
    Board Board,
    IReadOnlyList<BettingAction> Actions
);
