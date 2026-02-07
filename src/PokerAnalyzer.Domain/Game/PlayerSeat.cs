namespace PokerAnalyzer.Domain.Game;

public sealed record PlayerSeat(
    PlayerId Id,
    string Name,
    int SeatNumber,
    Position Position,
    ChipAmount StartingStack
);
