using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.HandHistory;

namespace PokerAnalyzer.Application.Analysis;

/// <summary>
/// Walks through the hand action list; whenever Hero acts, asks the strategy engine for a recommendation
/// and compares it to the actual action.
/// </summary>
public sealed class HandAnalyzer
{
    private readonly IStrategyEngine _engine;
    private readonly IAllInEquityCalculator? _allInEquityCalculator;
    private readonly IFlopContinuationValueCalculator? _flopContinuationValueCalculator;

    public HandAnalyzer(IStrategyEngine engine) : this(engine, null, null)
    {
    }

    public HandAnalyzer(
        IStrategyEngine engine,
        IAllInEquityCalculator? allInEquityCalculator,
        IFlopContinuationValueCalculator? flopContinuationValueCalculator = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _allInEquityCalculator = allInEquityCalculator;
        _flopContinuationValueCalculator = flopContinuationValueCalculator;
    }

    public HandAnalysisResult Analyze(Domain.HandHistory.Hand hand)
    {
        if (hand.Seats.Count == 0)
            throw new InvalidOperationException("Hand has no seats.");

        var heroCtx = new HeroContext(hand.HeroId, hand.SmallBlind, hand.BigBlind);

        var state = HandState.CreateNewHand(hand.Seats, hand.SmallBlind, hand.BigBlind, Street.Preflop, hand.Board);

        var decisions = new List<DecisionReview>();

        for (var i = 0; i < hand.Actions.Count; i++)
        {
            var a = hand.Actions[i];

            if (a.ActorId == hand.HeroId)
            {
                var rec = _engine.Recommend(state, heroCtx);
                var sev = Score(a, rec);
                decisions.Add(new DecisionReview(
                    ActionIndex: i,
                    Street: a.Street,
                    ActualAction: a,
                    Recommendation: rec,
                    Severity: sev,
                    Notes: rec.Explanation
                ));
            }

            state = state.Apply(a);
        }

        var preflopAllIn = TryComputePreflopAllIn(hand);
        var preflopToFlop = preflopAllIn is null ? TryComputePreflopToFlopTerminal(hand) : null;
        return new HandAnalysisResult(hand.HandId, decisions, preflopAllIn, preflopToFlop);
    }

    private PreflopToFlopTerminalResult? TryComputePreflopToFlopTerminal(Domain.HandHistory.Hand hand)
    {
        if (_flopContinuationValueCalculator is null)
            return null;

        var revealed = BuildKnownCards(hand);
        if (revealed.Count < 2)
            return null;

        var state = HandState.CreateNewHand(hand.Seats, hand.SmallBlind, hand.BigBlind, Street.Preflop, hand.Board);
        for (var i = 0; i < hand.Actions.Count; i++)
        {
            var action = hand.Actions[i];
            if (action.Street != Street.Preflop)
                break;

            state = state.Apply(action);

            var active = state.ActivePlayers.ToList();
            if (active.Count < 2)
                continue;

            var bettingClosed = active.All(p => state.Stacks[p].Value <= 0 || state.GetToCall(p).Value == 0);
            if (!bettingClosed)
                continue;

            var anyStackBehind = active.Any(p => state.Stacks[p].Value > 0);
            if (!anyStackBehind)
                continue;

            var knownPlayers = active.Where(revealed.ContainsKey).Select(p => (p, revealed[p])).ToList();
            if (knownPlayers.Count < 2)
                continue;

            var cv = _flopContinuationValueCalculator.ComputeAsync(knownPlayers, state, 50_000, 12345, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var holeCardsKnown = active.ToDictionary(p => p, p => revealed.TryGetValue(p, out var cards) ? cards : (HoleCards?)null);
            var stacks = active.ToDictionary(p => p, p => (decimal)state.Stacks[p].Value);
            return new PreflopToFlopTerminalResult(
                Street.Preflop,
                active,
                holeCardsKnown,
                state.Pot.Value,
                stacks,
                cv.ChipEv,
                cv.Method,
                cv.FlopsSampled,
                cv.SeedUsed);
        }

        return null;
    }

    private PreflopAllInTerminalResult? TryComputePreflopAllIn(Domain.HandHistory.Hand hand)
    {
        if (_allInEquityCalculator is null)
            return null;

        var revealed = BuildKnownCards(hand);
        if (revealed.Count < 2)
            return null;

        var state = HandState.CreateNewHand(hand.Seats, hand.SmallBlind, hand.BigBlind, Street.Preflop, hand.Board);
        for (var i = 0; i < hand.Actions.Count; i++)
        {
            var action = hand.Actions[i];
            if (action.Street != Street.Preflop)
                break;

            state = state.Apply(action);

            var active = state.ActivePlayers.ToList();
            var allInCount = active.Count(p => state.Stacks[p].Value <= 0);
            var effectivelyClosed = active.All(p => state.Stacks[p].Value <= 0 || state.GetToCall(p).Value == 0);
            if (allInCount < 2 || !effectivelyClosed)
                continue;

            var knownPlayers = active.Where(revealed.ContainsKey).Select(p => (p, revealed[p])).ToList();
            if (knownPlayers.Count < 2)
                continue;

            var equity = _allInEquityCalculator.ComputePreflopAsync(knownPlayers, samples: knownPlayers.Count == 2 ? null : 100_000, seed: 12345, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var holeCardsKnown = active.ToDictionary(p => p, p => revealed.TryGetValue(p, out var cards) ? cards : (HoleCards?)null);
            var committed = active.ToDictionary(p => p, p => (decimal)state.StreetContrib[p].Value);
            return new PreflopAllInTerminalResult(
                Street.Preflop,
                active,
                holeCardsKnown,
                state.Pot.Value,
                committed,
                equity.Equities,
                equity.Method,
                equity.SamplesUsed,
                equity.SeedUsed);
        }

        return null;
    }

    private static Dictionary<PlayerId, HoleCards> BuildKnownCards(Domain.HandHistory.Hand hand)
    {
        var revealed = hand.RevealedHoleCards is null
            ? new Dictionary<PlayerId, HoleCards>()
            : new Dictionary<PlayerId, HoleCards>(hand.RevealedHoleCards);
        if (hand.HeroHoleCards.HasValue)
            revealed[hand.HeroId] = hand.HeroHoleCards.Value;
        return revealed;
    }

    private static DecisionSeverity Score(BettingAction actual, Recommendation rec)
    {
        if (rec.RankedActions.Count == 0)
            return DecisionSeverity.Unknown;

        var top = rec.RankedActions[0];

        if (top.Type != actual.Type)
            return DecisionSeverity.Mistake;

        if (top.ToAmount is not null && (actual.Type is ActionType.Bet or ActionType.Raise or ActionType.AllIn))
        {
            if (top.ToAmount.Value != actual.Amount)
                return DecisionSeverity.Inaccuracy;
        }

        return DecisionSeverity.Ok;
    }
}
