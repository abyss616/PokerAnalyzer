namespace PokerAnalyzer.Domain.Game;

public static class SolverTraversalGuards
{
    public static bool IsTerminalLikeState(SolverHandState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var activePlayers = 0;
        var actionablePlayers = 0;
        var players = state.Players;

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!player.IsActive)
                continue;

            activePlayers++;
            if (!player.IsAllIn && player.Stack.Value > 0)
                actionablePlayers++;
        }

        return activePlayers <= 1
            || actionablePlayers == 0
            || IsCompletedPreflopState(state);
    }

    public static bool IsCompletedPreflopState(SolverHandState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Street != Street.Preflop)
            return false;

        var activePlayers = 0;
        var actionablePlayers = 0;
        var players = state.Players;

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!player.IsActive)
                continue;

            activePlayers++;
            if (!player.IsAllIn && player.Stack.Value > 0)
            {
                actionablePlayers++;
                if (player.CurrentStreetContribution.Value != 0)
                    return false;
            }
        }

        if (activePlayers <= 1)
            return true;

        if (state.CurrentBetSize.Value != 0 || state.ToCall.Value != 0)
            return false;

        if (actionablePlayers == 0)
            return true;

        return state.ActionHistory.Any(a => a.ActionType is
            ActionType.Fold or
            ActionType.Check or
            ActionType.Call or
            ActionType.Bet or
            ActionType.Raise or
            ActionType.AllIn);
    }
}
