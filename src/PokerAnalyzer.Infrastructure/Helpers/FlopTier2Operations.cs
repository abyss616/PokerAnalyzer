using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Helpers
{
    public static class FlopTier2Operations
    {
        // ---------- Tier 2: Raise vs Flop C-Bet ----------
        // Returns (Raiser, VsAggressor). If no raise vs c-bet, returns (null, aggressorOrNull).
        public static (string? Raiser, string? VsAggressor)
            GetFirstRaiseVsFlopCBet(IEnumerable<HandAction> actions)
        {
            if (actions is null) throw new ArgumentNullException(nameof(actions));

            var cbet = TryGetFlopCBetPlayer(actions);
            if (cbet is null)
                return (null, null);

            bool afterCBet = false;

            foreach (var a in actions.Where(a => a.Street == Street.Flop))
            {
                if (!afterCBet)
                {
                    if (a.Type == ActionType.Bet && a.Player == cbet)
                        afterCBet = true;

                    continue;
                }

                // First raise by anyone other than the c-bettor
                if (a.Type == ActionType.Raise && a.Player != cbet)
                    return (a.Player, cbet);

                // If your HH encodes "all-in raise" as Raise, still covered.
            }

            return (null, cbet);
        }

        // ---------- Tier 2: Call vs Flop C-Bet ----------
        // Returns (Caller, VsAggressor). If no call vs c-bet, returns (null, aggressorOrNull).
        public static (string? Caller, string? VsAggressor)
            GetFirstCallVsFlopCBet(IEnumerable<HandAction> actions)
        {
            if (actions is null) throw new ArgumentNullException(nameof(actions));

            var cbet = TryGetFlopCBetPlayer(actions);
            if (cbet is null)
                return (null, null);

            bool afterCBet = false;

            foreach (var a in actions.Where(a => a.Street == Street.Flop))
            {
                if (!afterCBet)
                {
                    if (a.Type == ActionType.Bet && a.Player == cbet)
                        afterCBet = true;

                    continue;
                }

                if (a.Type == ActionType.Call && a.Player != cbet)
                    return (a.Player, cbet);
            }

            return (null, cbet);
        }

        // ---------- Tier 2: Multiway Flop C-Bet ----------
        // Returns the aggressor name if they c-bet in a multiway pot; otherwise null.
        //
        // Multiway definition (simple & robust):
        //   Count distinct players who take ANY flop action.
        //   If >= 3 => multiway on flop.
        //
        // This avoids needing explicit "players dealt in" state.
        public static string? GetMultiwayFlopCBetPlayer(IEnumerable<HandAction> actions)
        {
            if (actions is null) throw new ArgumentNullException(nameof(actions));

            if (!actions.Any(a => a.Street == Street.Flop))
                return null;

            var flopPlayers = actions
                .Where(a => a.Street == Street.Flop)
                .Select(a => a.Player)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            bool isMultiway = flopPlayers.Count >= 3;
            if (!isMultiway)
                return null;

            // Who c-bet?
            var cbet = TryGetFlopCBetPlayer(actions);
            return cbet; // will be null if no c-bet happened
        }

        // ======================================================
        // Internal helper: returns preflop aggressor IF they made
        // the first flop bet before anyone else bet (i.e., c-bet).
        // Otherwise returns null.
        // ======================================================
        private static string? TryGetFlopCBetPlayer(IEnumerable<HandAction> actions)
        {
            if (!actions.Any(a => a.Street == Street.Flop))
                return null;

            var aggressor = FlopOperations.CalculateFlopAggressor(actions);
            if (aggressor is null)
                return null;

            foreach (var a in actions.Where(a => a.Street == Street.Flop))
            {
                // Donk bet blocks c-bet
                if (a.Type == ActionType.Bet && a.Player != aggressor)
                    return null;

                // Aggressor first flop action
                if (a.Player == aggressor)
                {
                    return a.Type == ActionType.Bet ? aggressor : null;
                }
            }

            return null;
        }
    }
}
