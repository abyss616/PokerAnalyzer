using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopSolver;

public sealed class HeuristicPreflopLeafEvaluator : IPreflopLeafEvaluator
{
    private const double LimpNoInitiativePenalty = 0.18;
    private const double RaiseInitiativeBonus = 0.22;
    private const double RaiseInvestmentPenaltyFactor = 0.75;

    public PreflopLeafEvaluation Evaluate(SolverHandState leafState)
    {
        ArgumentNullException.ThrowIfNull(leafState);

        var utility = leafState.Players.ToDictionary(player => player.PlayerId, _ => 0d);

        if (!TryBuildUnopenedFirstInContext(leafState, out var context, out var fallbackReason))
        {
            return new PreflopLeafEvaluation(utility, fallbackReason);
        }

        var foldEv = 0d;
        var limpEv = CalculateLimpEv(context);
        var raiseEv = CalculateRaiseEv(context);

        var chosenEv = context.FirstVoluntaryAction.ActionType switch
        {
            ActionType.Fold => foldEv,
            ActionType.Call => limpEv,
            ActionType.Raise => raiseEv,
            _ => 0d
        };

        utility[context.Hero.PlayerId] = chosenEv;

        var reason =
            $"heuristic preflop unopened first-in: hand={context.HandLabel}, pos={context.Hero.Position}, stackBb={context.EffectiveStackBb:0.0}, " +
            $"strength={context.HandStrength:0.000}, action={context.FirstVoluntaryAction.ActionType}, evFold={foldEv:0.000}, evLimp={limpEv:0.000}, evRaise={raiseEv:0.000}, chosen={chosenEv:0.000}";

        return new PreflopLeafEvaluation(utility, reason);
    }

    private static bool TryBuildUnopenedFirstInContext(
        SolverHandState state,
        out UnopenedFirstInContext context,
        out string reason)
    {
        context = default;

        if (state.Street != Street.Preflop)
        {
            reason = "heuristic preflop evaluator fallback: non-preflop node";
            return false;
        }

        var voluntaryActions = state.ActionHistory
            .Where(a => IsVoluntaryPreflopAction(a.ActionType))
            .ToArray();

        if (voluntaryActions.Length == 0)
        {
            reason = "heuristic preflop evaluator fallback: no voluntary preflop action yet";
            return false;
        }

        var firstVoluntary = voluntaryActions[0];
        if (firstVoluntary.ActionType is not (ActionType.Fold or ActionType.Call or ActionType.Raise))
        {
            reason = "heuristic preflop evaluator fallback: unsupported first-in action type";
            return false;
        }

        if (voluntaryActions.Skip(1).Any(a => a.ActionType is ActionType.Bet or ActionType.Raise or ActionType.AllIn))
        {
            reason = "heuristic preflop evaluator fallback: facing preflop aggression after first action";
            return false;
        }

        var hero = state.Players.FirstOrDefault(p => p.PlayerId == firstVoluntary.PlayerId);
        if (hero is null)
        {
            reason = "heuristic preflop evaluator fallback: hero not found in player list";
            return false;
        }

        if (!state.PrivateCardsByPlayer.TryGetValue(hero.PlayerId, out var heroCards))
        {
            reason = "heuristic preflop evaluator fallback: missing hero private cards";
            return false;
        }

        var effectiveStack = ResolveEffectiveStackBb(state, hero);
        var handStrength = EvaluateHandStrength(heroCards);

        context = new UnopenedFirstInContext(
            Hero: hero,
            HeroCards: heroCards,
            HandLabel: ToCanonicalPreflopHand(heroCards),
            FirstVoluntaryAction: firstVoluntary,
            HandStrength: handStrength,
            PositionFactor: PositionFactor(hero.Position),
            EffectiveStackBb: effectiveStack,
            FoldEquityEstimate: EstimateFoldEquity(hero.Position));

        reason = string.Empty;
        return true;
    }

    private static double CalculateLimpEv(UnopenedFirstInContext context)
    {
        var stackAdj = StackDepthAdjustment(context.EffectiveStackBb);
        return ((context.HandStrength - 0.42) * 1.8)
             + (context.PositionFactor * 0.8)
             + (stackAdj * 0.35)
             - LimpNoInitiativePenalty;
    }

    private static double CalculateRaiseEv(UnopenedFirstInContext context)
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

    private static bool IsVoluntaryPreflopAction(ActionType actionType)
        => actionType is not (ActionType.PostSmallBlind or ActionType.PostBigBlind or ActionType.SitOut);

    private static double ResolveEffectiveStackBb(SolverHandState state, SolverPlayerState hero)
    {
        var villainMaxContribution = state.Players
            .Where(p => p.PlayerId != hero.PlayerId && p.IsActive)
            .Select(p => p.Stack.Value + p.CurrentStreetContribution.Value)
            .DefaultIfEmpty(hero.Stack.Value + hero.CurrentStreetContribution.Value)
            .Min();

        var heroTotal = hero.Stack.Value + hero.CurrentStreetContribution.Value;
        var effectiveChips = Math.Min(heroTotal, villainMaxContribution);
        return effectiveChips / (double)Math.Max(1L, state.Config.BigBlind.Value);
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

    private readonly record struct UnopenedFirstInContext(
        SolverPlayerState Hero,
        HoleCards HeroCards,
        string HandLabel,
        SolverActionEntry FirstVoluntaryAction,
        double HandStrength,
        double PositionFactor,
        double EffectiveStackBb,
        double FoldEquityEstimate);
}
