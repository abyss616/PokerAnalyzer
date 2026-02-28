using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.PreflopTree;
using PokerAnalyzer.Infrastructure.PreflopSolver;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed record PreflopSpotContext(
    Position ActingPosition,
    Position? FacingPosition,
    int RaiseDepth,
    bool PotIsUnopened,
    decimal ToCallBb,
    decimal EffectiveStackBb,
    decimal LastRaiseSizeBb,
    decimal? OpenSizeBucket,
    decimal? ThreeBetSizeBucket,
    decimal? FourBetSizeBucket,
    bool IsSupported,
    string? UnsupportedReason);

public static class PreflopHistorySignatureV2
{
    public static string Build(PreflopSpotContext context)
    {
        if (!context.IsSupported)
            return "UNSUPPORTED";

        if (context.RaiseDepth == 0)
            return context.ToCallBb <= 0 ? "OPEN" : "UNOPENED";

        return context.RaiseDepth switch
        {
            1 => $"VS_OPEN_{FormatBucket(context.OpenSizeBucket ?? context.LastRaiseSizeBb)}",
            2 => $"VS_3BET_{FormatBucket(context.ThreeBetSizeBucket ?? context.LastRaiseSizeBb)}",
            3 => $"VS_4BET_{FormatBucket(context.FourBetSizeBucket ?? context.LastRaiseSizeBb)}",
            _ => "UNSUPPORTED"
        };
    }

    private static string FormatBucket(decimal value)
    {
        var rounded = decimal.Round(value, 1, MidpointRounding.AwayFromZero);
        return rounded % 1 == 0 ? decimal.Truncate(rounded).ToString() : rounded.ToString("0.#");
    }
}

public static class PreflopSpotContextBuilder
{
    public static PreflopSpotContext FromPublicState(
        PreflopPublicState state,
        IReadOnlyList<Position> positions,
        decimal effectiveStackBb)
    {
        var actingIndex = state.ActingIndex;
        var toCall = Math.Max(0, state.CurrentToCallBb - state.ContribBb[actingIndex]);
        var raiseDepth = Math.Max(0, state.RaisesCount - 1);
        var lastRaise = Math.Max(0, state.CurrentToCallBb);
        var facing = state.LastAggressorIndex >= 0 && state.LastAggressorIndex < positions.Count
            ? positions[state.LastAggressorIndex]
            : null;

        var context = new PreflopSpotContext(
            positions[actingIndex],
            facing,
            raiseDepth,
            raiseDepth == 0,
            toCall,
            effectiveStackBb,
            lastRaise,
            raiseDepth >= 1 ? state.CurrentToCallBb : null,
            raiseDepth >= 2 ? state.CurrentToCallBb : null,
            raiseDepth >= 3 ? state.CurrentToCallBb : null,
            IsSupported: true,
            UnsupportedReason: null);

        return Validate(context);
    }

    public static PreflopSpotContext FromHistory(
        HandState state,
        HeroContext hero,
        PreflopSizingConfig sizing,
        PreflopSizingNormalizer normalizer,
        Position actingPosition,
        Position? facingPosition)
    {
        var history = hero.ActionHistory?.Where(a => a.Street == Street.Preflop).ToList() ?? [];
        var contribBb = hero.PlayerPositions!.Keys.ToDictionary(id => id, _ => 0m);

        var playerCount = hero.PlayerPositions.Count;
        var sbId = hero.PlayerPositions.FirstOrDefault(kvp => kvp.Value == Position.SB).Key;
        if (playerCount == 2)
            sbId = hero.PlayerPositions.FirstOrDefault(kvp => kvp.Value == Position.BTN).Key;
        var bbId = hero.PlayerPositions.FirstOrDefault(kvp => kvp.Value == Position.BB).Key;

        if (sbId != default && contribBb.ContainsKey(sbId))
            contribBb[sbId] = hero.SmallBlind.Value / (decimal)hero.BigBlind.Value;
        if (bbId != default && contribBb.ContainsKey(bbId))
            contribBb[bbId] = 1m;

        var betToCallBb = contribBb.Values.DefaultIfEmpty(0m).Max();
        var raiseDepth = 0;
        decimal? openBucket = null;
        decimal? threeBetBucket = null;
        decimal? fourBetBucket = null;

        foreach (var action in history)
        {
            if (!contribBb.ContainsKey(action.ActorId))
                continue;

            if (action.Type is ActionType.Raise or ActionType.Bet or ActionType.AllIn)
            {
                var raiseToBb = action.Amount.Value / (decimal)hero.BigBlind.Value;
                if (raiseToBb <= betToCallBb)
                    continue;

                var normalizedRaiseTo = raiseToBb;
                if (raiseDepth == 0)
                {
                    openBucket = normalizer.Nearest(raiseToBb, sizing.OpenSizesBb);
                    normalizedRaiseTo = openBucket.Value;
                }
                else if (raiseDepth == 1)
                {
                    var multiplier = betToCallBb <= 0 ? 1m : raiseToBb / betToCallBb;
                    var normalizedMultiplier = normalizer.Nearest(multiplier, sizing.ThreeBetSizeMultipliers);
                    normalizedRaiseTo = betToCallBb * normalizedMultiplier;
                    threeBetBucket = normalizedRaiseTo;
                }
                else if (raiseDepth == 2)
                {
                    var multiplier = betToCallBb <= 0 ? 1m : raiseToBb / betToCallBb;
                    var normalizedMultiplier = normalizer.Nearest(multiplier, sizing.FourBetSizeMultipliers);
                    normalizedRaiseTo = betToCallBb * normalizedMultiplier;
                    fourBetBucket = normalizedRaiseTo;
                }

                raiseDepth++;
                contribBb[action.ActorId] = normalizedRaiseTo;
                betToCallBb = normalizedRaiseTo;
                continue;
            }

            if (action.Type == ActionType.Call)
                contribBb[action.ActorId] = betToCallBb;
            else if (action.Type is ActionType.PostSmallBlind or ActionType.PostBigBlind)
                contribBb[action.ActorId] = action.Amount.Value / (decimal)hero.BigBlind.Value;
        }

        var toCallBb = Math.Max(0m, betToCallBb - contribBb.GetValueOrDefault(hero.HeroId));
        var context = new PreflopSpotContext(
            actingPosition,
            facingPosition,
            raiseDepth,
            raiseDepth == 0,
            toCallBb,
            state.Stacks[hero.HeroId].Value / (decimal)hero.BigBlind.Value,
            betToCallBb,
            openBucket,
            threeBetBucket,
            fourBetBucket,
            IsSupported: true,
            UnsupportedReason: null);

        return Validate(context);
    }

    private static PreflopSpotContext Validate(PreflopSpotContext context)
    {
        var signature = PreflopHistorySignatureV2.Build(context);
        if (signature == "OPEN" && context.ToCallBb > 0)
            return context with { IsSupported = false, UnsupportedReason = "OPEN spot has toCall > 0" };
        if (signature.StartsWith("VS_", StringComparison.Ordinal) && context.ToCallBb <= 0)
            return context with { IsSupported = false, UnsupportedReason = "VS_* spot has toCall == 0" };
        if (context.RaiseDepth > 3)
            return context with { IsSupported = false, UnsupportedReason = $"Raise depth {context.RaiseDepth} is not supported" };

        return context;
    }
}
