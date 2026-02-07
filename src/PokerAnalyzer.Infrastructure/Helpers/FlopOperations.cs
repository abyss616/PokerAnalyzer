using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Helpers
{
    public static class FlopOperations
    {
        public static string? CalculateFlopAggressor(IEnumerable<HandAction> actions)
        {
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));

            // Only hands that reach the flop can have a flop aggressor
            if (!actions.Any(a => a.Street == Street.Flop))
                return null;

            string? lastAggressor = null;

            foreach (var action in actions.Where(a => a.Street == Street.Preflop))
            {
                if (action.Type == ActionType.Raise || action.Type == ActionType.Bet)
                {
                    lastAggressor = action.Player;
                }
            }

            return lastAggressor;
        }

        public static bool IsAggressive(this ActionType type) =>
    type == ActionType.Raise || type == ActionType.Bet;

        /// <summary>
        /// True if the preflop aggressor had the chance to c-bet on the flop
        /// (i.e., no one bet before their first flop decision).
        /// </summary>
        public static (string? OpportunityPlayer, string? CBetPlayer) GetFlopCBetResult(IEnumerable<HandAction> actions)
        { 
            if (actions is null)
                throw new ArgumentNullException(nameof(actions));

            // Must reach flop
            if (!actions.Any(a => a.Street == Street.Flop))
                return (null, null);

            // Preflop aggressor
            var aggressor = CalculateFlopAggressor(actions);
            if (aggressor is null)
                return (null, null);

            foreach (var action in actions.Where(a => a.Street == Street.Flop))
            {
                // Someone else bet first → donk → no c-bet opportunity
                if (action.Type == ActionType.Bet && action.Player != aggressor)
                    return (null, null);

                // Aggressor's first flop action
                if (action.Player == aggressor)
                {
                    // He had the opportunity
                    if (action.Type == ActionType.Bet)
                        return (aggressor, aggressor); // c-bet

                    // Check / fold → opportunity but no c-bet
                    return (aggressor, null);
                }
            }

            // Should not normally reach here
            return (null, null);
        }

        public static (string? DonkBettor, string? AgainstAggressor) GetFlopDonkBet(IEnumerable<HandAction> actions)
        {
            if (actions is null)
                throw new ArgumentNullException(nameof(actions));

            if (!actions.Any(a => a.Street == Street.Flop))
                return (null, null);

            var aggressor = CalculateFlopAggressor(actions);
            if (aggressor is null)
                return (null, null);

            foreach (var action in actions.Where(a => a.Street == Street.Flop))
            {
                // First bet on flop determines whether there's a donk
                if (action.Type == ActionType.Bet)
                {
                    if (action.Player != aggressor)
                        return (action.Player, aggressor); // donk bet

                    return (null, aggressor); // aggressor bet first => c-bet line, not a donk
                }

                // If you see a raise before any bet, your HH encoding is unusual,
                // but you could treat it as "not a donk bet" or handle separately.
            }

            return (null, aggressor);
        }

        public static (string? Folder, string? VsAggressor) GetFirstFoldToFlopCBet(IEnumerable<HandAction> actions)
        {
            if (actions is null)
                throw new ArgumentNullException(nameof(actions));

            if (!actions.Any(a => a.Street == Street.Flop))
                return (null, null);

            var aggressor = CalculateFlopAggressor(actions);
            if (aggressor is null)
                return (null, null);

            bool cBetOccurred = false;

            foreach (var action in actions.Where(a => a.Street == Street.Flop))
            {
                if (!cBetOccurred)
                {
                    // If someone else bets first, it's a donk line => no flop c-bet occurred
                    if (action.Type == ActionType.Bet && action.Player != aggressor)
                        return (null, null);

                    // If aggressor bets first, that's a flop c-bet
                    if (action.Type == ActionType.Bet && action.Player == aggressor)
                    {
                        cBetOccurred = true;
                        continue;
                    }

                    // If aggressor checks first, no c-bet this street
                    if (action.Player == aggressor && action.Type != ActionType.Bet)
                        return (null, aggressor); // vsAggressor is known, but no fold-to-cbet possible
                }
                else
                {
                    // After c-bet happens, find first fold by any opponent
                    if (action.Type == ActionType.Fold && action.Player != aggressor)
                        return (action.Player, aggressor);

                    // If your HH includes "raise" here, that's not a fold; keep scanning.
                }
            }

            return (null, cBetOccurred ? aggressor : aggressor);
        }


    }

}
