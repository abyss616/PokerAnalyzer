using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Engines;

public interface IStrategyEngine
{
    Recommendation Recommend(HandState state, HeroContext hero);
}

public sealed record HeroContext(
    PlayerId HeroId,
    ChipAmount SmallBlind,
    ChipAmount BigBlind
);
