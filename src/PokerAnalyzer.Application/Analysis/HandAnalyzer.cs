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

        var state = HandState.CreateNewHand(hand.Seats, hand.SmallBlind, hand.BigBlind, Street.Preflop, hand.Board);

        var decisions = new List<DecisionReview>();

        for (var i = 0; i < hand.Actions.Count; i++)
        {
            var a = hand.Actions[i];

            if (a.Street != state.Street)
                state = state.AdvanceStreet(a.Street);

            // Hero decision point: compare action vs engine recommendation
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

            // Apply action to advance the state
            state = state.Apply(a);
        }

        return new HandAnalysisResult(hand.HandId, decisions);
    }

    private static DecisionSeverity Score(BettingAction actual, Recommendation rec)
    {
        if (rec.RankedActions.Count == 0)
            return DecisionSeverity.Unknown;

        // v0 scoring: "OK" if the top recommended action matches type (and for bet/raise/all-in matches to-amount if provided)
        var top = rec.RankedActions[0];

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
