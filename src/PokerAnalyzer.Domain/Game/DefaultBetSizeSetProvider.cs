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

        var minRaiseTarget = state.CurrentBetSize + state.LastRaiseSize;
        var jamTarget = acting.CurrentStreetContribution + acting.Stack;

        var raiseTargets = new List<ChipAmount>(2) { minRaiseTarget };

        if (jamTarget >= minRaiseTarget && jamTarget != minRaiseTarget)
        {
            raiseTargets.Add(jamTarget);
        }

        return raiseTargets;
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
