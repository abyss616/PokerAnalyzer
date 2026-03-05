using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class FlopContinuationValueCalculator : IFlopContinuationValueCalculator
{
    private const int DefaultSeed = 12345;
    private const int TurnRiverSamplesPerFlop = 300;

    public Task<FlopContinuationValueResult> ComputeAsync(
        IReadOnlyList<(PlayerId PlayerId, HoleCards Cards)> knownPlayers,
        HandState endOfPreflopState,
        int flopsToSample = 50_000,
        int? seed = null,
        CancellationToken ct = default)
    {
        if (knownPlayers is null || knownPlayers.Count < 2)
            throw new ArgumentException("At least 2 players are required.", nameof(knownPlayers));
        if (endOfPreflopState is null)
            throw new ArgumentNullException(nameof(endOfPreflopState));
        if (endOfPreflopState.Street != Street.Preflop)
            throw new ArgumentException("State must be at end of preflop.", nameof(endOfPreflopState));
        if (flopsToSample <= 0)
            throw new ArgumentOutOfRangeException(nameof(flopsToSample));

        ValidateNoDuplicates(knownPlayers);

        var activeKnown = knownPlayers.Where(p => endOfPreflopState.ActivePlayers.Contains(p.PlayerId)).ToList();
        if (activeKnown.Count < 2)
            throw new InvalidOperationException("At least 2 active known players are required.");

        var deck = BuildDeckExcluding(activeKnown.SelectMany(p => new[] { p.Cards.First, p.Cards.Second }));
        var cards = deck.ToArray();

        var usedSeed = seed ?? DefaultSeed;
        var rng = new Random(usedSeed);
        var evAccumulator = activeKnown.ToDictionary(p => p.PlayerId, _ => 0d);

        var basePot = (double)endOfPreflopState.Pot.Value;
        var activeOrder = activeKnown.Select(p => p.PlayerId).ToList();
        var knownById = activeKnown.ToDictionary(x => x.PlayerId, x => x.Cards);

        for (var sample = 0; sample < flopsToSample; sample++)
        {
            ct.ThrowIfCancellationRequested();

            var flop = DrawDistinct(cards, 3, rng);

            var aggressor = ResolveAggressor(endOfPreflopState, activeOrder);
            var deltas = activeOrder.ToDictionary(id => id, _ => 0d);
            var pot = basePot;
            var showdownPlayers = new List<PlayerId> { aggressor };

            var aggressorStack = endOfPreflopState.Stacks[aggressor].Value;
            var canBet = aggressorStack > 0 && pot > 0;
            var betSize = canBet ? Math.Min(aggressorStack, (long)Math.Max(1d, Math.Round(pot * 0.5d, MidpointRounding.AwayFromZero))) : 0L;

            if (betSize > 0)
            {
                deltas[aggressor] -= betSize;
                pot += betSize;

                foreach (var player in activeOrder.Where(p => p != aggressor))
                {
                    var playerStack = endOfPreflopState.Stacks[player].Value;
                    var callCost = Math.Min(playerStack, betSize);
                    if (callCost <= 0)
                        continue;

                    var remaining = new List<PlayerId> { aggressor, player };
                    remaining.AddRange(activeOrder.Where(p => p != aggressor && p != player));
                    var equity = EstimateEquity(player, remaining, knownById, flop, cards, rng, ct);
                    var potOdds = callCost / (pot + callCost);
                    var call = equity >= potOdds + 0.05d;

                    if (!call)
                        continue;

                    deltas[player] -= callCost;
                    pot += callCost;
                    showdownPlayers.Add(player);
                }

                if (showdownPlayers.Count == 1)
                {
                    deltas[aggressor] += pot;
                    EnsureEndOfPreflopBaselineInvariant(deltas, basePot);
                    Accumulate(evAccumulator, deltas);
                    continue;
                }
            }
            else
            {
                showdownPlayers = new List<PlayerId>(activeOrder);
            }

            AwardShowdown(deltas, pot, showdownPlayers, knownById, flop, cards, rng, ct);
            EnsureEndOfPreflopBaselineInvariant(deltas, basePot);
            Accumulate(evAccumulator, deltas);
        }

        var chipEv = evAccumulator.ToDictionary(kvp => kvp.Key, kvp => (decimal)(kvp.Value / flopsToSample));
        return Task.FromResult(new FlopContinuationValueResult(chipEv, "FlopRollout", flopsToSample, usedSeed));
    }

    private static void AwardShowdown(
        Dictionary<PlayerId, double> deltas,
        double pot,
        IReadOnlyList<PlayerId> contenders,
        IReadOnlyDictionary<PlayerId, HoleCards> knownById,
        Card[] flop,
        Card[] deck,
        Random rng,
        CancellationToken ct)
    {
        var wins = contenders.ToDictionary(p => p, _ => 0d);
        var board = new Card[5];
        var ranks = new long[contenders.Count];
        board[0] = flop[0]; board[1] = flop[1]; board[2] = flop[2];

        for (var i = 0; i < TurnRiverSamplesPerFlop; i++)
        {
            ct.ThrowIfCancellationRequested();
            var turnRiver = DrawDistinctExcluding(deck, 2, flop, rng);
            board[3] = turnRiver[0];
            board[4] = turnRiver[1];

            var best = long.MinValue;
            var winnerCount = 0;
            for (var p = 0; p < contenders.Count; p++)
            {
                var pid = contenders[p];
                ranks[p] = HandRankEvaluator.Evaluate7(knownById[pid], board);
                if (ranks[p] > best)
                {
                    best = ranks[p];
                    winnerCount = 1;
                }
                else if (ranks[p] == best)
                {
                    winnerCount++;
                }
            }

            var split = 1d / winnerCount;
            for (var p = 0; p < contenders.Count; p++)
            {
                if (ranks[p] == best)
                    wins[contenders[p]] += split;
            }
        }

        foreach (var contender in contenders)
        {
            var equity = wins[contender] / TurnRiverSamplesPerFlop;
            deltas[contender] += pot * equity;
        }
    }

    private static double EstimateEquity(
        PlayerId player,
        IReadOnlyList<PlayerId> opponentsAndHero,
        IReadOnlyDictionary<PlayerId, HoleCards> knownById,
        Card[] flop,
        Card[] deck,
        Random rng,
        CancellationToken ct)
    {
        var wins = 0d;
        var board = new Card[5];
        board[0] = flop[0]; board[1] = flop[1]; board[2] = flop[2];

        for (var i = 0; i < TurnRiverSamplesPerFlop; i++)
        {
            ct.ThrowIfCancellationRequested();
            var turnRiver = DrawDistinctExcluding(deck, 2, flop, rng);
            board[3] = turnRiver[0];
            board[4] = turnRiver[1];

            var best = long.MinValue;
            var winners = 0;
            long playerRank = long.MinValue;
            for (var p = 0; p < opponentsAndHero.Count; p++)
            {
                var pid = opponentsAndHero[p];
                var rank = HandRankEvaluator.Evaluate7(knownById[pid], board);
                if (pid == player)
                    playerRank = rank;

                if (rank > best)
                {
                    best = rank;
                    winners = 1;
                }
                else if (rank == best)
                {
                    winners++;
                }
            }

            if (playerRank == best)
                wins += 1d / winners;
        }

        return wins / TurnRiverSamplesPerFlop;
    }

    private static void Accumulate(Dictionary<PlayerId, double> total, Dictionary<PlayerId, double> sample)
    {
        foreach (var (player, value) in sample)
            total[player] += value;
    }

    private static void EnsureEndOfPreflopBaselineInvariant(Dictionary<PlayerId, double> deltas, double initialPot)
    {
        const double epsilon = 1e-9;
        var sum = deltas.Values.Sum();
        if (Math.Abs(sum - initialPot) <= epsilon)
            return;

        throw new InvalidOperationException(
            $"Flop EV deltas must sum to the end-of-preflop pot. Sum={sum:R}, Expected={initialPot:R}, Players={deltas.Count}.");
    }

    private static PlayerId ResolveAggressor(HandState state, IReadOnlyList<PlayerId> activeOrder)
    {
        if (state.LastAggressor.HasValue && activeOrder.Contains(state.LastAggressor.Value))
            return state.LastAggressor.Value;

        return activeOrder[0];
    }

    private static Card[] DrawDistinct(Card[] deck, int count, Random rng)
    {
        var selected = new Card[count];
        var used = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            int idx;
            do
            {
                idx = rng.Next(deck.Length);
            } while (!used.Add(idx));

            selected[i] = deck[idx];
        }

        return selected;
    }

    private static Card[] DrawDistinctExcluding(Card[] deck, int count, Card[] excluded, Random rng)
    {
        var selected = new Card[count];
        var excludedKeys = excluded.Select(CardKey).ToHashSet();
        var used = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            int idx;
            do
            {
                idx = rng.Next(deck.Length);
            } while (used.Contains(idx) || excludedKeys.Contains(CardKey(deck[idx])));

            used.Add(idx);
            selected[i] = deck[idx];
        }

        return selected;
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
