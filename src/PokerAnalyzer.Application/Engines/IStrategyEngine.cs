using System.Threading;
using System.Threading.Tasks;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Engines;

public interface IStrategyEngine
{
    Task<Recommendation> RecommendAsync(HandState state, HeroContext hero, CancellationToken ct = default);
}

public sealed record HeroContext(
    PlayerId HeroId,
    ChipAmount SmallBlind,
    ChipAmount BigBlind)
{
    public HoleCards? HeroHoleCards { get; init; }
    public IReadOnlyDictionary<PlayerId, Position>? PlayerPositions { get; init; }
    public IReadOnlyList<BettingAction>? ActionHistory { get; init; }
    public bool UseExactStateForSolverLookup { get; init; }
}
