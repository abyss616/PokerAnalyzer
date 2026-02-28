using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public static class PreflopKeyValidator
{
    public static PreflopValidationResult Validate(PreflopInfoSetKey key, PreflopSpotContext ctx)
    {
        if (key.HistorySignature == "OPEN" && key.ToCallBb == 0)
            return PreflopValidationResult.Invalid("Invalid key: OPEN with ToCall == 0.");

        if (key.HistorySignature.StartsWith("VS_", StringComparison.Ordinal) && key.ToCallBb == 0)
            return PreflopValidationResult.Invalid("Invalid key: VS_* signature with ToCall == 0.");

        if (!IsDepthValid(key.HistorySignature, ctx.RaiseDepth))
            return PreflopValidationResult.Invalid($"Invalid key: {key.HistorySignature} requires higher raise depth (actual={ctx.RaiseDepth}).");

        if (ctx.ActingPosition == Position.Unknown || key.ActingPosition == Position.Unknown || key.ActingPosition != ctx.ActingPosition)
            return PreflopValidationResult.Invalid("Invalid key: acting position undefined/inconsistent with player to act.");

        if (key.HistorySignature.StartsWith("VS_", StringComparison.Ordinal) && (!ctx.FacingPlayerId.HasValue || !ctx.FacingPosition.HasValue || ctx.FacingPosition == Position.Unknown))
            return PreflopValidationResult.Invalid("Invalid key: VS_* requires facing/last aggressor position.");

        return PreflopValidationResult.Valid();
    }

    private static bool IsDepthValid(string signature, int raiseDepth)
        => signature switch
        {
            "VS_3BET" => raiseDepth >= 2,
            "VS_4BET" => raiseDepth >= 3,
            "VS_5BET" => raiseDepth >= 4,
            _ => true
        };
}
