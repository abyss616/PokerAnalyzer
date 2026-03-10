using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopSolver;

public sealed class HeuristicPreflopLeafEvaluator : IPreflopLeafEvaluator
{
    private const double LimpNoInitiativePenalty = 0.18;
    private const double RaiseInitiativeBonus = 0.22;
    private const double RaiseInvestmentPenaltyFactor = 0.75;

    public PreflopLeafEvaluation Evaluate(PreflopLeafEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var utility = context.LeafState.Players.ToDictionary(player => player.PlayerId, _ => 0d);

        if (!IsSupportedPreflopUnopenedAction(context.RootAction))
        {
            return new PreflopLeafEvaluation(
                utility,
                $"heuristic preflop evaluator fallback: unsupported root action {context.RootAction.ActionType}");
        }

        var handLabel = ToCanonicalPreflopHand(context.HeroCards);
        var handStrength = EvaluateHandStrength(context.HeroCards);
        var positionFactor = PositionFactor(context.HeroPosition);
        var foldEquityEstimate = EstimateFoldEquity(context.HeroPosition);

        var rootContext = new RootActionContext(
            handStrength,
            positionFactor,
            context.RootEffectiveStackBb,
            foldEquityEstimate);

        var foldEv = 0d;
        var limpEv = CalculateLimpEv(rootContext);
        var raiseEv = CalculateRaiseEv(rootContext);

        var chosenEv = context.RootAction.ActionType switch
        {
            ActionType.Fold => foldEv,
            ActionType.Call => limpEv,
            ActionType.Raise => raiseEv,
            _ => 0d
        };

        utility[context.HeroPlayerId] = chosenEv;

        var reason =
            $"heuristic preflop root-action: hand={handLabel}, rootAction={context.RootAction.ActionType}, pos={context.HeroPosition}, stackBb={context.RootEffectiveStackBb:0.0}, " +
            $"solverKey={context.SolverKey ?? \"n/a\"}, strength={handStrength:0.000}, evFold={foldEv:0.000}, evLimp={limpEv:0.000}, evRaise={raiseEv:0.000}, chosen={chosenEv:0.000}";

        return new PreflopLeafEvaluation(utility, reason);
    }

    private static bool IsSupportedPreflopUnopenedAction(LegalAction rootAction)
    {
        if (rootAction.ActionType == ActionType.Fold)
            return true;

        if (rootAction.ActionType == ActionType.Call)
            return true;

        return rootAction.ActionType == ActionType.Raise;
    }

    private static double CalculateLimpEv(RootActionContext context)
    {
        var stackAdj = StackDepthAdjustment(context.EffectiveStackBb);
        return ((context.HandStrength - 0.42) * 1.8)
             + (context.PositionFactor * 0.8)
             + (stackAdj * 0.35)
             - LimpNoInitiativePenalty;
    }

    private static double CalculateRaiseEv(RootActionContext context)
    {
        var stackAdj = StackDepthAdjustment(context.EffectiveStackBb);
        var openSizeBb = 2.5;
        var immediatePotWinBb = 1.5;

        var foldEquityComponent = context.FoldEquityEstimate * immediatePotWinBb;
        var continueComponent = ((context.HandStrength - 0.45) * 3.0)
                              + (context.PositionFactor * 0.65)
                              + (stackAdj * 0.3)
                              + RaiseInitiativeBonus;

        var weakness = Math.Clamp(0.6 - context.HandStrength, 0d, 0.6) / 0.6;
        var riskPenalty = (1d - context.FoldEquityEstimate)
                        * (openSizeBb - 1d)
                        * weakness
                        * RaiseInvestmentPenaltyFactor;

        return foldEquityComponent + continueComponent - riskPenalty;
    }

    private static double EvaluateHandStrength(HoleCards holeCards)
    {
        var first = holeCards.First;
        var second = holeCards.Second;

        var highRank = Math.Max((int)first.Rank, (int)second.Rank);
        var lowRank = Math.Min((int)first.Rank, (int)second.Rank);
        var highNorm = (highRank - 2d) / 12d;
        var lowNorm = (lowRank - 2d) / 12d;

        var isPair = first.Rank == second.Rank;
        var isSuited = first.Suit == second.Suit;
        var isBroadwayHigh = highRank >= 10;
        var isBroadwayLow = lowRank >= 10;

        var score = 0d;
        if (isPair)
        {
            score += 0.45 + (0.35 * highNorm);
        }
        else
        {
            score += (0.35 * highNorm) + (0.25 * lowNorm);

            if (isSuited)
                score += 0.08;

            if (isBroadwayHigh && isBroadwayLow)
                score += 0.08;

            if (highRank == (int)Rank.Ace)
                score += 0.05;

            var gap = Math.Max(0, highRank - lowRank - 1);
            score += gap switch
            {
                0 => 0.07,
                1 => 0.04,
                2 => 0.01,
                _ => Math.Max(-0.1, -0.02 * (gap - 2))
            };

            if (highRank <= 9 && gap >= 3)
                score -= 0.06;
        }

        return Math.Clamp(score, 0d, 1d);
    }

    private static double PositionFactor(Position position) => position switch
    {
        Position.UTG => -0.12,
        Position.UTG1 => -0.10,
        Position.UTG2 => -0.08,
        Position.LJ => -0.04,
        Position.HJ => 0.00,
        Position.CO => 0.06,
        Position.BTN => 0.12,
        Position.SB => -0.04,
        Position.BB => -0.09,
        _ => -0.05
    };

    private static double EstimateFoldEquity(Position position) => position switch
    {
        Position.UTG => 0.20,
        Position.UTG1 => 0.22,
        Position.UTG2 => 0.24,
        Position.LJ => 0.26,
        Position.HJ => 0.29,
        Position.CO => 0.33,
        Position.BTN => 0.38,
        Position.SB => 0.25,
        Position.BB => 0.15,
        _ => 0.24
    };

    private static double StackDepthAdjustment(double effectiveStackBb)
        => Math.Clamp((effectiveStackBb - 100d) / 220d, -0.15, 0.15);

    private static string ToCanonicalPreflopHand(HoleCards holeCards)
    {
        var first = holeCards.First;
        var second = holeCards.Second;

        if (first.Rank == second.Rank)
        {
            var pairRank = RankToChar(first.Rank);
            return string.Concat(pairRank, pairRank);
        }

        var (high, low) = first.Rank > second.Rank ? (first, second) : (second, first);
        var suitedness = first.Suit == second.Suit ? 's' : 'o';

        return string.Concat(RankToChar(high.Rank), RankToChar(low.Rank), suitedness);
    }

    private static char RankToChar(Rank rank) => rank switch
    {
        Rank.Two => '2',
        Rank.Three => '3',
        Rank.Four => '4',
        Rank.Five => '5',
        Rank.Six => '6',
        Rank.Seven => '7',
        Rank.Eight => '8',
        Rank.Nine => '9',
        Rank.Ten => 'T',
        Rank.Jack => 'J',
        Rank.Queen => 'Q',
        Rank.King => 'K',
        Rank.Ace => 'A',
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };

    private readonly record struct RootActionContext(
        double HandStrength,
        double PositionFactor,
        double EffectiveStackBb,
        double FoldEquityEstimate);
}
