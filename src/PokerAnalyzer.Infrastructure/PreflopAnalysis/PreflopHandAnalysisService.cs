using System.Globalization;
using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.HandHistories;

namespace PokerAnalyzer.Infrastructure.PreflopAnalysis;

public sealed class PreflopHandAnalysisService : IPreflopHandAnalysisService
{
    private const string NotYetImplemented = PreflopHandAnalysisResultDto.NotYetImplemented;

    private readonly IHandHistoryRepository _hands;
    private readonly PreflopStateExtractor _extractor;
    private readonly IPreflopStrategyProvider _strategyProvider;

    public PreflopHandAnalysisService(
        IHandHistoryRepository hands,
        PreflopStateExtractor extractor,
        IPreflopStrategyProvider strategyProvider)
    {
        _hands = hands;
        _extractor = extractor;
        _strategyProvider = strategyProvider;
    }

    public async Task<PreflopHandAnalysisResultDto?> AnalyzePreflopByHandNumberAsync(long handNumber, CancellationToken ct)
    {
        var result = await QueryPreflopNodeByHandNumberAsync(handNumber, ct);
        if (result is null)
            return null;

        return new PreflopHandAnalysisResultDto(
            handNumber.ToString(CultureInfo.InvariantCulture),
            result.CanonicalKey,
            result.LegalActions.Count == 0 ? NotYetImplemented : string.Join(", ", result.LegalActions.Select(x => x.ActionKey)),
            result.Strategy.Count == 0 ? NotYetImplemented : string.Join(", ", result.Strategy.Select(x => $"{x.ActionKey} {x.Frequency:0.##}%")),
            result.IsSupported ? "See structured solver node details." : result.UnsupportedReason ?? NotYetImplemented);
    }

    public async Task<PreflopNodeQueryResultDto?> QueryPreflopNodeByHandNumberAsync(long handNumber, CancellationToken ct)
    {
        var hand = await _hands.GetHandByGameCodeAsync(handNumber, ct);
        if (hand is null)
            return null;

        var request = BuildRequestFromHand(hand);
        if (request is null)
            return BuildUnsupported("Could not construct preflop query from hand history.");

        return await QueryPreflopNodeAsync(request, ct);
    }

    public async Task<PreflopNodeQueryResultDto> QueryPreflopNodeAsync(PreflopNodeQueryRequestDto request, CancellationToken ct)
    {
        if (request.Street != Street.Preflop)
            return BuildUnsupported($"Street '{request.Street}' is unsupported. Only preflop is currently queryable.");

        var seatById = request.Seats.ToDictionary(x => new PlayerId(x.PlayerId));
        var actingPlayerId = new PlayerId(request.ActingPlayerId);
        if (!seatById.ContainsKey(actingPlayerId))
            return BuildUnsupported("Acting player was not found in seat map.");

        var seats = request.Seats
            .OrderBy(s => s.Seat)
            .Select(s => new PlayerSeat(
                new PlayerId(s.PlayerId),
                s.Name,
                s.Seat,
                s.Position,
                new ChipAmount((long)Math.Round(s.StartingStackBb * 100m, MidpointRounding.AwayFromZero))))
            .ToList();

        var actions = request.PublicActionHistory
            .Select(a => new PreflopInputAction(new PlayerId(a.PlayerId), a.ActionType, a.AmountBb))
            .ToList();

        var extractorSmallBlind = BbToSolverChips(request.SmallBlind / request.BigBlind);
        var extractorBigBlind = BbToSolverChips(1m);
        var extraction = _extractor.TryExtract(seats, actions, actingPlayerId, extractorSmallBlind, extractorBigBlind);
        if (!extraction.IsSupported || extraction.Key is null)
            return BuildUnsupported(extraction.UnsupportedReason ?? "Preflop extraction was unsupported.", extraction.Trace);

        var legalActions = BuildLegalActions(request, extraction.Trace);
        var strategy = await ResolveStrategyAsync(extraction.Key.SolverKey, legalActions, ct);

        var canonicalKey = BuildCanonicalKey(extraction.Key, request.HeroHoleCards);
        return new PreflopNodeQueryResultDto(
            true,
            null,
            canonicalKey,
            extraction.Key.SolverKey,
            Street.Preflop,
            extraction.Key.ActingPosition,
            extraction.Key.FacingPosition,
            extraction.Key.HistorySignature,
            extraction.Trace.PotBb,
            extraction.Trace.ToCallBb,
            extraction.Trace.EffectiveStackBb,
            extraction.Key.RaiseDepth,
            BuildSizingSummary(extraction.Trace),
            legalActions,
            strategy,
            ToTraceDto(extraction.Trace));
    }

    private async Task<IReadOnlyList<PreflopNodeStrategyItemDto>> ResolveStrategyAsync(
        string solverKey,
        IReadOnlyList<PreflopNodeLegalActionDto> legalActions,
        CancellationToken ct)
    {
        var actionKeys = legalActions.Select(x => x.ActionKey).ToList();
        var strategyResult = await _strategyProvider.GetStrategyResultAsync(solverKey, actionKeys, ct);
        if (strategyResult?.AverageStrategy is not { Count: > 0 } strategy)
            return Array.Empty<PreflopNodeStrategyItemDto>();

        return legalActions
            .Where(x => strategy.ContainsKey(x.ActionKey))
            .Select(x => new PreflopNodeStrategyItemDto(x.ActionKey, decimal.Round(strategy[x.ActionKey] * 100m, 2)))
            .OrderByDescending(x => x.Frequency)
            .ToList();
    }

    private static string BuildCanonicalKey(PreflopInfoSetKey key, string? heroHoleCards)
    {
        if (!string.IsNullOrWhiteSpace(heroHoleCards))
        {
            try
            {
                var cards = HoleCards.Parse(heroHoleCards);
                return SolverInfoSetKey.CreatePreflop(key, cards).CanonicalKey;
            }
            catch
            {
                // keep key without private cards when hero cards cannot be parsed.
            }
        }

        return $"preflop/{key.SolverKey}";
    }

    private static PreflopNodeTraceDto ToTraceDto(PreflopQueryTrace trace)
        => new(
            trace.SolverKey,
            trace.HistorySignature,
            trace.RaiseDepth,
            trace.ToCallBb,
            trace.CurrentBetBb,
            trace.PotBb,
            trace.EffectiveStackBb,
            trace.OpenSizeBucket,
            trace.IsoSizeBucket,
            trace.ThreeBetBucket,
            trace.SqueezeBucket,
            trace.FourBetBucket,
            trace.JamThreshold,
            trace.RawActionHistory.Select(a => new PreflopNodeTraceActionDto(
                a.PlayerId.Value,
                a.Position,
                a.ActionType,
                a.AmountBb)).ToList());

    private static string BuildSizingSummary(PreflopQueryTrace trace)
        => $"open={trace.OpenSizeBucket}, iso={trace.IsoSizeBucket}, 3bet={trace.ThreeBetBucket}, squeeze={trace.SqueezeBucket}, 4bet={trace.FourBetBucket}, jam={trace.JamThreshold:0.##}";

    private static IReadOnlyList<PreflopNodeLegalActionDto> BuildLegalActions(PreflopNodeQueryRequestDto request, PreflopQueryTrace trace)
    {
        var state = BuildSnapshotState(request, trace);
        var actions = state.GenerateLegalActions(new TraceBetSizeSetProvider(trace));
        return actions.Select(ToLegalActionDto).ToList();
    }

    private static PreflopNodeLegalActionDto ToLegalActionDto(LegalAction action)
    {
        var amountBb = action.Amount is null
            ? null
            : decimal.Round(action.Amount.Value.Value / 100m, 2);

        var actionKey = action.ActionType switch
        {
            ActionType.Fold => "Fold",
            ActionType.Check => "Check",
            ActionType.Call => $"Call:{amountBb:0.##}",
            ActionType.Raise => action.Amount is null ? "Raise" : $"Raise:{amountBb:0.##}",
            ActionType.Bet => action.Amount is null ? "Bet" : $"Bet:{amountBb:0.##}",
            _ => action.ActionType.ToString()
        };

        return new PreflopNodeLegalActionDto(actionKey, action.ActionType, amountBb, false);
    }

    private static SolverHandState BuildSnapshotState(PreflopNodeQueryRequestDto request, PreflopQueryTrace trace)
    {
        var seatsByPlayer = request.Seats
            .OrderBy(s => s.Seat)
            .Select((seat, index) => new { Seat = seat, SeatIndex = index })
            .ToDictionary(x => new PlayerId(x.Seat.PlayerId));

        var contributions = seatsByPlayer.Keys.ToDictionary(id => id, _ => 0L);
        var stacks = seatsByPlayer.ToDictionary(
            kvp => kvp.Key,
            kvp => BbToSolverChips(kvp.Value.Seat.StartingStackBb));
        var folded = seatsByPlayer.Keys.ToDictionary(id => id, _ => false);

        var actionHistory = new List<SolverActionEntry>();
        long pot = 0;
        long currentBet = 0;
        long lastRaise = BbToSolverChips(1m);
        var raisesThisStreet = 0;

        void ApplyToContribution(PlayerId playerId, long targetContribution)
        {
            var delta = targetContribution - contributions[playerId];
            if (delta <= 0)
                return;

            contributions[playerId] = targetContribution;
            stacks[playerId] -= delta;
            pot += delta;
        }

        foreach (var action in trace.RawActionHistory)
        {
            var playerId = action.PlayerId;
            if (!seatsByPlayer.ContainsKey(playerId))
                continue;

            var targetContribution = BbToSolverChips(action.AmountBb);
            switch (action.ActionType)
            {
                case "POST_SB":
                    ApplyToContribution(playerId, targetContribution);
                    currentBet = Math.Max(currentBet, contributions[playerId]);
                    actionHistory.Add(new SolverActionEntry(playerId, ActionType.PostSmallBlind, new ChipAmount(targetContribution)));
                    break;
                case "POST_BB":
                    ApplyToContribution(playerId, targetContribution);
                    currentBet = Math.Max(currentBet, contributions[playerId]);
                    actionHistory.Add(new SolverActionEntry(playerId, ActionType.PostBigBlind, new ChipAmount(targetContribution)));
                    break;
                case "RAISE_TO":
                case "ALL_IN":
                    ApplyToContribution(playerId, targetContribution);
                    if (targetContribution > currentBet)
                    {
                        lastRaise = targetContribution - currentBet;
                        currentBet = targetContribution;
                        raisesThisStreet++;
                    }

                    actionHistory.Add(new SolverActionEntry(playerId, action.ActionType == "ALL_IN" ? ActionType.AllIn : ActionType.Raise, new ChipAmount(targetContribution)));
                    break;
                case "CALL":
                    ApplyToContribution(playerId, targetContribution);
                    actionHistory.Add(new SolverActionEntry(playerId, ActionType.Call, new ChipAmount(targetContribution)));
                    break;
                case "FOLD":
                    folded[playerId] = true;
                    actionHistory.Add(new SolverActionEntry(playerId, ActionType.Fold, ChipAmount.Zero));
                    break;
                case "CHECK":
                    actionHistory.Add(new SolverActionEntry(playerId, ActionType.Check, ChipAmount.Zero));
                    break;
            }
        }

        var players = seatsByPlayer
            .Select(kvp => new SolverPlayerState(
                kvp.Key,
                kvp.Value.SeatIndex,
                kvp.Value.Seat.Position,
                new ChipAmount(stacks[kvp.Key]),
                new ChipAmount(contributions[kvp.Key]),
                new ChipAmount(contributions[kvp.Key]),
                folded[kvp.Key],
                stacks[kvp.Key] == 0 && !folded[kvp.Key]))
            .ToList();

        var button = players.FirstOrDefault(p => p.Position == Position.BTN)?.SeatIndex ?? 0;
        var maxStartingStack = seatsByPlayer.Max(x => BbToSolverChips(x.Value.Seat.StartingStackBb));

        return new SolverHandState(
            new GameConfig(
                request.Seats.Count,
                new ChipAmount(BbToSolverChips(request.SmallBlind / request.BigBlind)),
                new ChipAmount(BbToSolverChips(1m)),
                ChipAmount.Zero,
                new ChipAmount(maxStartingStack)),
            Street.Preflop,
            button,
            new PlayerId(request.ActingPlayerId),
            new ChipAmount(pot),
            new ChipAmount(currentBet),
            new ChipAmount(lastRaise),
            raisesThisStreet,
            players,
            actionHistory);
    }

    private static long BbToSolverChips(decimal amountBb)
        => (long)Math.Round(amountBb * 100m, MidpointRounding.AwayFromZero);

    private sealed class TraceBetSizeSetProvider : IBetSizeSetProvider
    {
        private readonly IReadOnlyList<ChipAmount> _raiseSizes;

        public TraceBetSizeSetProvider(PreflopQueryTrace trace)
        {
            var sizes = new HashSet<decimal>();
            AddIfPositive(sizes, ParseBucket(trace.OpenSizeBucket));
            AddIfPositive(sizes, ParseBucket(trace.ThreeBetBucket));
            AddIfPositive(sizes, ParseBucket(trace.FourBetBucket));

            if (trace.ToCallBb > 0)
            {
                sizes.Add(4m);
                sizes.Add(9m);
            }

            _raiseSizes = sizes
                .OrderBy(x => x)
                .Select(BbToSolverChips)
                .Select(x => new ChipAmount(x))
                .ToArray();
        }

        public IReadOnlyList<ChipAmount> GetBetSizes(SolverHandState state)
            => Array.Empty<ChipAmount>();

        public IReadOnlyList<ChipAmount> GetRaiseSizes(SolverHandState state)
            => _raiseSizes;
    }

    private static decimal? ParseBucket(string value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static void AddIfPositive(HashSet<decimal> target, decimal? value)
    {
        if (value.HasValue && value.Value > 0)
            target.Add(value.Value);
    }

    private static PreflopNodeQueryRequestDto? BuildRequestFromHand(Hand hand)
    {
        var hero = hand.Players.FirstOrDefault(p => p.IsHero);
        if (hero is null)
            return null;

        var blindInfo = TryResolveBlinds(hand);
        if (!blindInfo.HasValue)
            return null;

        var seatMap = hand.Players.ToDictionary(p => p.Name, p => p);
        var positionsByPlayer = ResolvePositions(hand);
        var seats = hand.Players
            .OrderBy(p => p.Seat)
            .Select(p => new PreflopNodeSeatDto(
                p.Id,
                p.Name,
                p.Seat,
                positionsByPlayer.TryGetValue(p.Id, out var pos) ? pos : Position.Unknown,
                NormalizeStartingStackBb(p.StackStart, blindInfo.Value.BigBlind)))
            .ToList();

        var actions = BuildExtractorActions(hand, seatMap, blindInfo.Value.BigBlind, hero.Name)
            .Select(a => new PreflopNodeActionDto(a.PlayerId.Value, a.Type, a.AmountBb))
            .ToList();

        return new PreflopNodeQueryRequestDto(
            Street.Preflop,
            hero.Id,
            hand.HeroHoleCards,
            blindInfo.Value.SmallBlind,
            blindInfo.Value.BigBlind,
            seats,
            actions);
    }

    private static PreflopNodeQueryResultDto BuildUnsupported(string reason, PreflopQueryTrace? trace = null)
        => new(
            false,
            reason,
            NotYetImplemented,
            trace?.SolverKey ?? NotYetImplemented,
            Street.Preflop,
            trace?.ActingPosition ?? Position.Unknown,
            trace?.FacingPosition,
            trace?.HistorySignature ?? "UNSUPPORTED",
            trace?.PotBb ?? 0,
            trace?.ToCallBb ?? 0,
            trace?.EffectiveStackBb ?? 0,
            trace?.RaiseDepth ?? 0,
            trace is null ? "NA" : BuildSizingSummary(trace),
            Array.Empty<PreflopNodeLegalActionDto>(),
            Array.Empty<PreflopNodeStrategyItemDto>(),
            ToTraceDto(trace ?? EmptyTrace()));

    private static PreflopQueryTrace EmptyTrace()
        => new()
        {
            ActingPlayerId = new PlayerId(Guid.Empty),
            ActingPosition = Position.Unknown,
            HistorySignature = "UNSUPPORTED",
            RaiseDepth = 0,
            ToCallBb = 0,
            CurrentBetBb = 0,
            ActingContribBb = 0,
            PotBb = 0,
            EffectiveStackBb = 0,
            OpenSizeBucket = "NA",
            IsoSizeBucket = "NA",
            ThreeBetBucket = "NA",
            SqueezeBucket = "NA",
            FourBetBucket = "NA",
            JamThreshold = 0,
            SolverKey = "UNSUPPORTED",
            RawActionHistory = Array.Empty<PreflopRawActionTrace>()
        };

    private static List<PreflopInputAction> BuildExtractorActions(Hand hand, Dictionary<string, HandPlayer> playersByName, decimal bb, string heroName)
    {
        var actions = new List<PreflopInputAction>();
        foreach (var action in hand.Actions.Where(a => a.Street == Street.Preflop))
        {
            if (!playersByName.TryGetValue(action.Player, out var player))
                continue;

            var type = action.Type switch
            {
                ActionType.PostSmallBlind => "POST_SB",
                ActionType.PostBigBlind => "POST_BB",
                ActionType.Raise => "RAISE_TO",
                ActionType.AllIn => "ALL_IN",
                ActionType.Call => "CALL",
                ActionType.Check => "CHECK",
                ActionType.Fold => "FOLD",
                _ => "TYPE_4"
            };

            if (string.Equals(action.Player, heroName, StringComparison.Ordinal)
                && action.Type is not ActionType.PostSmallBlind and not ActionType.PostBigBlind)
            {
                break;
            }

            var amountBb = bb > 0 ? decimal.Round((action.Amount ?? 0m) / bb, 2) : 0m;
            actions.Add(new PreflopInputAction(new PlayerId(player.Id), type, amountBb));
        }

        return actions;
    }

    private static (decimal SmallBlind, decimal BigBlind)? TryResolveBlinds(Hand hand)
    {
        var sb = hand.Actions.FirstOrDefault(a => a.Street == Street.Preflop && a.Type == ActionType.PostSmallBlind)?.Amount;
        var bb = hand.Actions.FirstOrDefault(a => a.Street == Street.Preflop && a.Type == ActionType.PostBigBlind)?.Amount;
        if (!sb.HasValue || !bb.HasValue || bb.Value <= 0)
            return null;

        return (sb.Value, bb.Value);
    }

    private static decimal NormalizeStartingStackBb(decimal? stackStart, decimal bigBlind)
    {
        if (!stackStart.HasValue || stackStart.Value <= 0 || bigBlind <= 0)
            return 0m;

        var normalized = stackStart.Value / bigBlind;
        while (normalized > 500m)
            normalized /= 100m;

        return decimal.Round(Math.Max(0m, normalized), 2);
    }

    private static Dictionary<Guid, Position> ResolvePositions(Hand hand)
    {
        var orderedPlayers = hand.Players.OrderBy(p => p.Seat).ToList();
        if (orderedPlayers.Count == 0)
            return new Dictionary<Guid, Position>();

        var byName = orderedPlayers.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var sbName = hand.Actions.FirstOrDefault(a => a.Street == Street.Preflop && a.Type == ActionType.PostSmallBlind)?.Player;
        var bbName = hand.Actions.FirstOrDefault(a => a.Street == Street.Preflop && a.Type == ActionType.PostBigBlind)?.Player;

        if (!string.IsNullOrWhiteSpace(sbName)
            && !string.IsNullOrWhiteSpace(bbName)
            && byName.TryGetValue(sbName, out var sb)
            && byName.TryGetValue(bbName, out var bb)
            && sb.Id != bb.Id)
        {
            return ResolvePositionsFromBlinds(orderedPlayers, sb, bb);
        }

        return orderedPlayers
            .Select((player, index) => new { player.Id, Position = ResolveFallbackPosition(index, orderedPlayers.Count) })
            .ToDictionary(x => x.Id, x => x.Position);
    }

    private static Dictionary<Guid, Position> ResolvePositionsFromBlinds(IReadOnlyList<HandPlayer> orderedPlayers, HandPlayer sb, HandPlayer bb)
    {
        var bySeat = orderedPlayers.ToDictionary(p => p.Seat);
        var seats = orderedPlayers.Select(p => p.Seat).OrderBy(x => x).ToList();
        var playerCount = orderedPlayers.Count;

        var positions = new Dictionary<Guid, Position>
        {
            [sb.Id] = Position.SB,
            [bb.Id] = Position.BB
        };

        if (playerCount == 2)
            return positions;

        var sbIndex = seats.IndexOf(sb.Seat);
        var btnIndex = (sbIndex - 1 + seats.Count) % seats.Count;
        var btnSeat = seats[btnIndex];
        positions[bySeat[btnSeat].Id] = Position.BTN;

        var orderedFromBb = new List<int>();
        var bbIndex = seats.IndexOf(bb.Seat);
        for (var i = 1; i < seats.Count; i++)
        {
            var idx = (bbIndex + i) % seats.Count;
            var seat = seats[idx];
            if (seat == btnSeat || seat == sb.Seat || seat == bb.Seat)
                continue;

            orderedFromBb.Add(seat);
        }

        var earlyPositions = GetEarlyPositions(orderedFromBb.Count);
        for (var i = 0; i < orderedFromBb.Count; i++)
            positions[bySeat[orderedFromBb[i]].Id] = earlyPositions[i];

        return positions;
    }

    private static Position[] GetEarlyPositions(int count)
    {
        return count switch
        {
            <= 0 => [],
            1 => [Position.CO],
            2 => [Position.HJ, Position.CO],
            _ => [Position.UTG, Position.HJ, Position.CO]
        };
    }

    private static Position ResolveFallbackPosition(int index, int playerCount)
    {
        if (playerCount == 2)
            return index == 0 ? Position.SB : Position.BB;

        return playerCount switch
        {
            3 => new[] { Position.BTN, Position.SB, Position.BB }[index],
            4 => new[] { Position.CO, Position.BTN, Position.SB, Position.BB }[index],
            5 => new[] { Position.HJ, Position.CO, Position.BTN, Position.SB, Position.BB }[index],
            _ => new[] { Position.UTG, Position.HJ, Position.CO, Position.BTN, Position.SB, Position.BB }[Math.Min(index, 5)]
        };
    }
}
