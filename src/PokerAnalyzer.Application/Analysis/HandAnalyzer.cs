using PokerAnalyzer.Application.Engines;
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

    public HandAnalyzer(IStrategyEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public HandAnalysisResult Analyze(Domain.HandHistory.Hand hand)
    {
        if (hand.Seats.Count == 0)
            throw new InvalidOperationException("Hand has no seats.");

        var heroCtx = new HeroContext(hand.HeroId, hand.SmallBlind, hand.BigBlind);
        var positionMap = hand.Seats.ToDictionary(s => s.Id, s => s.Position);
        ValidateDistinctHeroAndVillainPositions(hand, positionMap);

        var state = HandState.CreateNewHand(hand.Seats, hand.SmallBlind, hand.BigBlind, Street.Preflop, hand.Board);

        var decisions = new List<DecisionReview>();

        for (var i = 0; i < hand.Actions.Count; i++)
        {
            var a = hand.Actions[i];

            if (state.Street != a.Street)
                state = state.TransitionToStreet(a.Street);

            // Variant A architecture: forced blinds are part of CreateNewHand setup, not runtime decisions.
            if (a.Type is ActionType.PostSmallBlind or ActionType.PostBigBlind)
                continue;

            // Hero decision point: compare action vs engine recommendation
            if (a.ActorId == hand.HeroId)
            {
                var decisionCtx = heroCtx with
                {
                    HeroHoleCards = hand.HeroHoleCards,
                    PlayerPositions = positionMap,
                    ActionHistory = hand.Actions.Take(i).ToArray()
                };

                var rec = _engine.Recommend(state, decisionCtx);
                var sev = Score(a, rec);
                decisions.Add(new DecisionReview(
                    ActionIndex: i,
                    Street: a.Street,
                    ActualAction: a,
                    Recommendation: rec,
                    Severity: sev,
                    Notes: rec.PrimaryExplanation
                ));
            }

            // Apply action to advance the state
            state = state.Apply(a);
        }

        return new HandAnalysisResult(hand.HandId, decisions);
    }


    private static void ValidateDistinctHeroAndVillainPositions(
        Domain.HandHistory.Hand hand,
        IReadOnlyDictionary<PlayerId, Position> positionMap)
    {
        if (!positionMap.TryGetValue(hand.HeroId, out var heroPosition))
            throw new InvalidOperationException("Invalid hand state: Hero seat was not found in position map.");

        var villainPosition = hand.Seats
            .Where(seat => seat.Id != hand.HeroId)
            .Select(seat => positionMap[seat.Id])
            .FirstOrDefault(position => position == heroPosition);

        if (villainPosition == heroPosition)
        {
            throw new InvalidOperationException(
                $"Invalid hand state: Hero and Villain cannot have the same position ({heroPosition}).");
        }
    }

    private static DecisionSeverity Score(BettingAction actual, Recommendation rec)
    {
        var top = rec.PrimaryAction ?? rec.RankedActions.FirstOrDefault();
        if (top is null)
            return DecisionSeverity.Unknown;

        // v0 scoring: "OK" if the top recommended action matches type (and for bet/raise/all-in matches to-amount if provided)

        if (top.Type != actual.Type)
            return DecisionSeverity.Mistake;

        if (top.ToAmount is not null && (actual.Type is ActionType.Bet or ActionType.Raise or ActionType.AllIn))
        {
            // For v0, require exact match when engine specifies a to-amount
            if (top.ToAmount.Value != actual.Amount)
                return DecisionSeverity.Inaccuracy;
        }

        return DecisionSeverity.Ok;
    }
}
