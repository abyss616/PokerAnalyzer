namespace PokerAnalyzer.Domain.Game;

public readonly record struct ChanceSampleResult(
    SolverHandState NextState,
    double SamplingProbability);

public interface IChanceSampler
{
    bool IsChanceNode(SolverHandState state);

    SolverHandState Sample(SolverHandState state, Random rng);

    ChanceSampleResult SampleWithProbability(SolverHandState state, Random rng);
}
