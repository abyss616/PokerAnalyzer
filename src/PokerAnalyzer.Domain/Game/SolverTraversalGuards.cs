namespace PokerAnalyzer.Domain.Game;

public static class SolverTraversalGuards
{
    public static bool IsCompletedPreflopState(SolverHandState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Street != Street.Preflop)
            return false;

        var activePlayers = state.Players.Count(p => p.IsActive);
        if (activePlayers <= 1)
            return true;

        if (state.CurrentBetSize.Value != 0 || state.ToCall.Value != 0)
            return false;

        var actionablePlayers = state.Players
            .Where(p => p.IsActive && !p.IsAllIn && p.Stack.Value > 0)
            .ToArray();

        if (actionablePlayers.Length == 0)
            return true;

        if (actionablePlayers.Any(p => p.CurrentStreetContribution.Value != 0))
            return false;

        return state.ActionHistory.Any(a => a.ActionType is
            ActionType.Fold or
            ActionType.Check or
            ActionType.Call or
            ActionType.Bet or
            ActionType.Raise or
            ActionType.AllIn);
    }
}
