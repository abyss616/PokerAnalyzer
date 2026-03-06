namespace PokerAnalyzer.Domain.Game;

public interface IChanceSampler
{
    bool IsChanceNode(SolverHandState state);

    SolverHandState Sample(SolverHandState state, Random rng);
}

