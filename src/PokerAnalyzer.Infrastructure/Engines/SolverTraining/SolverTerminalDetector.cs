using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines.SolverTraining;

public sealed class SolverTerminalDetector
{
    public bool IsTerminal(SolverHandState state)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        if (CountActivePlayers(state) <= 1)
            return true;

        if (HasNoActionablePlayers(state) && IsBoardComplete(state))
            return true;

        return IsTerminalByDomainEvaluatorHook(state);
    }

    private static int CountActivePlayers(SolverHandState state)
        => state.Players.Count(player => !player.IsFolded);

    private static bool HasNoActionablePlayers(SolverHandState state)
        => state.Players.All(player => player.IsFolded || player.IsAllIn || player.Stack.Value == 0);

    private static bool IsBoardComplete(SolverHandState state)
        => state.BoardCards.Count == 5 && (state.Street == Street.River || state.Street == Street.Showdown);

    private static bool IsTerminalByDomainEvaluatorHook(SolverHandState state)
    {
        _ = state;
        return false;
    }
}
