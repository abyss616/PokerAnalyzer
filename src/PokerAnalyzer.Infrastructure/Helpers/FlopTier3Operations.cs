using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Helpers
{
    public static class FlopTier3Operations
    {
        public static (string? ProbeBettor, string? AfterAggressorCheck)
            GetFlopProbeBet(IEnumerable<HandAction> actions)
        {
            if (actions is null) throw new ArgumentNullException(nameof(actions));
            var orderedActions = actions.OrderBy(a => a.SequenceNumber).ThenBy(a => a.Id).ToList();
            if (!orderedActions.Any(a => a.Street == Street.Flop)) return (null, null);

            var aggressor = FlopOperations.CalculateFlopAggressor(orderedActions);
            if (aggressor is null) return (null, null);

            bool aggressorCheckedFirst = false;

            foreach (var a in orderedActions.Where(a => a.Street == Street.Flop))
            {
                // If someone donks before aggressor acts, no probe spot (this is a donk line)
                if (!aggressorCheckedFirst && a.Type == ActionType.Bet && a.Player != aggressor)
                    return (null, null);

                if (a.Player == aggressor && !aggressorCheckedFirst)
                {
                    // Probe spot only exists if aggressor checks first
                    if (a.Type == ActionType.Check)
                    {
                        aggressorCheckedFirst = true;
                        continue;
                    }

                    // Aggressor bet first => c-bet line; no probe spot
                    return (null, aggressor);
                }

                // After aggressor checks, the first bet by someone else is a probe
                if (aggressorCheckedFirst && a.Type == ActionType.Bet && a.Player != aggressor)
                    return (a.Player, aggressor);
            }

            return (null, aggressor);
        }


        public static FlopTexture ClassifyFlopTexture(IReadOnlyList<Card> flop)
        {        
            if (flop is null) throw new ArgumentNullException(nameof(flop));
            if (flop.Count != 3) throw new ArgumentException("Flop must contain exactly 3 cards.", nameof(flop));

            bool isPaired = flop.Select(c => c.Rank).Distinct().Count() != 3;

            var suitGroups = flop.GroupBy(c => c.Suit).Select(g => g.Count()).OrderByDescending(x => x).ToList();
            bool isMonotone = suitGroups[0] == 3;
            bool isTwoTone = suitGroups[0] == 2;
            bool isRainbow = suitGroups[0] == 1;

            bool hasAceOrKing = flop.Any(c => c.Rank == Rank.Ace || c.Rank == Rank.King);

            // Broadway = T,J,Q,K,A (adjust if your Rank enum differs)
            bool isBroadway(Rank r) =>
                r == Rank.Ten || r == Rank.Jack || r == Rank.Queen || r == Rank.King || r == Rank.Ace;

            bool hasTwoBroadways = flop.Count(c => isBroadway(c.Rank)) >= 2;

            return new FlopTexture
            {
                IsPaired = isPaired,
                IsMonotone = isMonotone,
                IsTwoTone = isTwoTone,
                IsRainbow = isRainbow,
                HasAceOrKing = hasAceOrKing,
                HasTwoBroadways = hasTwoBroadways
            };
        }
    }
}
