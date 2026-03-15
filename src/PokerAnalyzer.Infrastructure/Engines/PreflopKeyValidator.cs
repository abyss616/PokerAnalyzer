using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public static class PreflopKeyValidator
{
    public static PreflopValidationResult Validate(PreflopInfoSetKey key, PreflopSpotContext ctx)
    {
        if (!IsKnownHistorySignature(key.HistorySignature))
            return PreflopValidationResult.Invalid($"Invalid key: unknown history signature '{key.HistorySignature}'.");

        if (key.HistorySignature == "OPEN" && key.ToCallBb != 0)
            return PreflopValidationResult.Invalid("Invalid key: OPEN with non-zero ToCall.");

        if (key.HistorySignature == "LIMP" && key.ToCallBb <= 0)
            return PreflopValidationResult.Invalid("Invalid key: LIMP requires ToCall > 0.");

        if (key.HistorySignature == "LIMP_OPTION" && key.ToCallBb != 0)
            return PreflopValidationResult.Invalid("Invalid key: LIMP_OPTION requires ToCall == 0.");

        if (key.HistorySignature == "UNOPENED_CHECK" && key.ToCallBb != 0)
            return PreflopValidationResult.Invalid("Invalid key: UNOPENED_CHECK requires ToCall == 0.");

        if (key.HistorySignature.StartsWith("VS_", StringComparison.Ordinal) && key.ToCallBb <= 0)
            return PreflopValidationResult.Invalid("Invalid key: VS_* signature with ToCall <= 0.");

        if (!IsDepthValid(key.HistorySignature, ctx.RaiseDepth))
            return PreflopValidationResult.Invalid($"Invalid key: {key.HistorySignature} requires higher raise depth (actual={ctx.RaiseDepth}).");

        if (key.HistorySignature == "VS_OPEN" && !key.OpenSizeBucketBb.HasValue)
            return PreflopValidationResult.Invalid("Invalid key: VS_OPEN requires open size bucket.");

        if (key.HistorySignature == "VS_3BET" && !key.ThreeBetSizeBucketBb.HasValue)
            return PreflopValidationResult.Invalid("Invalid key: VS_3BET requires 3bet size bucket.");

        if (key.HistorySignature == "VS_4BET" && !key.FourBetSizeBucketBb.HasValue)
            return PreflopValidationResult.Invalid("Invalid key: VS_4BET requires 4bet size bucket.");

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

    private static bool IsKnownHistorySignature(string signature)
        => signature is "OPEN"
            or "LIMP"
            or "LIMP_OPTION"
            or "UNOPENED"
            or "UNOPENED_SB"
            or "UNOPENED_CHECK"
            or "UNOPENED_FOLD"
            or "VS_OPEN"
            or "VS_3BET"
            or "VS_4BET"
            or "VS_5BET";
}
