namespace PokerAnalyzer.Domain.Game;

/// <summary>
/// Deterministic baseline sizing for executable solver trajectories.
/// Returned sizes are absolute target street-contribution amounts.
/// </summary>
public sealed class DefaultBetSizeSetProvider : IBetSizeSetProvider
{
    public IReadOnlyList<ChipAmount> GetBetSizes(SolverHandState state)
    {
        var acting = state.Players.First(player => player.PlayerId == state.ActingPlayerId);
        var halfPotTarget = BuildTargetContribution(acting.CurrentStreetContribution, state.Pot, numerator: 1, denominator: 2);
        var potTarget = BuildTargetContribution(acting.CurrentStreetContribution, state.Pot, numerator: 1, denominator: 1);

        return [halfPotTarget, potTarget];
    }

    public IReadOnlyList<ChipAmount> GetRaiseSizes(SolverHandState state)
    {
        var acting = state.Players.First(player => player.PlayerId == state.ActingPlayerId);

        var minIncrement = Math.Max(1, state.LastRaiseSize.Value);
        var minRaiseTarget = new ChipAmount(state.CurrentBetSize.Value + minIncrement);
        var jamTarget = acting.CurrentStreetContribution + acting.Stack;

        if (jamTarget <= minRaiseTarget)
        {
            return [jamTarget];
        }

        return [minRaiseTarget, jamTarget];
    }

    private static ChipAmount BuildTargetContribution(
        ChipAmount currentStreetContribution,
        ChipAmount pot,
        long numerator,
        long denominator)
    {
        var scaled = (pot.Value * numerator) / denominator;
        var delta = Math.Max(1, scaled);
        return currentStreetContribution + new ChipAmount(delta);
    }
}
