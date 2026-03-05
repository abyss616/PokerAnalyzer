namespace PokerAnalyzer.Domain.Game;

public sealed record GameConfig(
    int MaxPlayers,
    ChipAmount SmallBlind,
    ChipAmount BigBlind,
    ChipAmount Ante,
    ChipAmount StartingStack
);
