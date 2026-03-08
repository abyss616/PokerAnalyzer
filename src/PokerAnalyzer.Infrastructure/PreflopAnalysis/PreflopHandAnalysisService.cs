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

        var extraction = _extractor.TryExtract(seats, actions, actingPlayerId, request.SmallBlind, request.BigBlind);
        if (!extraction.IsSupported || extraction.Key is null)
            return BuildUnsupported(extraction.UnsupportedReason ?? "Preflop extraction was unsupported.", extraction.Trace);

        var legalActions = BuildLegalActions(extraction.Trace);
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

        return strategy
            .Select(kv => new PreflopNodeStrategyItemDto(kv.Key, decimal.Round(kv.Value * 100m, 2)))
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

    private static IReadOnlyList<PreflopNodeLegalActionDto> BuildLegalActions(PreflopQueryTrace trace)
    {
        var actions = new List<PreflopNodeLegalActionDto>();

        if (trace.ToCallBb > 0)
            actions.Add(new PreflopNodeLegalActionDto("Fold", ActionType.Fold, null, false));

        if (trace.ToCallBb > 0)
            actions.Add(new PreflopNodeLegalActionDto($"Call:{trace.ToCallBb:0.##}", ActionType.Call, trace.ToCallBb, false));
        else
            actions.Add(new PreflopNodeLegalActionDto("Check", ActionType.Check, null, false));

        var raiseSizes = new HashSet<decimal>();
        AddIfPositive(raiseSizes, ParseBucket(trace.OpenSizeBucket));
        AddIfPositive(raiseSizes, ParseBucket(trace.ThreeBetBucket));
        AddIfPositive(raiseSizes, ParseBucket(trace.FourBetBucket));

        if (trace.ToCallBb > 0)
        {
            raiseSizes.Add(4m);
            raiseSizes.Add(9m);
        }
        else
        {
            raiseSizes.Add(2.5m);
        }

        foreach (var size in raiseSizes.Where(x => x > trace.CurrentBetBb).OrderBy(x => x))
        {
            var isFacingAllIn = trace.EffectiveStackBb > 0 && size >= trace.EffectiveStackBb;
            actions.Add(new PreflopNodeLegalActionDto($"Raise:{size:0.##}", ActionType.Raise, size, isFacingAllIn));
        }

        return actions;
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
        var seats = hand.Players
            .OrderBy(p => p.Seat)
            .Select((p, i) => new PreflopNodeSeatDto(
                p.Id,
                p.Name,
                p.Seat,
                ResolvePosition(i, hand.Players.Count),
                blindInfo.Value.BigBlind > 0 ? decimal.Round((p.StackStart ?? 0m) / blindInfo.Value.BigBlind, 2) : 0m))
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

            var amountBb = bb > 0 ? decimal.Round((action.Amount ?? 0m) / bb, 2) : 0m;
            actions.Add(new PreflopInputAction(new PlayerId(player.Id), type, amountBb));

            if (string.Equals(action.Player, heroName, StringComparison.Ordinal)
                && action.Type is not ActionType.PostSmallBlind and not ActionType.PostBigBlind)
            {
                break;
            }
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

    private static Position ResolvePosition(int index, int playerCount)
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
