namespace PokerAnalyzer.Application.PreflopSolver;

public static class TerminalUtilities
{
    /// <summary>
    /// Computes per-player terminal utility at showdown using the same baseline convention as
    /// <see cref="ComputeEveryoneFoldsUtility"/>: utility is net chips relative to hand start
    /// (payout minus each player's contribution). Under this convention, sum(utility) = -rake.
    /// </summary>
    public static decimal[] ComputeShowdownUtility(
        IReadOnlyList<decimal> contributed,
        IReadOnlyList<bool> folded,
        IReadOnlyCollection<int> winnerIndices,
        decimal rake,
        decimal eps = 0.000001m)
    {
        if (contributed is null) throw new ArgumentNullException(nameof(contributed));
        if (folded is null) throw new ArgumentNullException(nameof(folded));
        if (winnerIndices is null) throw new ArgumentNullException(nameof(winnerIndices));
        if (contributed.Count != folded.Count)
            throw new ArgumentException("contributed and folded must have the same length.");
        if (contributed.Count == 0)
            throw new ArgumentException("At least one player is required.", nameof(contributed));
        if (winnerIndices.Count == 0)
            throw new ArgumentException("At least one showdown winner is required.", nameof(winnerIndices));
        if (contributed.Any(c => c < 0))
            throw new ArgumentOutOfRangeException(nameof(contributed), "Contributions must be >= 0.");
        if (rake < 0)
            throw new ArgumentOutOfRangeException(nameof(rake), "Rake must be >= 0.");
        if (eps < 0)
            throw new ArgumentOutOfRangeException(nameof(eps), "eps must be >= 0.");

        var eligible = new List<int>();
        for (var i = 0; i < folded.Count; i++)
        {
            if (!folded[i])
            {
                eligible.Add(i);
            }
        }

        if (eligible.Count == 0)
            throw new InvalidOperationException("Showdown terminal requires at least one non-folded player.");

        var maxEligibleContribution = contributed[eligible[0]];
        foreach (var playerIndex in eligible)
        {
            maxEligibleContribution = Math.Max(maxEligibleContribution, contributed[playerIndex]);
        }

        foreach (var playerIndex in eligible)
        {
            if (maxEligibleContribution - contributed[playerIndex] > eps)
            {
                throw new InvalidOperationException(
                    "Side pot or multi-pot showdown detected: non-folded players have unequal contributions.");
            }
        }

        var seenWinners = new HashSet<int>();
        foreach (var winnerIndex in winnerIndices)
        {
            if (winnerIndex < 0 || winnerIndex >= contributed.Count)
                throw new ArgumentOutOfRangeException(nameof(winnerIndices), "Winner index is out of range.");
            if (folded[winnerIndex])
                throw new InvalidOperationException("Folded players are not eligible to win at showdown.");
            seenWinners.Add(winnerIndex);
        }

        var pot = contributed.Sum();
        var distributedPot = Math.Max(0m, pot - rake);
        var payoutPerWinner = distributedPot / seenWinners.Count;

        var utility = new decimal[contributed.Count];
        for (var i = 0; i < contributed.Count; i++)
        {
            var payout = seenWinners.Contains(i) ? payoutPerWinner : 0m;
            utility[i] = payout - contributed[i];
        }

        var utilitySum = utility.Sum();
        if (Math.Abs(utilitySum + rake) > eps)
            throw new InvalidOperationException(
                $"Utility invariant failed: sum(u)={utilitySum} but expected {-rake} (eps={eps}).");

        return utility;
    }

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
