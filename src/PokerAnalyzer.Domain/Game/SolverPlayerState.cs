namespace PokerAnalyzer.Domain.Game;

public sealed record SolverPlayerState(
    PlayerId PlayerId,
    int SeatIndex,
    Position Position,
    ChipAmount Stack,
    ChipAmount CurrentStreetContribution,
    ChipAmount TotalContribution,
    bool IsFolded,
    bool IsAllIn
)
{
    public bool IsActive => !IsFolded;
}
