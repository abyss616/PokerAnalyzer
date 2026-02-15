using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using EvalCard = PokerAnalyzer.Domain.Cards.Card;

namespace PokerAnalyzer.Infrastructure.Engines;

/// <summary>
/// Monte-Carlo based EV approximation engine.
/// Uses simple opponent range profiles (position + action-history heuristics)
/// and falls back to <see cref="DummyStrategyEngine"/> when required inputs are missing.
/// </summary>
public sealed class MonteCarloStrategyEngine : IStrategyEngine
{
    private static readonly IReadOnlyDictionary<Position, decimal> BaseRangeByPosition = new Dictionary<Position, decimal>
    {
        [Position.UTG] = 0.20m,
        [Position.UTG1] = 0.22m,
        [Position.UTG2] = 0.24m,
        [Position.LJ] = 0.26m,
        [Position.HJ] = 0.29m,
        [Position.CO] = 0.33m,
        [Position.BTN] = 0.40m,
        [Position.SB] = 0.30m,
        [Position.BB] = 0.35m,
        [Position.Unknown] = 0.28m,
    };

    private readonly DummyStrategyEngine _fallback = new();
    private readonly Random _random;
    private readonly int _iterations;

    public MonteCarloStrategyEngine() : this(new Random(7), iterations: 350)
    {
    }

    internal MonteCarloStrategyEngine(Random random, int iterations)
    {
        _random = random;
        _iterations = Math.Max(100, iterations);
    }

    public Recommendation Recommend(HandState state, HeroContext hero)
    {
        var legal = state.GetLegalActions(hero.HeroId);
        if (legal.Count == 0)
            return _fallback.Recommend(state, hero);

        if (hero.HeroHoleCards is null)
            return _fallback.Recommend(state, hero) with
            {
                Explanation = "Monte Carlo fallback: hero hole cards unavailable."
            };

        var opponents = state.ActivePlayers.Where(id => id != hero.HeroId).ToArray();
        if (opponents.Length == 0)
            return _fallback.Recommend(state, hero) with
            {
                Explanation = "Monte Carlo fallback: no active opponents."
            };

        var boardCards = GetBoardCards(state.Board);
        var deadCards = new HashSet<EvalCard>(boardCards) { hero.HeroHoleCards.Value.First, hero.HeroHoleCards.Value.Second };
        var deck = BuildDeck(deadCards);

        if (deck.Count < opponents.Length * 2)
            return _fallback.Recommend(state, hero) with
            {
                Explanation = "Monte Carlo fallback: insufficient remaining deck cards."
            };

        var ranked = new List<RecommendedAction>();

        foreach (var action in legal)
        {
            var candidateTo = ResolveToAmount(state, hero.HeroId, action);
            var ev = EstimateActionEv(state, hero, action, candidateTo, opponents, boardCards, deck);
            ranked.Add(new RecommendedAction(action, candidateTo, ev));
        }

        var sorted = ranked
            .OrderByDescending(r => r.EstimatedEv ?? decimal.MinValue)
            .ToList();

        return new Recommendation(
            sorted,
            "Monte Carlo engine: EV-ranked actions using position + action-history opponent ranges. Solver export lookup can be layered later."
        );
    }

    private decimal EstimateActionEv(
        HandState state,
        HeroContext hero,
        ActionType action,
        ChipAmount? toAmount,
        IReadOnlyList<PlayerId> opponents,
        IReadOnlyList<EvalCard> boardCards,
        IReadOnlyList<EvalCard> baseDeck)
    {
        var heroAlready = state.StreetContrib[hero.HeroId].Value;
        var callCost = state.GetToCall(hero.HeroId).Value;
        var risk = action switch
        {
            ActionType.Fold => 0,
            ActionType.Check => 0,
            ActionType.Call => callCost,
            ActionType.Bet or ActionType.Raise or ActionType.AllIn => Math.Max(0, (toAmount?.Value ?? heroAlready) - heroAlready),
            _ => 0,
        };

        if (action == ActionType.Fold)
            return 0m;

        var foldEquity = EstimateFoldEquity(action, hero, opponents);

        decimal equitySum = 0m;

        for (var i = 0; i < _iterations; i++)
        {
            if (!TryRunSingleSimulation(hero.HeroHoleCards!.Value, hero, opponents, boardCards, baseDeck, out var winShare))
                continue;

            equitySum += winShare;
        }

        var avgEquity = equitySum / _iterations;

        var potIfCalled = state.Pot.Value + risk;
        var calledEv = (avgEquity * potIfCalled) - risk;

        return (foldEquity * state.Pot.Value) + ((1m - foldEquity) * calledEv);
    }

    private bool TryRunSingleSimulation(
        HoleCards heroCards,
        HeroContext hero,
        IReadOnlyList<PlayerId> opponents,
        IReadOnlyList<EvalCard> boardCards,
        IReadOnlyList<EvalCard> baseDeck,
        out decimal winShare)
    {
        var deck = baseDeck.ToList();
        var villainHands = new List<HoleCards>(opponents.Count);

        foreach (var opponent in opponents)
        {
            if (!TryDrawOpponentHand(deck, opponent, hero, out var hand))
            {
                winShare = 0m;
                return false;
            }

            villainHands.Add(hand);
            deck.Remove(hand.First);
            deck.Remove(hand.Second);
        }

        var completedBoard = boardCards.ToList();
        while (completedBoard.Count < 5)
        {
            var idx = _random.Next(deck.Count);
            completedBoard.Add(deck[idx]);
            deck.RemoveAt(idx);
        }

        var heroScore = HandRankEvaluator.Evaluate(heroCards, completedBoard);
        var villainScores = villainHands.Select(h => HandRankEvaluator.Evaluate(h, completedBoard)).ToArray();
        var bestVillain = villainScores.Max();

        if (heroScore > bestVillain)
        {
            winShare = 1m;
            return true;
        }

        if (heroScore < bestVillain)
        {
            winShare = 0m;
            return true;
        }

        var tied = villainScores.Count(v => v == heroScore) + 1;
        winShare = 1m / tied;
        return true;
    }

    private bool TryDrawOpponentHand(
        IReadOnlyList<EvalCard> deck,
        PlayerId opponent,
        HeroContext hero,
        out HoleCards hand)
    {
        var percentile = EstimateRangePercentile(opponent, hero);
        var maxAttempts = 60;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var c1 = deck[_random.Next(deck.Count)];
            var c2 = deck[_random.Next(deck.Count)];
            if (c1 == c2)
                continue;

            var score = PreflopHandScore(c1, c2);
            if (score >= percentile)
            {
                hand = new HoleCards(c1, c2);
                return true;
            }
        }

        hand = default;
        return false;
    }

    private static ChipAmount? ResolveToAmount(HandState state, PlayerId heroId, ActionType action)
    {
        var already = state.StreetContrib[heroId].Value;
        var stack = state.Stacks[heroId].Value;
        var toCall = state.GetToCall(heroId).Value;

        return action switch
        {
            ActionType.Bet => new ChipAmount(already + Math.Min(stack, Math.Max(1, state.Pot.Value / 2))),
            ActionType.Raise => new ChipAmount(already + Math.Min(stack, Math.Max(toCall * 2, state.BetToCall.Value * 3 - already))),
            ActionType.AllIn => new ChipAmount(already + stack),
            _ => null,
        };
    }

    private static decimal EstimateFoldEquity(ActionType action, HeroContext hero, IReadOnlyList<PlayerId> opponents)
    {
        if (action is not (ActionType.Bet or ActionType.Raise or ActionType.AllIn))
            return 0m;

        var aggression = opponents
            .Select(id => OpponentAggressionFactor(id, hero.ActionHistory))
            .DefaultIfEmpty(0m)
            .Average();

        var baseline = action == ActionType.AllIn ? 0.36m : 0.28m;
        var adjustment = (-aggression * 0.12m);
        return Math.Clamp(baseline + adjustment, 0.08m, 0.60m);
    }

    private static decimal EstimateRangePercentile(PlayerId opponent, HeroContext hero)
    {
        var basePercentile = BaseRangeByPosition.TryGetValue(
            hero.PlayerPositions is not null && hero.PlayerPositions.TryGetValue(opponent, out var pos) ? pos : Position.Unknown,
            out var posRange)
            ? posRange
            : 0.28m;

        var aggression = OpponentAggressionFactor(opponent, hero.ActionHistory);
        var looseness = OpponentPassivityFactor(opponent, hero.ActionHistory);

        var adjusted = basePercentile - (aggression * 0.08m) + (looseness * 0.06m);
        return Math.Clamp(adjusted, 0.12m, 0.58m);
    }

    private static decimal OpponentAggressionFactor(PlayerId opponent, IReadOnlyList<BettingAction>? history)
    {
        if (history is null || history.Count == 0)
            return 0m;

        var raises = history.Count(a => a.ActorId == opponent && a.Type is ActionType.Raise or ActionType.Bet or ActionType.AllIn);
        return Math.Min(1m, raises / 3m);
    }

    private static decimal OpponentPassivityFactor(PlayerId opponent, IReadOnlyList<BettingAction>? history)
    {
        if (history is null || history.Count == 0)
            return 0m;

        var calls = history.Count(a => a.ActorId == opponent && a.Type == ActionType.Call);
        return Math.Min(1m, calls / 4m);
    }

    private static decimal PreflopHandScore(EvalCard a, EvalCard b)
    {
        var ar = (int)a.Rank;
        var br = (int)b.Rank;
        var high = Math.Max(ar, br);
        var low = Math.Min(ar, br);
        var suited = a.Suit == b.Suit ? 0.05m : 0m;
        var pair = a.Rank == b.Rank ? 0.20m : 0m;
        var connected = Math.Abs((int)a.Rank - (int)b.Rank) <= 1 ? 0.04m : 0m;

        var normalized = ((high + low) / 2m) / 14m;
        return Math.Clamp(normalized + suited + pair + connected, 0m, 1m);
    }

    private static IReadOnlyList<EvalCard> BuildDeck(ISet<EvalCard> deadCards)
    {
        var deck = new List<EvalCard>(52);

        foreach (var rank in Enum.GetValues<Rank>())
        foreach (var suit in Enum.GetValues<Suit>())
        {
            var c = new EvalCard(rank, suit);
            if (!deadCards.Contains(c))
                deck.Add(c);
        }

        return deck;
    }

    private static IReadOnlyList<EvalCard> GetBoardCards(global::Board board)
    {
        var cards = new List<EvalCard>(5);

        cards.AddRange(board.Flop.Select(ToEvalCard));
        if (board.Turn is not null)
            cards.Add(ToEvalCard(board.Turn!));
        if (board.River is not null)
            cards.Add(ToEvalCard(board.River!));

        return cards;
    }

    private static EvalCard ToEvalCard(global::Card card)
        => new(card.Rank, card.Suit);

    private static class HandRankEvaluator
    {
        public static long Evaluate(HoleCards hole, IReadOnlyList<EvalCard> board)
        {
            var cards = new[]
            {
                hole.First,
                hole.Second,
                board[0],
                board[1],
                board[2],
                board[3],
                board[4]
            };

            long best = 0;
            for (var a = 0; a < cards.Length - 4; a++)
            for (var b = a + 1; b < cards.Length - 3; b++)
            for (var c = b + 1; c < cards.Length - 2; c++)
            for (var d = c + 1; d < cards.Length - 1; d++)
            for (var e = d + 1; e < cards.Length; e++)
            {
                var score = EvaluateFive(cards[a], cards[b], cards[c], cards[d], cards[e]);
                if (score > best)
                    best = score;
            }

            return best;
        }

        private static long EvaluateFive(EvalCard c1, EvalCard c2, EvalCard c3, EvalCard c4, EvalCard c5)
        {
            var ranks = new[] { (int)c1.Rank, (int)c2.Rank, (int)c3.Rank, (int)c4.Rank, (int)c5.Rank };
            Array.Sort(ranks);
            Array.Reverse(ranks);

            var counts = ranks.GroupBy(r => r).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToList();
            var isFlush = c1.Suit == c2.Suit && c2.Suit == c3.Suit && c3.Suit == c4.Suit && c4.Suit == c5.Suit;
            var straightHigh = GetStraightHigh(ranks);

            if (isFlush && straightHigh > 0)
                return Pack(8, straightHigh);

            if (counts[0].Count() == 4)
                return Pack(7, counts[0].Key, counts[1].Key);

            if (counts[0].Count() == 3 && counts[1].Count() == 2)
                return Pack(6, counts[0].Key, counts[1].Key);

            if (isFlush)
                return Pack(5, ranks);

            if (straightHigh > 0)
                return Pack(4, straightHigh);

            if (counts[0].Count() == 3)
            {
                var kickers = counts.Skip(1).Select(g => g.Key).OrderByDescending(v => v).ToArray();
                return Pack(3, new[] { counts[0].Key, kickers[0], kickers[1] });
            }

            if (counts[0].Count() == 2 && counts[1].Count() == 2)
            {
                var highPair = Math.Max(counts[0].Key, counts[1].Key);
                var lowPair = Math.Min(counts[0].Key, counts[1].Key);
                var kicker = counts[2].Key;
                return Pack(2, highPair, lowPair, kicker);
            }

            if (counts[0].Count() == 2)
            {
                var pair = counts[0].Key;
                var kickers = counts.Skip(1).Select(g => g.Key).OrderByDescending(v => v).ToArray();
                return Pack(1, new[] { pair, kickers[0], kickers[1], kickers[2] });
            }

            return Pack(0, ranks);
        }

        private static int GetStraightHigh(int[] ranks)
        {
            var distinct = ranks.Distinct().OrderByDescending(r => r).ToList();
            if (distinct.SequenceEqual(new[] { 14, 5, 4, 3, 2 }))
                return 5;

            if (distinct.Count < 5)
                return 0;

            for (var i = 0; i <= distinct.Count - 5; i++)
            {
                if (distinct[i] - 1 == distinct[i + 1]
                    && distinct[i + 1] - 1 == distinct[i + 2]
                    && distinct[i + 2] - 1 == distinct[i + 3]
                    && distinct[i + 3] - 1 == distinct[i + 4])
                {
                    return distinct[i];
                }
            }

            return 0;
        }

        private static long Pack(int category, params int[] kickers)
        {
            long value = category;
            foreach (var k in kickers)
                value = (value << 4) | (uint)k;

            return value;
        }
    }
}
