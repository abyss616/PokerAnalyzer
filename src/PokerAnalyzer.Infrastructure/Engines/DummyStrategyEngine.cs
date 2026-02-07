using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

/// <summary>
/// Placeholder engine. Produces deterministic but naive recommendations based on basic legality.
/// Replace with heuristic/EV/solver engines later.
/// </summary>
public sealed class DummyStrategyEngine : IStrategyEngine
{
    public Recommendation Recommend(HandState state, HeroContext hero)
    {
        var legal = state.GetLegalActions(hero.HeroId);

        if (legal.Count == 0)
            return new Recommendation(Array.Empty<RecommendedAction>(), "No legal actions detected (hero may have folded).");

        // Prefer check when available; otherwise call; otherwise fold.
        ActionType top =
            legal.Contains(ActionType.Check) ? ActionType.Check :
            legal.Contains(ActionType.Call) ? ActionType.Call :
            legal.Contains(ActionType.Fold) ? ActionType.Fold :
            legal[0];

        var ranked = new List<RecommendedAction>();

        if (top is ActionType.Bet or ActionType.Raise or ActionType.AllIn)
        {
            // Default to "all-in" only if no other sizing is possible; otherwise suggest a small bet/raise.
            var toCall = state.GetToCall(hero.HeroId);
            var already = state.StreetContrib[hero.HeroId];
            var stack = state.Stacks[hero.HeroId];

            // naive sizing: 1/2 pot bet if betting; or raise to 3x (betToCall) if raising
            if (top == ActionType.Bet)
            {
                var halfPot = new ChipAmount(Math.Max(1, state.Pot.Value / 2));
                var toAmount = new ChipAmount(already.Value + Math.Min(halfPot.Value, stack.Value));
                ranked.Add(new RecommendedAction(ActionType.Bet, toAmount));
            }
            else if (top == ActionType.Raise)
            {
                var raiseTo = new ChipAmount(Math.Max(state.BetToCall.Value * 3, already.Value + toCall.Value * 2));
                var capped = new ChipAmount(already.Value + Math.Min(stack.Value, raiseTo.Value - already.Value));
                ranked.Add(new RecommendedAction(ActionType.Raise, capped));
            }
            else
            {
                ranked.Add(new RecommendedAction(ActionType.AllIn, new ChipAmount(already.Value + stack.Value)));
            }
        }
        else
        {
            ranked.Add(new RecommendedAction(top));
        }

        // add other legal actions as alternates (no sizing)
        foreach (var t in legal.Where(t => t != ranked[0].Type))
            ranked.Add(new RecommendedAction(t));

        return new Recommendation(ranked, "Dummy engine: legality-based recommendation only.");
    }
}
