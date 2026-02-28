using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed record PreflopInfoSetKey(
    Position ActingPosition,
    Position? FacingPosition,
    string HistorySignature,
    int RaiseDepth,
    decimal ToCallBb,
    decimal EffectiveStackBb,
    string OpenSizeBucket,
    string IsoSizeBucket,
    string ThreeBetBucket,
    string SqueezeBucket,
    string FourBetBucket,
    decimal JamThreshold,
    string SolverKey);

public sealed record PreflopSpotContext(
    PlayerId ActingPlayerId,
    Position ActingPosition,
    PlayerId? FacingPlayerId,
    Position? FacingPosition,
    int RaiseDepth,
    decimal ToCallBb,
    decimal CurrentBetBb,
    decimal ActingContribBb,
    decimal PotBb,
    decimal EffectiveStackBb);

public sealed record PreflopExtractionResult(
    bool IsSupported,
    PreflopInfoSetKey? Key,
    PreflopQueryTrace Trace,
    string? UnsupportedReason)
{
    public static PreflopExtractionResult Supported(PreflopInfoSetKey key, PreflopQueryTrace trace) => new(true, key, trace, null);
    public static PreflopExtractionResult Unsupported(string reason, PreflopQueryTrace trace) => new(false, null, trace, reason);
}

public sealed record PreflopValidationResult(bool IsValid, string? Reason)
{
    public static PreflopValidationResult Valid() => new(true, null);
    public static PreflopValidationResult Invalid(string reason) => new(false, reason);
}
