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

            if (TryGetBbFoldVsLateOpen(preflopActions, positionAssignments, out var bbPlayer, out var bbFolded))
            {
                var profile = GetOrCreate(profiles, bbPlayer);
                var bbStats = GetPositionPreflopStats(profile.PreflopModel, PositionStats.PositionEnum.BB);
                bbStats.FacedLateOpenHands++;
                if (bbFolded)
                    bbStats.FoldedVsLateOpenHands++;
            }

            foreach (var assignment in positionAssignments)
            {
                var profile = GetOrCreate(profiles, assignment.Key);
                var positionStats = GetPositionPreflopStats(profile.PreflopModel, assignment.Value);

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

                IncrementFlopProfilesByPosition(profiles, positionAssignments, flopPlayers, p => p.SawFlop++);

                var showdownPlayers = hand.Showdown
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementFlopProfilesByPosition(profiles, positionAssignments, showdownPlayers, p => p.WentToShowdown++);

                var winners = hand.Showdown
                    .Where(s => s.Won)
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementFlopProfilesByPosition(profiles, positionAssignments, winners, p => p.WonAtShowdown++);

                var (cbetOpportunityPlayer, cbetPlayer) = FlopOperations.GetFlopCBetResult(hand.Actions);
                if (!string.IsNullOrWhiteSpace(cbetOpportunityPlayer))
                {
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, new[] { cbetOpportunityPlayer }, p => p.CBetOpportunities++);
                }

                if (!string.IsNullOrWhiteSpace(cbetPlayer))
                {
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, new[] { cbetPlayer }, p => p.CBets++);

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

                    IncrementFlopProfilesByPosition(profiles, positionAssignments, facedCBetPlayers, p => p.FoldToCBetOpportunities++);
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, foldedToCBetPlayers, p => p.FoldToCBet++);
                }

                var donkBet = FlopOperations.GetFlopDonkBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(donkBet.DonkBettor))
                {
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, new[] { donkBet.DonkBettor }, p => p.DonkBets++);
                }

                var firstFoldToCBet = FlopOperations.GetFirstFoldToFlopCBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(firstFoldToCBet.Folder))
                {
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, new[] { firstFoldToCBet.Folder }, p => p.FirstFoldToCBet++);
                }

                var firstCallVsCBet = FlopTier2Operations.GetFirstCallVsFlopCBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(firstCallVsCBet.Caller))
                {
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, new[] { firstCallVsCBet.Caller }, p => p.CallVsCBet++);
                }

                var firstRaiseVsCBet = FlopTier2Operations.GetFirstRaiseVsFlopCBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(firstRaiseVsCBet.Raiser))
                {
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, new[] { firstRaiseVsCBet.Raiser }, p => p.RaiseVsCBet++);
                }

                var multiwayCBetPlayer = FlopTier2Operations.GetMultiwayFlopCBetPlayer(hand.Actions);
                if (!string.IsNullOrWhiteSpace(multiwayCBetPlayer))
                {
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, new[] { multiwayCBetPlayer }, p => p.MultiwayCBets++);
                }

                var probeBet = FlopTier3Operations.GetFlopProbeBet(hand.Actions);
                if (!string.IsNullOrWhiteSpace(probeBet.ProbeBettor))
                {
                    IncrementFlopProfilesByPosition(profiles, positionAssignments, new[] { probeBet.ProbeBettor }, p => p.ProbeBets++);
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

                IncrementTurnProfilesByPosition(profiles, positionAssignments, turnPlayers, p => p.SawTurn++);

                var showdownPlayers = hand.Showdown
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementTurnProfilesByPosition(profiles, positionAssignments, showdownPlayers, p => p.WentToShowdown++);

                var winners = hand.Showdown
                    .Where(s => s.Won)
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementTurnProfilesByPosition(profiles, positionAssignments, winners, p => p.WonAtShowdown++);

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
                            IncrementTurnProfilesByPosition(profiles, positionAssignments, new[] { flopCBetPlayer }, p => p.TurnCBet++);
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
                        IncrementTurnProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.TurnCheck++);
                    }

                    if (action.Type == ActionType.Fold && betSeen)
                    {
                        IncrementTurnProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.TurnFoldToBet++);
                    }

                    if (IsAggressivePostflopAction(action.Type))
                    {
                        var current = turnAggressionByPlayer.TryGetValue(action.Player, out var value)
                            ? value
                            : (0, 0);
                        turnAggressionByPlayer[action.Player] = (current.Item1 + 1, current.Item2);

                        if (betSeen)
                        {
                            IncrementTurnProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.TurnRaiseVsBet++);
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

                    IncrementTurnProfilesByPosition(profiles, positionAssignments, new[] { playerAggression.Key }, p => p.TurnAggressionFactor += factor);
                }

                foreach (var playerBetSize in turnBetSizeTotals)
                {
                    var averagePercent = playerBetSize.Value.TotalPercent / playerBetSize.Value.Count;
                    IncrementTurnProfilesByPosition(profiles, positionAssignments, new[] { playerBetSize.Key }, p => p.TurnBetSizePercentPot += averagePercent);
                }

                var wtsdCarryoverPlayers = turnPlayers
                    .Where(p => showdownPlayers.Contains(p, StringComparer.Ordinal))
                    .ToList();

                IncrementTurnProfilesByPosition(profiles, positionAssignments, wtsdCarryoverPlayers, p => p.TurnWTSDCarryover++);
            }

            var riverActions = hand.Actions
                .Where(a => a.Street == Street.River && a.Type != ActionType.SitOut)
                .ToList();

            if (riverActions.Count > 0)
            {
                var riverAggressionByPlayer = new Dictionary<string, (int BetsRaises, int Calls)>(StringComparer.Ordinal);
                var riverBetSizeTotals = new Dictionary<string, (decimal TotalPercent, int Count)>(StringComparer.Ordinal);
                var betSeen = false;
                var checkSeen = false;

                foreach (var action in riverActions)
                {
                    if (string.IsNullOrWhiteSpace(action.Player))
                        continue;

                    if (!betSeen && (action.Type == ActionType.Check || IsAggressivePostflopAction(action.Type)))
                    {
                        if (checkSeen)
                        {
                            IncrementRiverProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.RiverBetOpportunities++);
                        }

                        if (checkSeen && IsAggressivePostflopAction(action.Type))
                        {
                            IncrementRiverProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.RiverBetsWhenCheckedTo++);
                        }
                    }

                    if (betSeen && (action.Type == ActionType.Call || action.Type == ActionType.Fold || IsAggressivePostflopAction(action.Type)))
                    {
                        IncrementRiverProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.RiverFacedBet++);

                        if (action.Type == ActionType.Call)
                        {
                            IncrementRiverProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.RiverCallsVsBet++);
                        }

                        if (action.Type == ActionType.Fold)
                        {
                            IncrementRiverProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.RiverFoldToBet++);
                        }

                        if (IsAggressivePostflopAction(action.Type))
                        {
                            IncrementRiverProfilesByPosition(profiles, positionAssignments, new[] { action.Player }, p => p.RiverRaiseVsBet++);
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

                    if (!betSeen && action.Type == ActionType.Check)
                    {
                        checkSeen = true;
                    }
                }

                var riverPlayers = riverActions
                    .Select(a => a.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementRiverProfilesByPosition(profiles, positionAssignments, riverPlayers, p => p.SawRiver++);

                foreach (var playerAggression in riverAggressionByPlayer)
                {
                    if (playerAggression.Value.BetsRaises == 0 && playerAggression.Value.Calls == 0)
                        continue;

                    var factor = playerAggression.Value.Calls == 0
                        ? playerAggression.Value.BetsRaises
                        : (decimal)playerAggression.Value.BetsRaises / playerAggression.Value.Calls;

                    IncrementRiverProfilesByPosition(profiles, positionAssignments, new[] { playerAggression.Key }, p => p.RiverAggressionFactor += factor);
                }

                foreach (var playerBetSize in riverBetSizeTotals)
                {
                    var averagePercent = playerBetSize.Value.TotalPercent / playerBetSize.Value.Count;
                    IncrementRiverProfilesByPosition(profiles, positionAssignments, new[] { playerBetSize.Key }, p => p.RiverBetSizePercentPot += averagePercent);
                }

                var showdownPlayers = hand.Showdown
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementRiverProfilesByPosition(profiles, positionAssignments, showdownPlayers, p => p.WentToShowdown++);

                var winners = hand.Showdown
                    .Where(s => s.Won)
                    .Select(s => s.Player)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                IncrementRiverProfilesByPosition(profiles, positionAssignments, winners, p => p.WonAtShowdown++);
            }
        }

        return profiles.Values.Where(HasAnyStats);
    }

    private static bool HasAnyStats(PlayerProfile profile)
    {
        return 
             HasPreflopStats(profile.PreflopModel)
            || HasFlopStats(profile.FlopModel)
            || HasTurnStats(profile.TurnModel)
            || HasRiverStats(profile.RiverModel);
    }

    private static bool HasPreflopStats(PreflopStats stats)
    {
        var positions = stats.Positions;
        return HasPreflopPositionStats(positions.Utg)
            || HasPreflopPositionStats(positions.Hj)
            || HasPreflopPositionStats(positions.Co)
            || HasPreflopPositionStats(positions.Btn)
            || HasPreflopPositionStats(positions.Sb)
            || HasPreflopPositionStats(positions.Bb);
    }

    private static bool HasPreflopPositionStats(PositionPreflopStats stats)
    {
        return stats.VpipHands > 0
            || stats.PfrHands > 0
            || stats.ThreeBetHands > 0
            || stats.FacedThreeBetHands > 0
            || stats.FoldToThreeBetHands > 0
            || stats.FacedLateOpenHands > 0
            || stats.FoldedVsLateOpenHands > 0;
    }

    private static bool HasFlopStats(FlopStatsByPosition stats)
    {
        var positions = stats.Positions;
        return HasFlopPositionStats(positions.Utg)
            || HasFlopPositionStats(positions.Hj)
            || HasFlopPositionStats(positions.Co)
            || HasFlopPositionStats(positions.Btn)
            || HasFlopPositionStats(positions.Sb)
            || HasFlopPositionStats(positions.Bb);
    }

    private static bool HasFlopPositionStats(FlopStats stats)
    {
        return stats.SawFlop > 0
            || stats.WentToShowdown > 0
            || stats.WonAtShowdown > 0
            || stats.CBetOpportunities > 0
            || stats.CBets > 0
            || stats.FoldToCBetOpportunities > 0
            || stats.FoldToCBet > 0
            || stats.DonkBets > 0
            || stats.FirstFoldToCBet > 0
            || stats.CallVsCBet > 0
            || stats.RaiseVsCBet > 0
            || stats.MultiwayCBets > 0
            || stats.ProbeBets > 0;
    }

    private static bool HasTurnStats(TurnStatsByPosition stats)
    {
        var positions = stats.Positions;
        return HasTurnPositionStats(positions.Utg)
            || HasTurnPositionStats(positions.Hj)
            || HasTurnPositionStats(positions.Co)
            || HasTurnPositionStats(positions.Btn)
            || HasTurnPositionStats(positions.Sb)
            || HasTurnPositionStats(positions.Bb);
    }

    private static bool HasTurnPositionStats(TurnStats stats)
    {
        return stats.SawTurn > 0
            || stats.WentToShowdown > 0
            || stats.WonAtShowdown > 0
            || stats.TurnCBet > 0
            || stats.TurnCheck > 0
            || stats.TurnFoldToBet > 0
            || stats.TurnAggressionFactor != 0m
            || stats.TurnBetSizePercentPot != 0m
            || stats.TurnRaiseVsBet > 0
            || stats.TurnWTSDCarryover > 0;
    }

    private static bool HasRiverStats(RiverStatsByPosition stats)
    {
        var positions = stats.Positions;
        return HasRiverPositionStats(positions.Utg)
            || HasRiverPositionStats(positions.Hj)
            || HasRiverPositionStats(positions.Co)
            || HasRiverPositionStats(positions.Btn)
            || HasRiverPositionStats(positions.Sb)
            || HasRiverPositionStats(positions.Bb);
    }

    private static bool HasRiverPositionStats(RiverStats stats)
    {
        return stats.SawRiver > 0
            || stats.WentToShowdown > 0
            || stats.WonAtShowdown > 0
            || stats.RiverBetOpportunities > 0
            || stats.RiverBetsWhenCheckedTo > 0
            || stats.RiverFacedBet > 0
            || stats.RiverCallsVsBet > 0
            || stats.RiverFoldToBet > 0
            || stats.RiverRaiseVsBet > 0
            || stats.RiverAggressionFactor != 0m
            || stats.RiverBetSizePercentPot != 0m;
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

    private static PositionPreflopStats GetPositionPreflopStats(
        PreflopStats preflopStats,
        PositionStats.PositionEnum position)
    {
        return position switch
        {
            PositionStats.PositionEnum.UTG => preflopStats.Positions.Utg,
            PositionStats.PositionEnum.HJ => preflopStats.Positions.Hj,
            PositionStats.PositionEnum.CO => preflopStats.Positions.Co,
            PositionStats.PositionEnum.BTN => preflopStats.Positions.Btn,
            PositionStats.PositionEnum.SB => preflopStats.Positions.Sb,
            PositionStats.PositionEnum.BB => preflopStats.Positions.Bb,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unsupported position.")
        };
    }

    private static FlopStats GetPositionFlopStats(
        FlopStatsByPosition flopStats,
        PositionStats.PositionEnum position)
    {
        return position switch
        {
            PositionStats.PositionEnum.UTG => flopStats.Positions.Utg,
            PositionStats.PositionEnum.HJ => flopStats.Positions.Hj,
            PositionStats.PositionEnum.CO => flopStats.Positions.Co,
            PositionStats.PositionEnum.BTN => flopStats.Positions.Btn,
            PositionStats.PositionEnum.SB => flopStats.Positions.Sb,
            PositionStats.PositionEnum.BB => flopStats.Positions.Bb,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unsupported position.")
        };
    }

    private static TurnStats GetPositionTurnStats(
        TurnStatsByPosition turnStats,
        PositionStats.PositionEnum position)
    {
        return position switch
        {
            PositionStats.PositionEnum.UTG => turnStats.Positions.Utg,
            PositionStats.PositionEnum.HJ => turnStats.Positions.Hj,
            PositionStats.PositionEnum.CO => turnStats.Positions.Co,
            PositionStats.PositionEnum.BTN => turnStats.Positions.Btn,
            PositionStats.PositionEnum.SB => turnStats.Positions.Sb,
            PositionStats.PositionEnum.BB => turnStats.Positions.Bb,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unsupported position.")
        };
    }

    private static RiverStats GetPositionRiverStats(
        RiverStatsByPosition riverStats,
        PositionStats.PositionEnum position)
    {
        return position switch
        {
            PositionStats.PositionEnum.UTG => riverStats.Positions.Utg,
            PositionStats.PositionEnum.HJ => riverStats.Positions.Hj,
            PositionStats.PositionEnum.CO => riverStats.Positions.Co,
            PositionStats.PositionEnum.BTN => riverStats.Positions.Btn,
            PositionStats.PositionEnum.SB => riverStats.Positions.Sb,
            PositionStats.PositionEnum.BB => riverStats.Positions.Bb,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unsupported position.")
        };
    }

    private static void IncrementFlopProfilesByPosition(
        Dictionary<string, PlayerProfile> profiles,
        Dictionary<string, PositionStats.PositionEnum> positionAssignments,
        IEnumerable<string> players,
        Action<FlopStats> incrementAction)
    {
        foreach (var player in players)
        {
            if (!positionAssignments.TryGetValue(player, out var position))
                continue;

            var profile = GetOrCreate(profiles, player);
            var positionStats = GetPositionFlopStats(profile.FlopModel, position);
            incrementAction(positionStats);
        }
    }

    private static void IncrementTurnProfilesByPosition(
        Dictionary<string, PlayerProfile> profiles,
        Dictionary<string, PositionStats.PositionEnum> positionAssignments,
        IEnumerable<string> players,
        Action<TurnStats> incrementAction)
    {
        foreach (var player in players)
        {
            if (!positionAssignments.TryGetValue(player, out var position))
                continue;

            var profile = GetOrCreate(profiles, player);
            var positionStats = GetPositionTurnStats(profile.TurnModel, position);
            incrementAction(positionStats);
        }
    }

    private static void IncrementRiverProfilesByPosition(
        Dictionary<string, PlayerProfile> profiles,
        Dictionary<string, PositionStats.PositionEnum> positionAssignments,
        IEnumerable<string> players,
        Action<RiverStats> incrementAction)
    {
        foreach (var player in players)
        {
            if (!positionAssignments.TryGetValue(player, out var position))
                continue;

            var profile = GetOrCreate(profiles, player);
            var positionStats = GetPositionRiverStats(profile.RiverModel, position);
            incrementAction(positionStats);
        }
    }

    private static PositionStats GetOrCreatePositionStats(PlayerProfile profile, PositionStats.PositionEnum position)
    {
        var existing = profile.ByPosition.FirstOrDefault(p => p.Position == position);
        if (existing != null)
            return existing;

        var created = new PositionStats
        {
            Position = position,
            PlayerProfile = profile
        };

        profile.ByPosition.Add(created);
        return created;
    }

    private static bool TryGetBbFoldVsLateOpen(
        IReadOnlyList<HandAction> preflopActions,
        Dictionary<string, PositionStats.PositionEnum> positionAssignments,
        out string bbPlayer,
        out bool bbFolded)
    {
        bbPlayer = string.Empty;
        bbFolded = false;

        if (positionAssignments.Count == 0)
            return false;

        var bbEntry = positionAssignments.FirstOrDefault(p => p.Value == PositionStats.PositionEnum.BB);
        if (string.IsNullOrWhiteSpace(bbEntry.Key))
            return false;

        var openActionIndex = -1;
        HandAction? openAction = null;
        for (var i = 0; i < preflopActions.Count; i++)
        {
            var action = preflopActions[i];
            if (action.Type == ActionType.SitOut)
                continue;

            if (PreFlopOperations.IsPreflopAggressive(action.Type))
            {
                openActionIndex = i;
                openAction = action;
                break;
            }
        }

        if (openAction == null)
            return false;

        if (!positionAssignments.TryGetValue(openAction.Player, out var openerPosition))
            return false;

        if (openerPosition is not (PositionStats.PositionEnum.CO or PositionStats.PositionEnum.BTN or PositionStats.PositionEnum.SB))
            return false;

        for (var i = openActionIndex + 1; i < preflopActions.Count; i++)
        {
            var action = preflopActions[i];
            if (!string.Equals(action.Player, bbEntry.Key, StringComparison.Ordinal))
                continue;

            if (action.Type == ActionType.PostBigBlind || action.Type == ActionType.SitOut)
                continue;

            bbPlayer = bbEntry.Key;
            bbFolded = action.Type == ActionType.Fold;
            return true;
        }

        return false;
    }
}
