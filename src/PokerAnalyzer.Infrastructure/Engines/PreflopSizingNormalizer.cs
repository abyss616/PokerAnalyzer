using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopSolver;

namespace PokerAnalyzer.Infrastructure.Engines;

public enum PreflopSizingActionType
{
    Open,
    ThreeBet,
    FourBet,
    Jam
}

public sealed record PreflopSizingNormalizationResult(decimal NormalizedSizeBb, int NormalizedToCallBb, string NormalizationNote);

public sealed class PreflopSizingNormalizer
{
    public PreflopSizingNormalizationResult Normalize(
        Street street,
        PreflopSizingActionType actionType,
        decimal realSizeBb,
        decimal toCallBb,
        decimal currentBetBb,
        decimal potBb,
        PreflopSizingConfig sizing)
    {
        if (street != Street.Preflop)
            return new PreflopSizingNormalizationResult(realSizeBb, RoundBb(toCallBb), $"Non-preflop action unchanged ({realSizeBb:0.###}bb)");

        var allowed = actionType switch
        {
            PreflopSizingActionType.Open => sizing.OpenSizesBb,
            PreflopSizingActionType.ThreeBet => sizing.ThreeBetSizeMultipliers,
            PreflopSizingActionType.FourBet => sizing.FourBetSizeMultipliers,
            PreflopSizingActionType.Jam => new[] { sizing.JamThresholdStackBb },
            _ => Array.Empty<decimal>()
        };

        if (allowed.Count == 0)
            return new PreflopSizingNormalizationResult(realSizeBb, RoundBb(toCallBb), $"{actionType} unchanged ({realSizeBb:0.###}bb)");

        var normalized = Nearest(realSizeBb, allowed);
        var normalizedToCall = RoundBb(actionType switch
        {
            PreflopSizingActionType.Open => normalized,
            PreflopSizingActionType.ThreeBet or PreflopSizingActionType.FourBet => currentBetBb,
            PreflopSizingActionType.Jam => Math.Max(toCallBb, currentBetBb),
            _ => toCallBb
        });

        return new PreflopSizingNormalizationResult(
            normalized,
            normalizedToCall,
            $"{actionType} {realSizeBb:0.###}bb -> {normalized:0.###}bb");
    }

    public decimal Nearest(decimal value, IReadOnlyList<decimal> options)
    {
        var best = options[0];
        var bestDistance = Math.Abs(value - best);

        for (var i = 1; i < options.Count; i++)
        {
            var candidate = options[i];
            var distance = Math.Abs(value - candidate);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static int RoundBb(decimal bb) => (int)Math.Round(bb, MidpointRounding.ToEven);
}

public sealed record PreflopExtractionResult(
    PreflopInfoSetKey Key,
    decimal? RealOpenSizeBb,
    decimal? RealThreeBetSizeBb,
    decimal? RealFourBetSizeBb,
    int RealToCallBb,
    decimal? NormalizedOpenSizeBb,
    decimal? NormalizedThreeBetSizeBb,
    decimal? NormalizedFourBetSizeBb,
    int NormalizedToCallBb,
    string NormalizationNote);

public static class PreflopStateExtractor
{
    public static PreflopExtractionResult? TryExtract(HandState state, HeroContext hero, PreflopSizingConfig sizing, PreflopSizingNormalizer? normalizer = null)
    {
        if (hero.PlayerPositions is null || !hero.PlayerPositions.TryGetValue(hero.HeroId, out var heroPos))
            return null;

        normalizer ??= new PreflopSizingNormalizer();
        var playerCount = hero.PlayerPositions.Count;
        var normalizedHeroPos = playerCount == 2 && heroPos == Position.SB ? Position.BTN : heroPos;
        var effectiveStackBb = (int)Math.Round(state.Stacks[hero.HeroId].Value / (decimal)hero.BigBlind.Value);

        var history = hero.ActionHistory?.Where(a => a.Street == Street.Preflop).ToList() ?? [];
        if (history.Count == 0)
        {
            var realToCall = hero.BigBlind.Value <= 0 ? 0 : PreflopSizingNormalizer.RoundBb(state.GetToCall(hero.HeroId).Value / (decimal)hero.BigBlind.Value);
            var key = new PreflopInfoSetKey(playerCount, normalizedHeroPos, "UNOPENED", realToCall, effectiveStackBb);
            return new PreflopExtractionResult(key, null, null, null, realToCall, null, null, null, realToCall, "No preflop raises to normalize");
        }

        var playerIds = hero.PlayerPositions.Keys.ToHashSet();
        var contribBb = playerIds.ToDictionary(id => id, _ => 0m);

        var sbId = hero.PlayerPositions.FirstOrDefault(kvp => kvp.Value == Position.SB).Key;
        if (playerCount == 2)
            sbId = hero.PlayerPositions.FirstOrDefault(kvp => kvp.Value == Position.BTN).Key;
        var bbId = hero.PlayerPositions.FirstOrDefault(kvp => kvp.Value == Position.BB).Key;

        if (sbId != default && contribBb.ContainsKey(sbId))
            contribBb[sbId] = hero.SmallBlind.Value / (decimal)hero.BigBlind.Value;
        if (bbId != default && contribBb.ContainsKey(bbId))
            contribBb[bbId] = 1m;

        var betToCallBb = contribBb.Values.DefaultIfEmpty(0m).Max();
        var raisesCount = 0;
        var notes = new List<string>();

        decimal? realOpen = null;
        decimal? realThreeBet = null;
        decimal? realFourBet = null;
        decimal? normalizedOpen = null;
        decimal? normalizedThreeBet = null;
        decimal? normalizedFourBet = null;

        foreach (var action in history)
        {
            if (!contribBb.ContainsKey(action.ActorId))
                continue;

            if (action.Type is ActionType.Raise or ActionType.Bet)
            {
                var realRaiseToBb = action.Amount.Value / (decimal)hero.BigBlind.Value;
                var actorContribBb = contribBb[action.ActorId];
                var actorToCallBb = Math.Max(0m, betToCallBb - actorContribBb);

                if (raisesCount == 0)
                {
                    realOpen = realRaiseToBb;
                    var normalizedAction = normalizer.Normalize(Street.Preflop, PreflopSizingActionType.Open, realRaiseToBb, actorToCallBb, betToCallBb, 0m, sizing);
                    normalizedOpen = normalizedAction.NormalizedSizeBb;
                    notes.Add(normalizedAction.NormalizationNote);
                    contribBb[action.ActorId] = normalizedAction.NormalizedSizeBb;
                    betToCallBb = normalizedAction.NormalizedSizeBb;
                }
                else if (raisesCount == 1)
                {
                    var realMultiplier = betToCallBb <= 0 ? realRaiseToBb : realRaiseToBb / betToCallBb;
                    realThreeBet = realMultiplier;
                    var normalizedAction = normalizer.Normalize(Street.Preflop, PreflopSizingActionType.ThreeBet, realMultiplier, actorToCallBb, betToCallBb, 0m, sizing);
                    normalizedThreeBet = normalizedAction.NormalizedSizeBb;
                    notes.Add($"3bet to {realRaiseToBb:0.###}bb ({realMultiplier:0.###}x) -> {(betToCallBb * normalizedAction.NormalizedSizeBb):0.###}bb ({normalizedAction.NormalizedSizeBb:0.###}x)");
                    var normalizedRaiseTo = betToCallBb * normalizedAction.NormalizedSizeBb;
                    contribBb[action.ActorId] = normalizedRaiseTo;
                    betToCallBb = normalizedRaiseTo;
                }
                else
                {
                    var realMultiplier = betToCallBb <= 0 ? realRaiseToBb : realRaiseToBb / betToCallBb;
                    realFourBet = realMultiplier;
                    var normalizedAction = normalizer.Normalize(Street.Preflop, PreflopSizingActionType.FourBet, realMultiplier, actorToCallBb, betToCallBb, 0m, sizing);
                    normalizedFourBet = normalizedAction.NormalizedSizeBb;
                    notes.Add($"4bet to {realRaiseToBb:0.###}bb ({realMultiplier:0.###}x) -> {(betToCallBb * normalizedAction.NormalizedSizeBb):0.###}bb ({normalizedAction.NormalizedSizeBb:0.###}x)");
                    var normalizedRaiseTo = betToCallBb * normalizedAction.NormalizedSizeBb;
                    contribBb[action.ActorId] = normalizedRaiseTo;
                    betToCallBb = normalizedRaiseTo;
                }

                raisesCount++;
            }
            else if (action.Type == ActionType.AllIn)
            {
                var realRaiseToBb = action.Amount.Value / (decimal)hero.BigBlind.Value;
                var actorContribBb = contribBb[action.ActorId];
                var actorToCallBb = Math.Max(0m, betToCallBb - actorContribBb);
                var normalizedAction = normalizer.Normalize(Street.Preflop, PreflopSizingActionType.Jam, realRaiseToBb, actorToCallBb, betToCallBb, 0m, sizing);
                notes.Add(normalizedAction.NormalizationNote);
                contribBb[action.ActorId] = realRaiseToBb;
                betToCallBb = Math.Max(betToCallBb, realRaiseToBb);
                raisesCount++;
            }
            else if (action.Type == ActionType.Call)
            {
                contribBb[action.ActorId] = betToCallBb;
            }
            else if (action.Type is ActionType.PostSmallBlind or ActionType.PostBigBlind)
            {
                contribBb[action.ActorId] = action.Amount.Value / (decimal)hero.BigBlind.Value;
                betToCallBb = Math.Max(betToCallBb, contribBb[action.ActorId]);
            }
        }

        var normalizedToCallBb = PreflopSizingNormalizer.RoundBb(Math.Max(0m, betToCallBb - contribBb.GetValueOrDefault(hero.HeroId)));
        var realToCallBb = hero.BigBlind.Value <= 0
            ? 0
            : PreflopSizingNormalizer.RoundBb(state.GetToCall(hero.HeroId).Value / (decimal)hero.BigBlind.Value);

        var actionTypes = history.Select(a => a.Type).ToList();
        var infoSet = new PreflopInfoSetKey(
            playerCount,
            normalizedHeroPos,
            PreflopHistorySignature.Build(actionTypes),
            normalizedToCallBb,
            effectiveStackBb);

        var note = notes.Count == 0 ? "No preflop raises to normalize" : string.Join(" | ", notes);

        return new PreflopExtractionResult(
            infoSet,
            realOpen,
            realThreeBet,
            realFourBet,
            realToCallBb,
            normalizedOpen,
            normalizedThreeBet,
            normalizedFourBet,
            normalizedToCallBb,
            note);
    }
}
