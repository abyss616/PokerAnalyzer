using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Engines;

public interface IMonteCarloReferenceEngine
{
    Recommendation EvaluateReference(HandState state, HeroContext hero);
}
