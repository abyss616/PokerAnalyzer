using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.Helpers;
using PokerAnalyzer.Infrastructure.Helpers;

public sealed partial class HandHistoryIngestService
{
    private static IEnumerable<PlayerProfile> BuildPlayerProfiles(IEnumerable<Hand> hands)
    {
        var profiles = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);

        foreach (var hand in hands)
        {
            var preflopActions = hand.Actions
                .Where(a => a.Street == Street.Preflop)
                .ToList();

            var activePlayers = preflopActions
                .Where(a => a.Type != ActionType.SitOut)
                .Select(a => a.Player)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var player in activePlayers)
            {
                GetOrCreate(profiles, player).Hands++;
            }

            var vpipPlayers = new HashSet<string>(StringComparer.Ordinal);
            var pfrPlayers = new HashSet<string>(StringComparer.Ordinal);
            var threeBetPlayers = new HashSet<string>(StringComparer.Ordinal);
            var facedThreeBetPlayers = new HashSet<string>(StringComparer.Ordinal);
            var foldToThreeBetPlayers = new HashSet<string>(StringComparer.Ordinal);

            string? preflopRaiser = null;
            bool sawRaise = false;
            bool sawThreeBet = false;

            foreach (var action in preflopActions)
            {
                if (action.Type == ActionType.SitOut) continue;

                if (IsVoluntaryPreflopInvestment(action.Type))
                {
                    vpipPlayers.Add(action.Player);
                }

                if (PreFlopOperations.IsPreflopAggressive(action.Type))
                {
                    if (!sawRaise)
                    {
                        sawRaise = true;
                        preflopRaiser = action.Player;
                        pfrPlayers.Add(action.Player);
                    }
                    else if (!sawThreeBet && !string.Equals(action.Player, preflopRaiser, StringComparison.Ordinal))
                    {
                        sawThreeBet = true;
                        threeBetPlayers.Add(action.Player);
                        if (!string.IsNullOrWhiteSpace(preflopRaiser))
                            facedThreeBetPlayers.Add(preflopRaiser);
                    }
                }

                if (sawThreeBet &&
                    action.Type == ActionType.Fold &&
                    !string.IsNullOrWhiteSpace(preflopRaiser) &&
                    string.Equals(action.Player, preflopRaiser, StringComparison.Ordinal))
                {
                    foldToThreeBetPlayers.Add(action.Player);
                }
            }

            var positionAssignments = GetSixMaxPositionAssignments(hand, activePlayers, preflopActions);
            foreach (var assignment in positionAssignments)
            {
                var profile = GetOrCreate(profiles, assignment.Key);
                var positionStats = assignment.Value switch
                {
                    PositionStats.PositionEnum.UTG => profile.PreflopModel.UTGPosition,
                    PositionStats.PositionEnum.HJ => profile.PreflopModel.HJPosition,
                    PositionStats.PositionEnum.CO => profile.PreflopModel.COPosition,
                    PositionStats.PositionEnum.BTN => profile.PreflopModel.BTNPosition,
                    PositionStats.PositionEnum.SB => profile.PreflopModel.SBPosition,
                    PositionStats.PositionEnum.BB => profile.PreflopModel.BBPosition,
                    _ => profile.PreflopModel.UTGPosition
                };

                if (vpipPlayers.Contains(assignment.Key))
                    positionStats.VpipHands++;

                if (pfrPlayers.Contains(assignment.Key))
                    positionStats.PfrHands++;

                if (threeBetPlayers.Contains(assignment.Key))
                    positionStats.ThreeBetHands++;

                if (facedThreeBetPlayers.Contains(assignment.Key))
                    positionStats.FacedThreeBetHands++;

                if (foldToThreeBetPlayers.Contains(assignment.Key))
                    positionStats.FoldToThreeBetHands++;
            }

            var flopActions = hand.Actions
                .Where(a => a.Street == Street.Flop && a.Type != ActionType.SitOut)
                .ToList();

            if (flopActions.Count > 0)
            {
                var flopPlayers = flopActions
                    .Select(a => a.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, flopPlayers, p => p.FlopModel.SawFlop++);

                var showdownPlayers = hand.Showdown
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, showdownPlayers, p => p.FlopModel.WentToShowdown++);

                var winners = hand.Showdown
                    .Where(s => s.Won)
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, winners, p => p.FlopModel.WonAtShowdown++);

                var (cbetOpportunityPlayer, cbetPlayer) = FlopOperations.GetFlopCBetResult(hand.Actions);
                if (!string.IsNullOrWhiteSpace(cbetOpportunityPlayer))
                {
                    IncrementProfiles(profiles, new[] { cbetOpportunityPlayer }, p => p.FlopModel.CBetOpportunities++);
                }

                if (!string.IsNullOrWhiteSpace(cbetPlayer))
                {
                    IncrementProfiles(profiles, new[] { cbetPlayer }, p => p.FlopModel.CBets++);

                    var facedCBetPlayers = new HashSet<string>(StringComparer.Ordinal);
                    var foldedToCBetPlayers = new HashSet<string>(StringComparer.Ordinal);
                    bool afterCBet = false;

                    foreach (var action in flopActions)
                    {
                        if (!afterCBet)
                        {
                            if (action.Type == ActionType.Bet && string.Equals(action.Player, cbetPlayer, StringComparison.Ordinal))
                                afterCBet = true;

                            continue;
                        }

                        if (string.Equals(action.Player, cbetPlayer, StringComparison.Ordinal))
                            continue;

                        if (string.IsNullOrWhiteSpace(action.Player))
                            continue;

                        facedCBetPlayers.Add(action.Player);

                        if (action.Type == ActionType.Fold)
                            foldedToCBetPlayers.Add(action.Player);
                    }

                    IncrementProfiles(profiles, facedCBetPlayers, p => p.FlopModel.FoldToCBetOpportunities++);
                    IncrementProfiles(profiles, foldedToCBetPlayers, p => p.FlopModel.FoldToCBet++);
                }

                var donkBet = FlopOperations.GetFlopDonkBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(donkBet.DonkBettor))
                {
                    IncrementProfiles(profiles, new[] { donkBet.DonkBettor }, p => p.FlopModel.DonkBets++);
                }

                var firstFoldToCBet = FlopOperations.GetFirstFoldToFlopCBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(firstFoldToCBet.Folder))
                {
                    IncrementProfiles(profiles, new[] { firstFoldToCBet.Folder }, p => p.FlopModel.FirstFoldToCBet++);
                }

                var firstCallVsCBet = FlopTier2Operations.GetFirstCallVsFlopCBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(firstCallVsCBet.Caller))
                {
                    IncrementProfiles(profiles, new[] { firstCallVsCBet.Caller }, p => p.FlopModel.CallVsCBet++);
                }

                var firstRaiseVsCBet = FlopTier2Operations.GetFirstRaiseVsFlopCBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(firstRaiseVsCBet.Raiser))
                {
                    IncrementProfiles(profiles, new[] { firstRaiseVsCBet.Raiser }, p => p.FlopModel.RaiseVsCBet++);
                }

                var multiwayCBetPlayer = FlopTier2Operations.GetMultiwayFlopCBetPlayer(hand.Actions);
                if (!string.IsNullOrWhiteSpace(multiwayCBetPlayer))
                {
                    IncrementProfiles(profiles, new[] { multiwayCBetPlayer }, p => p.FlopModel.MultiwayCBets++);
                }

                var probeBet = FlopTier3Operations.GetFlopProbeBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(probeBet.ProbeBettor))
                {
                    IncrementProfiles(profiles, new[] { probeBet.ProbeBettor }, p => p.FlopModel.ProbeBets++);
                }
            }

            var turnActions = hand.Actions
                .Where(a => a.Street == Street.Turn && a.Type != ActionType.SitOut)
                .ToList();

            if (turnActions.Count > 0)
            {
                var turnPlayers = turnActions
                    .Select(a => a.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, turnPlayers, p => p.TurnModel.SawTurn++);

                var showdownPlayers = hand.Showdown
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, showdownPlayers, p => p.TurnModel.WentToShowdown++);

                var winners = hand.Showdown
                    .Where(s => s.Won)
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, winners, p => p.TurnModel.WonAtShowdown++);

                var flopCBetResult = FlopOperations.GetFlopCBetResult(hand.Actions);
                if (!string.IsNullOrWhiteSpace(flopCBetResult.CBetPlayer))
                {
                    var flopCBetPlayer = flopCBetResult.CBetPlayer;
                    var firstTurnBet = turnActions.FirstOrDefault(a =>
                        string.Equals(a.Player, flopCBetPlayer, StringComparison.Ordinal));

                    if (firstTurnBet is not null)
                    {
                        var betBefore = turnActions
                            .TakeWhile(a => !string.Equals(a.Player, flopCBetPlayer, StringComparison.Ordinal))
                            .Any(a => IsAggressivePostflopAction(a.Type));

                        if (!betBefore && IsAggressivePostflopAction(firstTurnBet.Type))
                        {
                            IncrementProfiles(profiles, new[] { flopCBetPlayer }, p => p.TurnModel.TurnCBet++);
                        }
                    }
                }

                var turnAggressionByPlayer = new Dictionary<string, (int BetsRaises, int Calls)>(StringComparer.Ordinal);
                var turnBetSizeTotals = new Dictionary<string, (decimal TotalPercent, int Count)>(StringComparer.Ordinal);
                var betSeen = false;

                foreach (var action in turnActions)
                {
                    if (string.IsNullOrWhiteSpace(action.Player))
                        continue;

                    if (action.Type == ActionType.Check && !betSeen)
                    {
                        IncrementProfiles(profiles, new[] { action.Player }, p => p.TurnModel.TurnCheck++);
                    }

                    if (action.Type == ActionType.Fold && betSeen)
                    {
                        IncrementProfiles(profiles, new[] { action.Player }, p => p.TurnModel.TurnFoldToBet++);
                    }

                    if (IsAggressivePostflopAction(action.Type))
                    {
                        var current = turnAggressionByPlayer.TryGetValue(action.Player, out var value)
                            ? value
                            : (0, 0);
                        turnAggressionByPlayer[action.Player] = (current.Item1 + 1, current.Item2);

                        if (betSeen)
                        {
                            IncrementProfiles(profiles, new[] { action.Player }, p => p.TurnModel.TurnRaiseVsBet++);
                        }

                        if (action.Amount.HasValue && hand.Pot.HasValue && hand.Pot.Value > 0m)
                        {
                            var percent = action.Amount.Value / hand.Pot.Value * 100m;
                            var betTotals = turnBetSizeTotals.TryGetValue(action.Player, out var totals)
                                ? totals
                                : (0m, 0);
                            turnBetSizeTotals[action.Player] = (betTotals.Item1 + percent, betTotals.Item2 + 1);
                        }

                        betSeen = true;
                    }
                    else if (action.Type == ActionType.Call)
                    {
                        var current = turnAggressionByPlayer.TryGetValue(action.Player, out var value)
                            ? value
                            : (0, 0);
                        turnAggressionByPlayer[action.Player] = (current.Item1, current.Item2 + 1);
                    }
                }

                foreach (var playerAggression in turnAggressionByPlayer)
                {
                    if (playerAggression.Value.BetsRaises == 0 && playerAggression.Value.Calls == 0)
                        continue;

                    var factor = playerAggression.Value.Calls == 0
                        ? playerAggression.Value.BetsRaises
                        : (decimal)playerAggression.Value.BetsRaises / playerAggression.Value.Calls;

                    IncrementProfiles(profiles, new[] { playerAggression.Key }, p => p.TurnModel.TurnAggressionFactor += factor);
                }

                foreach (var playerBetSize in turnBetSizeTotals)
                {
                    var averagePercent = playerBetSize.Value.TotalPercent / playerBetSize.Value.Count;
                    IncrementProfiles(profiles, new[] { playerBetSize.Key }, p => p.TurnModel.TurnBetSizePercentPot += averagePercent);
                }

                var wtsdCarryoverPlayers = turnPlayers
                    .Where(p => showdownPlayers.Contains(p, StringComparer.Ordinal))
                    .ToList();

                IncrementProfiles(profiles, wtsdCarryoverPlayers, p => p.TurnModel.TurnWTSDCarryover++);
            }

            var riverActions = hand.Actions
                .Where(a => a.Street == Street.River && a.Type != ActionType.SitOut)
                .ToList();

            if (riverActions.Count > 0)
            {
                var riverAggressionByPlayer = new Dictionary<string, (int BetsRaises, int Calls)>(StringComparer.Ordinal);
                var riverBetSizeTotals = new Dictionary<string, (decimal TotalPercent, int Count)>(StringComparer.Ordinal);
                var betSeen = false;

                foreach (var action in riverActions)
                {
                    if (string.IsNullOrWhiteSpace(action.Player))
                        continue;

                    if (!betSeen && (action.Type == ActionType.Check || IsAggressivePostflopAction(action.Type)))
                    {
                        IncrementProfiles(profiles, new[] { action.Player }, p => p.RiverModel.RiverBetOpportunities++);

                        if (IsAggressivePostflopAction(action.Type))
                        {
                            IncrementProfiles(profiles, new[] { action.Player }, p => p.RiverModel.RiverBetsWhenCheckedTo++);
                        }
                    }

                    if (betSeen && (action.Type == ActionType.Call || action.Type == ActionType.Fold || IsAggressivePostflopAction(action.Type)))
                    {
                        IncrementProfiles(profiles, new[] { action.Player }, p => p.RiverModel.RiverFacedBet++);

                        if (action.Type == ActionType.Call)
                        {
                            IncrementProfiles(profiles, new[] { action.Player }, p => p.RiverModel.RiverCallsVsBet++);
                        }

                        if (action.Type == ActionType.Fold)
                        {
                            IncrementProfiles(profiles, new[] { action.Player }, p => p.RiverModel.RiverFoldToBet++);
                        }

                        if (IsAggressivePostflopAction(action.Type))
                        {
                            IncrementProfiles(profiles, new[] { action.Player }, p => p.RiverModel.RiverRaiseVsBet++);
                        }
                    }

                    if (IsAggressivePostflopAction(action.Type))
                    {
                        var current = riverAggressionByPlayer.TryGetValue(action.Player, out var value)
                            ? value
                            : (0, 0);
                        riverAggressionByPlayer[action.Player] = (current.Item1 + 1, current.Item2);

                        if (action.Amount.HasValue && hand.Pot.HasValue && hand.Pot.Value > 0m)
                        {
                            var percent = action.Amount.Value / hand.Pot.Value * 100m;
                            var betTotals = riverBetSizeTotals.TryGetValue(action.Player, out var totals)
                                ? totals
                                : (0m, 0);
                            riverBetSizeTotals[action.Player] = (betTotals.Item1 + percent, betTotals.Item2 + 1);
                        }

                        betSeen = true;
                    }
                    else if (action.Type == ActionType.Call)
                    {
                        var current = riverAggressionByPlayer.TryGetValue(action.Player, out var value)
                            ? value
                            : (0, 0);
                        riverAggressionByPlayer[action.Player] = (current.Item1, current.Item2 + 1);
                    }
                }

                var riverPlayers = riverActions
                    .Select(a => a.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, riverPlayers, p => p.RiverModel.SawRiver++);

                foreach (var playerAggression in riverAggressionByPlayer)
                {
                    if (playerAggression.Value.BetsRaises == 0 && playerAggression.Value.Calls == 0)
                        continue;

                    var factor = playerAggression.Value.Calls == 0
                        ? playerAggression.Value.BetsRaises
                        : (decimal)playerAggression.Value.BetsRaises / playerAggression.Value.Calls;

                    IncrementProfiles(profiles, new[] { playerAggression.Key }, p => p.RiverModel.RiverAggressionFactor += factor);
                }

                foreach (var playerBetSize in riverBetSizeTotals)
                {
                    var averagePercent = playerBetSize.Value.TotalPercent / playerBetSize.Value.Count;
                    IncrementProfiles(profiles, new[] { playerBetSize.Key }, p => p.RiverModel.RiverBetSizePercentPot += averagePercent);
                }

                var showdownPlayers = hand.Showdown
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, showdownPlayers, p => p.RiverModel.WentToShowdown++);

                var winners = hand.Showdown
                    .Where(s => s.Won)
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementProfiles(profiles, winners, p => p.RiverModel.WonAtShowdown++);
            }
        }

        return profiles.Values;
    }

    private static PlayerProfile GetOrCreate(Dictionary<string, PlayerProfile> profiles, string player)
    {
        if (profiles.TryGetValue(player, out var existing)) return existing;
        var created = new PlayerProfile { Player = player };
        profiles[player] = created;
        return created;
    }

    private static void IncrementProfiles(
        Dictionary<string, PlayerProfile> profiles,
        IEnumerable<string> players,
        Action<PlayerProfile> increment)
    {
        foreach (var player in players)
        {
            increment(GetOrCreate(profiles, player));
        }
    }

    private static Dictionary<string, PositionStats.PositionEnum> GetSixMaxPositionAssignments(
        Hand hand,
        IReadOnlyCollection<string> activePlayers,
        IReadOnlyCollection<HandAction> preflopActions)
    {
        if (hand.Players.Count == 0 || activePlayers.Count == 0)
            return new Dictionary<string, PositionStats.PositionEnum>(StringComparer.Ordinal);

        var activeSeats = hand.Players
            .Where(p => activePlayers.Contains(p.Name))
            .Where(p => p.Seat > 0)
            .OrderBy(p => p.Seat)
            .ToList();

        if (activeSeats.Count != 6)
            return new Dictionary<string, PositionStats.PositionEnum>(StringComparer.Ordinal);

        var sbPlayer = preflopActions
            .FirstOrDefault(a => a.Type == ActionType.PostSmallBlind)?.Player;

        if (string.IsNullOrWhiteSpace(sbPlayer))
            return new Dictionary<string, PositionStats.PositionEnum>(StringComparer.Ordinal);

        var sbIndex = activeSeats.FindIndex(p => string.Equals(p.Name, sbPlayer, StringComparison.Ordinal));
        if (sbIndex < 0)
            return new Dictionary<string, PositionStats.PositionEnum>(StringComparer.Ordinal);

        var buttonIndex = (sbIndex - 1 + activeSeats.Count) % activeSeats.Count;
        var orderedSeats = activeSeats
            .Skip(buttonIndex)
            .Concat(activeSeats.Take(buttonIndex))
            .ToList();

        var positions = new[]
        {
            PositionStats.PositionEnum.BTN,
            PositionStats.PositionEnum.SB,
            PositionStats.PositionEnum.BB,
            PositionStats.PositionEnum.UTG,
            PositionStats.PositionEnum.HJ,
            PositionStats.PositionEnum.CO
        };

        var assignments = new Dictionary<string, PositionStats.PositionEnum>(StringComparer.Ordinal);
        for (var i = 0; i < positions.Length && i < orderedSeats.Count; i++)
        {
            assignments[orderedSeats[i].Name] = positions[i];
        }

        return assignments;
    }
}
