namespace PokerAnalyzer.Domain.Game;

public readonly record struct SolverActionEntry(
    PlayerId PlayerId,
    ActionType ActionType,
    ChipAmount Amount
)
{
    public override string ToString() => $"{PlayerId.Value:N}:{(int)ActionType}:{Amount.Value}";
}
