using Microsoft.Extensions.Logging;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Analysis;

/// <summary>
/// Walks through the hand action list; whenever Hero acts, asks the strategy engine for a recommendation
/// and compares it to the actual action.
/// </summary>
public sealed class HandAnalyzer
{
    private readonly IStrategyEngine _engine;
    private readonly ILogger<HandAnalyzer> _logger;

    public HandAnalyzer(IStrategyEngine engine)
        : this(engine, Microsoft.Extensions.Logging.Abstractions.NullLogger<HandAnalyzer>.Instance)
    {
    }

    public HandAnalyzer(IStrategyEngine engine, ILogger<HandAnalyzer> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public HandAnalysisResult Analyze(Domain.HandHistory.Hand hand)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (hand.Seats.Count == 0)
            throw new InvalidOperationException("Hand has no seats.");

        _logger.LogInformation(
            "Start analyze. HandId={HandId}, Street={Street}, Players={Players}, Actions={Actions}, UtcStart={UtcStart}",
            hand.HandId,
            Street.Preflop,
            hand.Seats.Count,
            hand.Actions.Count,
            startedAt);

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
                var villainId = ResolveVillainId(hand.HeroId, state, hand.Actions.Take(i));
                if (hand.HeroId == villainId)
                    throw new InvalidOperationException("Invalid decision context: hero and villain must be different PlayerIds.");

                _logger.LogInformation(
                    "Decision point. ActionIndex={ActionIndex}, Street={Street}, HeroToCall={HeroToCall}, Pot={Pot}, Stacks={Stacks}, LastActionsTail={LastActionsTail}",
                    i,
                    a.Street,
                    state.GetToCall(hand.HeroId).Value,
                    state.Pot.Value,
                    string.Join(", ", state.Stacks.Select(stack => $"{stack.Key}:{stack.Value.Value}")),
                    string.Join(" | ", hand.Actions.Take(i).TakeLast(5).Select(action => $"{action.Street}:{action.ActorId}:{action.Type}:{action.Amount.Value}")));

                var villainSeat = hand.Seats.FirstOrDefault(seat => seat.Id == villainId);
                _logger.LogInformation(
                    "Resolve villain. VillainId={VillainId}, VillainPosition={VillainPosition}",
                    villainId,
                    villainSeat?.Position);

                var decisionCtx = heroCtx with
                {
                    HeroHoleCards = hand.HeroHoleCards,
                    PlayerPositions = positionMap,
                    ActionHistory = hand.Actions.Take(i).ToArray()
                };

                var rec = EnsureLegalRecommendation(state, hand.HeroId, _engine.Recommend(state, decisionCtx));
                var primaryAction = rec.PrimaryAction ?? rec.RankedActions.FirstOrDefault();
                var isUnsupported = primaryAction is null;
                _logger.LogInformation(
                    "Engine recommend. EngineName={EngineName}, Supported={Supported}, ReasonIfUnsupported={ReasonIfUnsupported}",
                    _engine.GetType().Name,
                    !isUnsupported,
                    isUnsupported ? rec.PrimaryExplanation : null);

                var sev = Score(a, rec);
                _logger.LogInformation(
                    "Compare actual vs recommended. Actual={Actual}, RecommendedPrimary={RecommendedPrimary}, Severity={Severity}, EV={EV}, ReferenceEV={ReferenceEV}",
                    a.Type,
                    primaryAction?.Type,
                    sev,
                    rec.PrimaryEV,
                    rec.ReferenceEV);

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

        stopwatch.Stop();
        var unsupportedCount = decisions.Count(decision =>
            decision.Recommendation.PrimaryAction is null && decision.Recommendation.RankedActions.Count == 0);
        _logger.LogInformation(
            "Analysis done. DecisionCount={DecisionCount}, UnsupportedCount={UnsupportedCount}, DurationMs={DurationMs}",
            decisions.Count,
            unsupportedCount,
            stopwatch.ElapsedMilliseconds);

        return new HandAnalysisResult(hand.HandId, decisions);
    }

    private static Recommendation EnsureLegalRecommendation(HandState handState, PlayerId heroId, Recommendation recommendation)
    {
        var recommendedAction = recommendation.EffectivePrimaryAction;
        if (recommendedAction is null)
            return recommendation;

        var legal = handState.GetLegalActions(heroId);
        if (!legal.Contains(recommendedAction.Type))
        {
            return Recommendation.Invalid(
                $"Illegal recommendation: {recommendedAction.Type} when toCall={handState.GetToCall(heroId).Value}. Legal=[{string.Join(",", legal)}]");
        }

        return recommendation;
    }

    private static PlayerId ResolveVillainId(PlayerId heroId, HandState state, IEnumerable<BettingAction> actionHistory)
    {
        foreach (var action in actionHistory.Reverse())
        {
            if (action.ActorId != heroId)
                return action.ActorId;
        }

        return state.ActivePlayers.First(id => id != heroId);
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
