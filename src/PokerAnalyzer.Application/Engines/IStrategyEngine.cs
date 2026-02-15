using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Engines;

public interface IStrategyEngine
{
    Recommendation Recommend(HandState state, HeroContext hero);
}

public sealed record HeroContext(
    PlayerId HeroId,
    ChipAmount SmallBlind,
    ChipAmount BigBlind)
{
    public HoleCards? HeroHoleCards { get; init; }
    public IReadOnlyDictionary<PlayerId, Position>? PlayerPositions { get; init; }
    public IReadOnlyList<BettingAction>? ActionHistory { get; init; }
}
