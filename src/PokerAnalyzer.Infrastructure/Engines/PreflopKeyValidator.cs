using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopSolver;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed record PreflopActionHistoryEntry(
    Street Street,
    string PlayerId,
    Position? Position,
    ActionType ActionType,
    decimal Amount);

public sealed record PreflopKeyValidationContext(
    Position ActingPosition,
    string HistorySignature,
    decimal ToCallBb,
    decimal? CurrentBetBb,
    decimal? HeroContribBb,
    int? RaiseDepth,
    Position? LastAggressorPosition,
    IReadOnlyList<PreflopActionHistoryEntry> ActionHistory);

public sealed record PreflopKeyValidationResult(bool IsValid, string? Reason, PreflopKeyValidationContext Context)
{
    public static PreflopKeyValidationResult Valid(PreflopKeyValidationContext context) => new(true, null, context);

    public static PreflopKeyValidationResult Invalid(string reason, PreflopKeyValidationContext context) => new(false, reason, context);
}

public sealed class PreflopKeyValidator
{
    private static readonly IReadOnlyDictionary<string, int> SignatureRaiseDepthRequirements = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["VS_3BET"] = 2,
        ["VS_4BET"] = 3,
        ["VS_5BET"] = 4
    };

    public PreflopKeyValidationResult Validate(PreflopInfoSetKey key, PreflopSpotContext? context, HandState state, HeroContext hero)
    {
        var history = BuildActionHistory(hero);
        var toCallBb = context?.ToCallBb ?? key.ToCallBb;
        var raiseDepth = context?.RaiseDepth ?? DeriveRaiseDepth(history);
        var lastAggressorPosition = ResolveLastAggressorPosition(state, hero);
        var heroContribBb = DeriveHeroContributionBb(history, hero.HeroId, hero.BigBlind);

        var validationContext = new PreflopKeyValidationContext(
            key.ActingPosition,
            key.HistorySignature,
            toCallBb,
            context?.LastRaiseSizeBb,
            heroContribBb,
            raiseDepth,
            lastAggressorPosition,
            history);

        if (key.ActingPosition == Position.Unknown)
            return PreflopKeyValidationResult.Invalid($"Inconsistent preflop key: ActingPosition is Unknown for HistorySignature={key.HistorySignature}", validationContext);

        if (hero.PlayerPositions is null)
            return PreflopKeyValidationResult.Invalid($"Inconsistent preflop key: missing PlayerPositions mapping for ActingPosition={key.ActingPosition}", validationContext);

        if (!hero.PlayerPositions.Values.Contains(key.ActingPosition))
            return PreflopKeyValidationResult.Invalid($"Inconsistent preflop key: ActingPosition={key.ActingPosition} missing in PlayerPositions mapping", validationContext);

        if (string.Equals(key.HistorySignature, "OPEN", StringComparison.Ordinal) && toCallBb > 0)
            return PreflopKeyValidationResult.Invalid($"Inconsistent preflop key: OPEN with ToCallBb={toCallBb:0.###}, ActingPosition={key.ActingPosition}, lastAggressor={lastAggressorPosition?.ToString() ?? "Unknown"}", validationContext);

        if (key.HistorySignature.StartsWith("VS_", StringComparison.Ordinal) && toCallBb == 0)
            return PreflopKeyValidationResult.Invalid($"Inconsistent preflop key: {key.HistorySignature} with ToCallBb=0, ActingPosition={key.ActingPosition}, lastAggressor={lastAggressorPosition?.ToString() ?? "Unknown"}", validationContext);

        foreach (var requirement in SignatureRaiseDepthRequirements)
        {
            if (!key.HistorySignature.StartsWith(requirement.Key, StringComparison.Ordinal))
                continue;

            if (raiseDepth < requirement.Value)
                return PreflopKeyValidationResult.Invalid($"Inconsistent preflop key: {key.HistorySignature} requires RaiseDepth>={requirement.Value} but was {raiseDepth}", validationContext);
        }

        return PreflopKeyValidationResult.Valid(validationContext);
    }

    private static List<PreflopActionHistoryEntry> BuildActionHistory(HeroContext hero)
    {
        var actions = hero.ActionHistory?.Where(a => a.Street == Street.Preflop) ?? [];
        return actions.Select(a => new PreflopActionHistoryEntry(
            a.Street,
            a.ActorId.ToString(),
            hero.PlayerPositions?.TryGetValue(a.ActorId, out var position) == true ? position : null,
            a.Type,
            a.Amount.Value)).ToList();
    }

    private static int DeriveRaiseDepth(IReadOnlyList<PreflopActionHistoryEntry> history)
    {
        decimal currentBetBb = 0;
        var raiseDepth = 0;

        foreach (var action in history)
        {
            if (action.ActionType is not (ActionType.Raise or ActionType.Bet or ActionType.AllIn))
                continue;

            if (action.Amount > currentBetBb)
            {
                raiseDepth++;
                currentBetBb = action.Amount;
            }
        }

        return raiseDepth;
    }

    private static decimal? DeriveHeroContributionBb(IReadOnlyList<PreflopActionHistoryEntry> history, PlayerId heroId, ChipAmount bigBlind)
    {
        if (bigBlind.Value <= 0)
            return null;

        var heroAction = history.LastOrDefault(a => a.PlayerId == heroId.ToString());
        return heroAction is null ? null : heroAction.Amount / (decimal)bigBlind.Value;
    }

    private static Position? ResolveLastAggressorPosition(HandState state, HeroContext hero)
    {
        if (state.LastAggressor is null || hero.PlayerPositions is null)
            return null;

        return hero.PlayerPositions.TryGetValue(state.LastAggressor.Value, out var pos)
            ? pos
            : null;
    }
}
