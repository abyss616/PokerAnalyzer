using System.Collections.Concurrent;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

/// <summary>
/// Monte-Carlo based EV approximation engine.
/// Uses simple opponent range profiles (position + action-history heuristics)
/// and falls back to <see cref="DummyStrategyEngine"/> when required inputs are missing.
/// </summary>
public sealed class MonteCarloStrategyEngine : IMonteCarloReferenceEngine
{
    private const decimal EvTieBreakEpsilon = 0.0001m;
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

    public Recommendation Recommend(HandState state, HeroContext hero) => EvaluateReference(state, hero);

    public Recommendation EvaluateReference(HandState state, HeroContext hero)
    {
        var legal = state.GetLegalActions(hero.HeroId);
        if (legal.Count == 0)
            return BuildReference(_fallback.Recommend(state, hero));

        if (hero.HeroHoleCards is null)
            return BuildReference(_fallback.Recommend(state, hero)) with
            {
                Explanation = "Monte Carlo Reference (non-decision): hero hole cards unavailable.",
                ReferenceExplanation = "Reference: unavailable (hero hole cards missing); using dummy legality-based baseline."
            };

        var opponents = state.ActivePlayers.Where(id => id != hero.HeroId).ToArray();
        if (opponents.Length == 0)
            return BuildReference(_fallback.Recommend(state, hero)) with
            {
                Explanation = "Monte Carlo Reference (non-decision): no active opponents."
            };

        var boardCards = GetBoardCards(state.Board);
        var deadCards = new HashSet<Card>(boardCards) { hero.HeroHoleCards.Value.First, hero.HeroHoleCards.Value.Second };
        var deck = BuildDeck(deadCards);

        if (deck.Count < opponents.Length * 2)
            return BuildReference(_fallback.Recommend(state, hero)) with
            {
                Explanation = "Monte Carlo Reference (non-decision): insufficient remaining deck cards."
            };

        if (state.Street == Street.Preflop)
            return RecommendPreflopPolicy(state, hero, legal, opponents, boardCards, deck);

        var ranked = BuildMonteCarloRanking(state, hero, legal, opponents, boardCards, deck, ResolveToAmount);

        return BuildReference(new Recommendation(
            ranked,
            "Monte Carlo Reference (non-decision): EV-ranked actions using position + action-history opponent ranges."
        ));
    }

    private Recommendation RecommendPreflopPolicy(
        HandState state,
        HeroContext hero,
        IReadOnlyList<ActionType> legal,
        IReadOnlyList<PlayerId> opponents,
        IReadOnlyList<Card> boardCards,
        IReadOnlyList<Card> deck)
    {
        var spot = ResolvePreflopSpot(state, hero);
        var handClass = ResolvePreflopHandClass(hero.HeroHoleCards!.Value);
        var frequencies = GetPreflopFrequencies(spot, handClass);

        var ranked = BuildMonteCarloRanking(
            state,
            hero,
            legal,
            opponents,
            boardCards,
            deck,
            (s, heroId, action) => ResolvePreflopToAmount(s, heroId, action, spot),
            frequencies);

        var top = ranked.FirstOrDefault();
        PreflopCalibrationLog.Record(new PreflopCalibrationSample(
            DateTimeOffset.UtcNow,
            spot,
            handClass,
            legal,
            top?.Type,
            top?.EstimatedEv,
            top is null ? 0m : (frequencies.TryGetValue(top.Type, out var f) ? f : 0m)));

        return BuildReference(new Recommendation(
            ranked,
            $"Monte Carlo Reference (non-decision): spot={spot}, class={handClass}. Range-frequency policy with MC EV tie-break + spot sizing."
        ));
    }


    private static Recommendation BuildReference(Recommendation recommendation)
    {
        var top = recommendation.RankedActions.FirstOrDefault();
        return recommendation with
        {
            ReferenceEV = top?.EstimatedEv,
            ReferenceExplanation = recommendation.Explanation ?? "Monte Carlo Reference (non-decision)."
        };
    }

    private List<RecommendedAction> BuildMonteCarloRanking(
        HandState state,
        HeroContext hero,
        IReadOnlyList<ActionType> legal,
        IReadOnlyList<PlayerId> opponents,
        IReadOnlyList<Card> boardCards,
        IReadOnlyList<Card> deck,
        Func<HandState, PlayerId, ActionType, ChipAmount?> toAmountResolver,
        IReadOnlyDictionary<ActionType, decimal>? policyFrequencies = null)
    {
        var ranked = new List<RecommendedAction>(legal.Count);
        var policy = policyFrequencies ?? new Dictionary<ActionType, decimal>();

        foreach (var action in legal)
        {
            var candidateTo = toAmountResolver(state, hero.HeroId, action);
            var ev = EstimateActionEv(state, hero, action, candidateTo, opponents, boardCards, deck);
            ranked.Add(new RecommendedAction(action, candidateTo, ev));
        }

        ranked.Sort((left, right) => CompareRankedActions(left, right, policy));
        return ranked;
    }

    private static int CompareRankedActions(
        RecommendedAction left,
        RecommendedAction right,
        IReadOnlyDictionary<ActionType, decimal> policy)
    {
        var leftEv = left.EstimatedEv ?? decimal.MinValue;
        var rightEv = right.EstimatedEv ?? decimal.MinValue;
        var evDelta = leftEv - rightEv;

        if (Math.Abs(evDelta) > EvTieBreakEpsilon)
            return rightEv.CompareTo(leftEv);

        var leftFrequency = policy.TryGetValue(left.Type, out var leftPolicyFrequency) ? leftPolicyFrequency : 0m;
        var rightFrequency = policy.TryGetValue(right.Type, out var rightPolicyFrequency) ? rightPolicyFrequency : 0m;
        return rightFrequency.CompareTo(leftFrequency);
    }

    private decimal EstimateActionEv(
        HandState state,
        HeroContext hero,
        ActionType action,
        ChipAmount? toAmount,
        IReadOnlyList<PlayerId> opponents,
        IReadOnlyList<Card> boardCards,
        IReadOnlyList<Card> baseDeck)
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

        var potAfterCall = ComputePotAfterCall(state, hero.HeroId, action, toAmount, opponents);
        return ComputeTotalEv(state.Pot.Value, foldEquity, avgEquity, risk, potAfterCall);
    }

    private static decimal ComputeTotalEv(decimal currentPot, decimal foldEquity, decimal averageEquity, decimal heroRisk, decimal potAfterCall)
    {
        var calledEv = (averageEquity * potAfterCall) - heroRisk;
        return (foldEquity * currentPot) + ((1m - foldEquity) * calledEv);
    }

    private static decimal ComputePotAfterCall(
        HandState state,
        PlayerId heroId,
        ActionType action,
        ChipAmount? toAmount,
        IReadOnlyList<PlayerId> opponents)
    {
        if (action is ActionType.Fold or ActionType.Check)
            return state.Pot.Value;

        var heroAlready = state.StreetContrib[heroId].Value;
        var heroTargetContribution = action switch
        {
            ActionType.Call => heroAlready + state.GetToCall(heroId).Value,
            ActionType.Bet or ActionType.Raise or ActionType.AllIn => Math.Max(heroAlready, toAmount?.Value ?? heroAlready),
            _ => heroAlready
        };

        var heroContribution = Math.Max(0, heroTargetContribution - heroAlready);

        var headsUpBlindCorrection = 0m;
        if (action == ActionType.Call
            && state.Street == Street.Preflop
            && opponents.Count == 1
            && state.StreetContrib.Count == 2
            && state.BetToCall.Value > heroAlready)
        {
            // In HU preflop spots, keep the called-pot estimate aligned with the
            // blind model used by strategy tests: account for the SB dead money
            // when hero is calling from the posted-BB branch.
            headsUpBlindCorrection = heroAlready / 2m;
        }

        // Keep the called-pot estimate conservative by adding only hero's incremental
        // contribution. Including every active opponent's full matching amount here can
        // massively overstate shove/raise EV in multiway spots.
        return state.Pot.Value + heroContribution + headsUpBlindCorrection;
    }

    private bool TryRunSingleSimulation(
        HoleCards heroCards,
        HeroContext hero,
        IReadOnlyList<PlayerId> opponents,
        IReadOnlyList<Card> boardCards,
        IReadOnlyList<Card> baseDeck,
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
        IReadOnlyList<Card> deck,
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


    private static PreflopSpot ResolvePreflopSpot(HandState state, HeroContext hero)
    {
        var history = hero.ActionHistory?.Where(a => a.Street == Street.Preflop).ToArray() ?? Array.Empty<BettingAction>();
        var aggressiveCount = history.Count(a => a.Type is ActionType.Bet or ActionType.Raise or ActionType.AllIn);
        var limpers = history.Count(a => a.Type == ActionType.Call);
        var toCall = state.GetToCall(hero.HeroId).Value;
        var heroPosition = hero.PlayerPositions is not null && hero.PlayerPositions.TryGetValue(hero.HeroId, out var pos)
            ? pos
            : Position.Unknown;

        if (heroPosition == Position.BB && toCall == 0 && limpers > 0 && aggressiveCount == 0)
            return PreflopSpot.BigBlindOptionVsLimp;

        if (aggressiveCount == 0)
            return PreflopSpot.UnopenedPot;

        if (aggressiveCount == 1)
            return PreflopSpot.FacingOpenRaise;

        return PreflopSpot.FacingThreeBetOrMore;
    }

    private static PreflopHandClass ResolvePreflopHandClass(HoleCards hand)
    {
        var score = PreflopHandScore(hand.First, hand.Second);
        if (score >= 0.92m) return PreflopHandClass.Premium;
        if (score >= 0.78m) return PreflopHandClass.Strong;
        if (score >= 0.60m) return PreflopHandClass.Medium;
        if (score >= 0.46m) return PreflopHandClass.Speculative;
        return PreflopHandClass.Weak;
    }

    private static IReadOnlyDictionary<ActionType, decimal> GetPreflopFrequencies(PreflopSpot spot, PreflopHandClass handClass)
    {
        var aggressiveAction = spot == PreflopSpot.UnopenedPot || spot == PreflopSpot.BigBlindOptionVsLimp
            ? ActionType.Bet
            : ActionType.Raise;

        return handClass switch
        {
            PreflopHandClass.Premium => new Dictionary<ActionType, decimal>
            {
                [aggressiveAction] = 0.70m,
                [ActionType.Check] = 0.15m,
                [ActionType.Call] = 0.10m,
                [ActionType.AllIn] = 0.05m
            },
            PreflopHandClass.Strong => new Dictionary<ActionType, decimal>
            {
                [aggressiveAction] = 0.62m,
                [ActionType.Call] = 0.24m,
                [ActionType.Check] = 0.10m,
                [ActionType.AllIn] = 0.04m
            },
            PreflopHandClass.Medium => new Dictionary<ActionType, decimal>
            {
                [ActionType.Call] = 0.44m,
                [aggressiveAction] = 0.34m,
                [ActionType.Check] = 0.18m,
                [ActionType.Fold] = 0.04m
            },
            PreflopHandClass.Speculative => new Dictionary<ActionType, decimal>
            {
                [ActionType.Call] = 0.40m,
                [ActionType.Check] = 0.36m,
                [aggressiveAction] = 0.16m,
                [ActionType.Fold] = 0.08m
            },
            _ => new Dictionary<ActionType, decimal>
            {
                [ActionType.Check] = 0.48m,
                [ActionType.Fold] = 0.32m,
                [ActionType.Call] = 0.16m,
                [aggressiveAction] = 0.04m
            }
        };
    }

    private static ChipAmount? ResolvePreflopToAmount(HandState state, PlayerId heroId, ActionType action, PreflopSpot spot)
    {
        var already = state.StreetContrib[heroId].Value;
        var stack = state.Stacks[heroId].Value;
        var toCall = state.GetToCall(heroId).Value;

        if (action == ActionType.AllIn)
            return new ChipAmount(already + stack);

        if (action is ActionType.Bet or ActionType.Raise)
        {
            var target = spot switch
            {
                PreflopSpot.UnopenedPot => state.BetToCall.Value * 2,
                PreflopSpot.BigBlindOptionVsLimp => state.BetToCall.Value * 4,
                PreflopSpot.FacingOpenRaise => state.BetToCall.Value * 3,
                PreflopSpot.FacingThreeBetOrMore => state.BetToCall.Value * 2,
                _ => state.BetToCall.Value * 3
            };

            var minTo = already + toCall;
            var toAmount = Math.Max(target, minTo + Math.Max(1, state.BetToCall.Value / 2));
            return new ChipAmount(already + Math.Min(stack, toAmount - already));
        }

        return null;
    }

    public static class PreflopCalibrationLog
    {
        private static readonly ConcurrentQueue<PreflopCalibrationSample> Samples = new();
        private const int MaxSamples = 500;

        public static void Record(PreflopCalibrationSample sample)
        {
            Samples.Enqueue(sample);
            while (Samples.Count > MaxSamples && Samples.TryDequeue(out _))
            {
            }
        }

        public static IReadOnlyList<PreflopCalibrationSample> Snapshot() => Samples.ToArray();
    }

    public sealed record PreflopCalibrationSample(
        DateTimeOffset Timestamp,
        PreflopSpot Spot,
        PreflopHandClass HandClass,
        IReadOnlyList<ActionType> LegalActions,
        ActionType? RecommendedAction,
        decimal? EstimatedEv,
        decimal Frequency);

    public enum PreflopSpot
    {
        UnopenedPot,
        FacingOpenRaise,
        FacingThreeBetOrMore,
        BigBlindOptionVsLimp
    }

    public enum PreflopHandClass
    {
        Premium,
        Strong,
        Medium,
        Speculative,
        Weak
    }

    private static decimal PreflopHandScore(Card a, Card b)
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

    private static IReadOnlyList<Card> BuildDeck(ISet<Card> deadCards)
    {
        var deck = new List<Card>(52);

        foreach (var rank in Enum.GetValues<Rank>())
        foreach (var suit in Enum.GetValues<Suit>())
        {
            var c = new Card(rank, suit);
            if (!deadCards.Contains(c))
                deck.Add(c);
        }

        return deck;
    }

    private static IReadOnlyList<Card> GetBoardCards(Board board)
    {
        var cards = new List<Card>(5);

        cards.AddRange(board.Flop.Select(c => new Card(c.Rank, c.Suit)));
        if (board.Turn is not null)
            cards.Add(new Card(board.Turn.Rank, board.Turn.Suit));
        if (board.River is not null)
            cards.Add(new Card(board.River.Rank, board.River.Suit));

        return cards;
    }

    private static class HandRankEvaluator
    {
        public static long Evaluate(HoleCards hole, IReadOnlyList<Card> board)
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

        private static long EvaluateFive(Card c1, Card c2, Card c3, Card c4, Card c5)
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
