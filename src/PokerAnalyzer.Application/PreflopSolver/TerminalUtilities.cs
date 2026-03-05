using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Application.PreflopSolver;

public static class TerminalUtilities
{
    /// <summary>
    /// Computes realized terminal utility for an all-in showdown on a fully known 5-card runout.
    /// Baseline matches <see cref="ComputeEveryoneFoldsUtility"/>: utility is net chips relative
    /// to hand start (payout minus each player's contribution), so sum(utility) = -rake.
    /// </summary>
    public static decimal[] ComputeAllInRunoutUtility(
        IReadOnlyList<decimal> contributed,
        IReadOnlyList<bool> folded,
        IReadOnlyList<HoleCards?> holeCards,
        IReadOnlyList<Card> board,
        decimal rake,
        bool hasMultiplePotsOrSidePots = false,
        decimal eps = 0.000001m)
    {
        if (contributed is null) throw new ArgumentNullException(nameof(contributed));
        if (folded is null) throw new ArgumentNullException(nameof(folded));
        if (holeCards is null) throw new ArgumentNullException(nameof(holeCards));
        if (board is null) throw new ArgumentNullException(nameof(board));
        if (contributed.Count != folded.Count || contributed.Count != holeCards.Count)
            throw new ArgumentException("contributed, folded, and holeCards must have the same length.");
        if (board.Count != 5)
            throw new ArgumentException("All-in runout terminal requires a fully known 5-card board.", nameof(board));
        if (hasMultiplePotsOrSidePots)
            throw new InvalidOperationException("Side pot or multi-pot showdown detected: all-in runout utility supports a single pot only.");

        var seenCards = new HashSet<int>();
        for (var i = 0; i < board.Count; i++)
        {
            if (!seenCards.Add(CardKey(board[i])))
                throw new InvalidOperationException("Duplicate card detected in board/hole cards for all-in runout utility.");
        }

        var winners = new List<int>();
        var best = long.MinValue;

        for (var i = 0; i < folded.Count; i++)
        {
            if (folded[i])
            {
                continue;
            }

            if (holeCards[i] is null)
                throw new InvalidOperationException("Missing hole cards for a non-folded player in all-in runout utility.");

            var hole = holeCards[i]!.Value;
            if (!seenCards.Add(CardKey(hole.First)) || !seenCards.Add(CardKey(hole.Second)))
                throw new InvalidOperationException("Duplicate card detected in board/hole cards for all-in runout utility.");

            var rank = Evaluate7(hole, board);
            if (rank > best)
            {
                best = rank;
                winners.Clear();
                winners.Add(i);
            }
            else if (rank == best)
            {
                winners.Add(i);
            }
        }

        if (winners.Count == 0)
            throw new InvalidOperationException("Showdown terminal requires at least one non-folded player.");

        return ComputeShowdownUtility(contributed, folded, winners, rake, eps);
    }

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

    private static long Evaluate7(HoleCards hole, IReadOnlyList<Card> board)
    {
        var cards = new Card[7];
        cards[0] = hole.First;
        cards[1] = hole.Second;
        for (var i = 0; i < 5; i++) cards[i + 2] = board[i];

        var best = long.MinValue;
        for (var a = 0; a < 3; a++)
        for (var b = a + 1; b < 4; b++)
        for (var c = b + 1; c < 5; c++)
        for (var d = c + 1; d < 6; d++)
        for (var e = d + 1; e < 7; e++)
        {
            var rank = Evaluate5(cards[a], cards[b], cards[c], cards[d], cards[e]);
            if (rank > best) best = rank;
        }

        return best;
    }

    private static long Evaluate5(Card c1, Card c2, Card c3, Card c4, Card c5)
    {
        Span<int> counts = stackalloc int[15];
        Span<int> suitCounts = stackalloc int[5];
        Span<int> ranks = stackalloc int[5] { (int)c1.Rank, (int)c2.Rank, (int)c3.Rank, (int)c4.Rank, (int)c5.Rank };
        for (var i = 0; i < 5; i++) counts[ranks[i]]++;
        suitCounts[(int)c1.Suit]++; suitCounts[(int)c2.Suit]++; suitCounts[(int)c3.Suit]++; suitCounts[(int)c4.Suit]++; suitCounts[(int)c5.Suit]++;
        var flush = suitCounts[1] == 5 || suitCounts[2] == 5 || suitCounts[3] == 5 || suitCounts[4] == 5;

        var straightHigh = 0;
        for (var h = 14; h >= 5; h--)
            if (counts[h] > 0 && counts[h - 1] > 0 && counts[h - 2] > 0 && counts[h - 3] > 0 && counts[h - 4] > 0) { straightHigh = h; break; }
        if (straightHigh == 0 && counts[14] > 0 && counts[2] > 0 && counts[3] > 0 && counts[4] > 0 && counts[5] > 0) straightHigh = 5;

        if (flush && straightHigh > 0) return Key(8, straightHigh, 0, 0, 0, 0);

        var fours = 0; var three = 0;
        Span<int> pairs = stackalloc int[2]; var pairCount = 0;
        for (var r = 14; r >= 2; r--)
        {
            if (counts[r] == 4) fours = r;
            else if (counts[r] == 3) three = r;
            else if (counts[r] == 2 && pairCount < 2) pairs[pairCount++] = r;
        }

        if (fours > 0)
        {
            var k = HighestExcluding(counts, fours);
            return Key(7, fours, k, 0, 0, 0);
        }

        if (three > 0 && pairCount > 0)
            return Key(6, three, pairs[0], 0, 0, 0);

        if (flush)
        {
            var sorted = SortRanksDesc(ranks);
            return Key(5, sorted[0], sorted[1], sorted[2], sorted[3], sorted[4]);
        }

        if (straightHigh > 0) return Key(4, straightHigh, 0, 0, 0, 0);

        if (three > 0)
        {
            var k1 = HighestExcluding(counts, three);
            var k2 = HighestExcluding(counts, three, k1);
            return Key(3, three, k1, k2, 0, 0);
        }

        if (pairCount >= 2)
        {
            var hp = Math.Max(pairs[0], pairs[1]);
            var lp = Math.Min(pairs[0], pairs[1]);
            var k = HighestExcluding(counts, hp, lp);
            return Key(2, hp, lp, k, 0, 0);
        }

        if (pairCount == 1)
        {
            var p = pairs[0];
            var k1 = HighestExcluding(counts, p);
            var k2 = HighestExcluding(counts, p, k1);
            var k3 = HighestExcluding(counts, p, k1, k2);
            return Key(1, p, k1, k2, k3, 0);
        }

        {
            var sorted = SortRanksDesc(ranks);
            return Key(0, sorted[0], sorted[1], sorted[2], sorted[3], sorted[4]);
        }
    }

    private static Span<int> SortRanksDesc(Span<int> ranks)
    {
        for (var i = 1; i < ranks.Length; i++)
        {
            var v = ranks[i];
            var j = i - 1;
            while (j >= 0 && ranks[j] < v)
            {
                ranks[j + 1] = ranks[j];
                j--;
            }
            ranks[j + 1] = v;
        }

        return ranks;
    }

    private static int HighestExcluding(Span<int> counts, params int[] excludes)
    {
        for (var r = 14; r >= 2; r--)
        {
            if (counts[r] == 0) continue;
            var ex = false;
            for (var i = 0; i < excludes.Length; i++) if (r == excludes[i]) { ex = true; break; }
            if (!ex) return r;
        }

        return 0;
    }

    private static long Key(int cat, int a, int b, int c, int d, int e)
        => (((((long)cat * 15 + a) * 15 + b) * 15 + c) * 15 + d) * 15 + e;

    private static int CardKey(Card c) => ((int)c.Rank << 3) | (int)c.Suit;
}
