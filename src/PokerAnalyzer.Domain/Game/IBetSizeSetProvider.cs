namespace PokerAnalyzer.Domain.Game;

public interface IBetSizeSetProvider
{
    IReadOnlyList<ChipAmount> GetBetSizes(SolverHandState state);
    IReadOnlyList<ChipAmount> GetRaiseSizes(SolverHandState state);
}
