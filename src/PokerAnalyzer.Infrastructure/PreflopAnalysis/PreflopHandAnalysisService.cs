using PokerAnalyzer.Application.PreflopAnalysis;
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
        var hand = await _hands.GetHandByGameCodeAsync(handNumber, ct);
        if (hand is null)
            return null;

        var hero = hand.Players.FirstOrDefault(p => p.IsHero);
        if (hero is null)
            return CreateFallback(handNumber);

        var blindInfo = TryResolveBlinds(hand);
        if (!blindInfo.HasValue)
            return CreateFallback(handNumber);

        var seatMap = hand.Players.ToDictionary(p => p.Name, p => p);
        var seats = hand.Players
            .OrderBy(p => p.Seat)
            .Select((p, i) => new PlayerSeat(
                new PlayerId(p.Id),
                p.Name,
                p.Seat,
                ResolvePosition(i, hand.Players.Count),
                new ChipAmount(ToChips(p.StackStart ?? 0m, blindInfo.Value.BigBlind))))
            .ToList();

        var heroSeat = seats.FirstOrDefault(s => s.Name == hero.Name);
        if (heroSeat is null)
            return CreateFallback(handNumber);

        var actionsBeforeHero = BuildExtractorActions(hand, seatMap, blindInfo.Value.BigBlind, hero.Name);

        var extraction = _extractor.TryExtract(
            seats,
            actionsBeforeHero,
            heroSeat.Id,
            blindInfo.Value.SmallBlind,
            blindInfo.Value.BigBlind);

        var canonicalNode = extraction.IsSupported
            ? BuildCanonicalNode(extraction.Trace)
            : NotYetImplemented;

        var legalActions = extraction.IsSupported
            ? BuildLegalActionsText(extraction.Trace)
            : NotYetImplemented;

        var mixedStrategy = NotYetImplemented;
        var actualVsRecommendation = NotYetImplemented;

        if (extraction.IsSupported && extraction.Key is not null)
        {
            var strategy = await _strategyProvider.GetMixedStrategyAsync(extraction.Key.SolverKey, ct);
            if (strategy is { Count: > 0 })
            {
                mixedStrategy = FormatStrategy(strategy);
                actualVsRecommendation = BuildActualVsRecommendation(hand, hero.Name, strategy);
            }
            else
            {
                actualVsRecommendation = BuildActualVsRecommendationFallback(hand, hero.Name);
            }
        }

        return new PreflopHandAnalysisResultDto(
            handNumber.ToString(),
            canonicalNode,
            legalActions,
            mixedStrategy,
            actualVsRecommendation);
    }

    private static PreflopHandAnalysisResultDto CreateFallback(long handNumber) =>
        new(
            handNumber.ToString(),
            NotYetImplemented,
            NotYetImplemented,
            NotYetImplemented,
            NotYetImplemented);

    private static string BuildCanonicalNode(PreflopQueryTrace trace)
    {
        if (trace.HistorySignature is "UNSUPPORTED")
            return NotYetImplemented;

        var eff = trace.EffectiveStackBb > 0 ? $", {trace.EffectiveStackBb:0.##}bb" : string.Empty;
        return trace.HistorySignature switch
        {
            "VS_OPEN" when trace.ActingPosition == Position.BB && trace.FacingPosition.HasValue
                => $"BB vs {trace.FacingPosition.Value} Open{eff}, facing {trace.CurrentBetBb:0.##}bb open",
            "OPEN" => $"{trace.ActingPosition} Open{eff}",
            "LIMP" => $"{trace.ActingPosition} Limp{eff}",
            "VS_3BET" => $"Facing 3Bet, {(IsInPosition(trace) ? "IP" : "OOP")}{eff}",
            _ => NotYetImplemented
        };
    }

    private static bool IsInPosition(PreflopQueryTrace trace)
        => trace.ActingPosition switch
        {
            Position.BTN => true,
            Position.CO when trace.FacingPosition is Position.HJ or Position.UTG => true,
            _ => false
        };

    private static string BuildLegalActionsText(PreflopQueryTrace trace)
    {
        var actions = new List<string>();
        if (trace.ToCallBb > 0)
            actions.Add("Fold");
        else
            actions.Add("Check");

        if (trace.ToCallBb > 0)
            actions.Add($"Call {trace.ToCallBb:0.##}bb");

        actions.Add("Raise");

        return string.Join(", ", actions.Distinct());
    }

    private static string BuildActualVsRecommendationFallback(Hand hand, string heroName)
    {
        var heroAction = hand.Actions
            .FirstOrDefault(a => a.Street == Street.Preflop
                && string.Equals(a.Player, heroName, StringComparison.Ordinal)
                && a.Type is not ActionType.PostSmallBlind and not ActionType.PostBigBlind);

        if (heroAction is null)
            return NotYetImplemented;

        return $"Hero {heroAction.Type}. Solver recommendation: {NotYetImplemented}.";
    }

    private static string BuildActualVsRecommendation(Hand hand, string heroName, IReadOnlyDictionary<string, decimal> strategy)
    {
        var heroAction = hand.Actions
            .FirstOrDefault(a => a.Street == Street.Preflop
                && string.Equals(a.Player, heroName, StringComparison.Ordinal)
                && a.Type is not ActionType.PostSmallBlind and not ActionType.PostBigBlind);

        if (heroAction is null)
            return NotYetImplemented;

        var actionKey = heroAction.Type.ToString();
        var inStrategy = strategy.ContainsKey(actionKey);
        return inStrategy
            ? $"Hero {heroAction.Type}. Solver mix: {FormatStrategy(strategy)}. Actual action is in-strategy."
            : $"Hero {heroAction.Type}. Solver mix: {FormatStrategy(strategy)}. Solver prefers other actions.";
    }

    private static string FormatStrategy(IReadOnlyDictionary<string, decimal> strategy)
        => string.Join(", ", strategy.Select(kv => $"{kv.Key} {kv.Value:0.##}%"));

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

    private static long ToChips(decimal stack, decimal bb)
    {
        if (bb <= 0)
            return 0;

        return (long)Math.Round(stack / bb * 100m, MidpointRounding.AwayFromZero);
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
