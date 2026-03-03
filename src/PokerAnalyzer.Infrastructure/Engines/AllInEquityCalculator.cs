using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class AllInEquityCalculator : IAllInEquityCalculator
{
    public Task<AllInEquityResult> ComputePreflopAsync(
        IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> players,
        int? samples,
        int? seed,
        CancellationToken ct)
    {
        if (players is null || players.Count < 2)
            throw new ArgumentException("At least 2 players are required.", nameof(players));

        ValidateNoDuplicates(players);

        var deck = BuildDeckExcluding(players.SelectMany(p => new[] { p.Cards.First, p.Cards.Second }));
        var useExact = players.Count == 2 && samples is null;

        return Task.FromResult(useExact
            ? ComputeExact(players, deck, ct)
            : ComputeMonteCarlo(players, deck, samples ?? 100_000, seed ?? 12345, ct));
    }

    private static AllInEquityResult ComputeExact(IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> players, List<Card> deck, CancellationToken ct)
    {
        var wins = new double[players.Count];
        var board = new Card[5];
        var p1 = players[0];
        var p2 = players[1];
        long total = 0;

        for (var a = 0; a < deck.Count - 4; a++)
        for (var b = a + 1; b < deck.Count - 3; b++)
        for (var c = b + 1; c < deck.Count - 2; c++)
        for (var d = c + 1; d < deck.Count - 1; d++)
        for (var e = d + 1; e < deck.Count; e++)
        {
            ct.ThrowIfCancellationRequested();
            board[0] = deck[a]; board[1] = deck[b]; board[2] = deck[c]; board[3] = deck[d]; board[4] = deck[e];

            var r1 = HandRankEvaluator.Evaluate7(p1.Cards, board);
            var r2 = HandRankEvaluator.Evaluate7(p2.Cards, board);

            if (r1 > r2) wins[0] += 1d;
            else if (r2 > r1) wins[1] += 1d;
            else { wins[0] += 0.5d; wins[1] += 0.5d; }
            total++;
        }

        return new AllInEquityResult(
            new Dictionary<PlayerId, decimal>
            {
                [p1.PlayerId] = (decimal)(wins[0] / total),
                [p2.PlayerId] = (decimal)(wins[1] / total)
            },
            "Exact",
            null,
            null);
    }

    private static AllInEquityResult ComputeMonteCarlo(IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> players, List<Card> deck, int samples, int seed, CancellationToken ct)
    {
        var wins = new double[players.Count];
        var ranks = new long[players.Count];
        var board = new Card[5];
        var rng = new Random(seed);
        var cards = deck.ToArray();

        for (var s = 0; s < samples; s++)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < 5; i++)
            {
                var j = rng.Next(i, cards.Length);
                (cards[i], cards[j]) = (cards[j], cards[i]);
                board[i] = cards[i];
            }

            var best = long.MinValue;
            for (var p = 0; p < players.Count; p++)
            {
                ranks[p] = HandRankEvaluator.Evaluate7(players[p].Cards, board);
                if (ranks[p] > best) best = ranks[p];
            }

            var winners = 0;
            for (var p = 0; p < players.Count; p++)
                if (ranks[p] == best) winners++;

            var split = 1d / winners;
            for (var p = 0; p < players.Count; p++)
                if (ranks[p] == best) wins[p] += split;
        }

        var equities = new Dictionary<PlayerId, decimal>(players.Count);
        for (var i = 0; i < players.Count; i++)
            equities[players[i].PlayerId] = (decimal)(wins[i] / samples);

        return new AllInEquityResult(equities, "MonteCarlo", samples, seed);
    }

    private static List<Card> BuildDeckExcluding(IEnumerable<Card> excluded)
    {
        var ex = excluded.Select(CardKey).ToHashSet();
        var deck = new List<Card>(52 - ex.Count);
        foreach (Rank rank in Enum.GetValues<Rank>())
        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            var c = new Card(rank, suit);
            if (!ex.Contains(CardKey(c))) deck.Add(c);
        }

        return deck;
    }

    private static void ValidateNoDuplicates(IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> players)
    {
        var set = new HashSet<int>();
        foreach (var p in players)
        {
            var c1 = CardKey(p.Cards.First);
            var c2 = CardKey(p.Cards.Second);
            if (!set.Add(c1) || !set.Add(c2))
                throw new InvalidOperationException("duplicate card detected across players");
        }
    }

    private static int CardKey(Card c) => ((int)c.Rank << 3) | (int)c.Suit;
}

internal static class HandRankEvaluator
{
    public static long Evaluate7(HoleCards hole, Card[] board)
    {
        Span<Card> cards = stackalloc Card[7];
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
}
