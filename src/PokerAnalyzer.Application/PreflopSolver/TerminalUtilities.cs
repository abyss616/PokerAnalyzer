namespace PokerAnalyzer.Application.PreflopSolver;

public static class TerminalUtilities
{
    public static decimal[] ComputeEveryoneFoldsUtility(
        IReadOnlyList<decimal> contributed,
        IReadOnlyList<bool> folded,
        decimal rake,
        decimal eps = 0.000001m)
    {
        if (contributed is null) throw new ArgumentNullException(nameof(contributed));
        if (folded is null) throw new ArgumentNullException(nameof(folded));
        if (contributed.Count != folded.Count)
            throw new ArgumentException("contributed and folded must have the same length.");
        if (contributed.Count == 0)
            throw new ArgumentException("At least one player is required.", nameof(contributed));
        if (contributed.Any(c => c < 0))
            throw new ArgumentOutOfRangeException(nameof(contributed), "Contributions must be >= 0.");
        if (rake < 0)
            throw new ArgumentOutOfRangeException(nameof(rake), "Rake must be >= 0.");
        if (eps < 0)
            throw new ArgumentOutOfRangeException(nameof(eps), "eps must be >= 0.");

        var winner = -1;
        for (var i = 0; i < folded.Count; i++)
        {
            if (folded[i])
            {
                continue;
            }

            if (winner != -1)
                throw new InvalidOperationException("Everyone-folds terminal expects exactly one active player.");

            winner = i;
        }

        if (winner == -1)
            throw new InvalidOperationException("Everyone-folds terminal expects exactly one active player.");

        var pot = contributed.Sum();
        var distributedPot = Math.Max(0m, pot - rake);

        var utility = new decimal[contributed.Count];
        for (var i = 0; i < contributed.Count; i++)
        {
            utility[i] = i == winner
                ? distributedPot - contributed[i]
                : -contributed[i];
        }

        var utilitySum = utility.Sum();
        if (Math.Abs(utilitySum + rake) > eps)
            throw new InvalidOperationException(
                $"Utility invariant failed: sum(u)={utilitySum} but expected {-rake} (eps={eps}).");
        return utility;
    }
}