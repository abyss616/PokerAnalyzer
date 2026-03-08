using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopAnalysis;

public sealed record PreflopNodeQueryRequestDto(
    Street Street,
    Guid ActingPlayerId,
    string? HeroHoleCards,
    decimal SmallBlind,
    decimal BigBlind,
    IReadOnlyList<PreflopNodeSeatDto> Seats,
    IReadOnlyList<PreflopNodeActionDto> PublicActionHistory);

public sealed record PreflopNodeSeatDto(
    Guid PlayerId,
    string Name,
    int Seat,
    Position Position,
    decimal StartingStackBb);

public sealed record PreflopNodeActionDto(
    Guid PlayerId,
    string ActionType,
    decimal AmountBb);

public sealed record PreflopNodeQueryResultDto(
    bool IsSupported,
    string? UnsupportedReason,
    string CanonicalKey,
    string SolverKey,
    Street Street,
    Position ActingPosition,
    Position? FacingPosition,
    string HistorySignature,
    decimal PotBb,
    decimal ToCallBb,
    decimal EffectiveStackBb,
    int RaiseDepth,
    string SizingBucketSummary,
    IReadOnlyList<PreflopNodeLegalActionDto> LegalActions,
    IReadOnlyList<PreflopNodeStrategyItemDto> Strategy,
    PreflopNodeTraceDto Trace);

public sealed record PreflopNodeLegalActionDto(
    string ActionKey,
    ActionType ActionType,
    decimal? SizeBb,
    bool IsFacingAllIn);

public sealed record PreflopNodeStrategyItemDto(string ActionKey, decimal Frequency);

public sealed record PreflopNodeTraceDto(
    string SolverKey,
    string HistorySignature,
    int RaiseDepth,
    decimal ToCallBb,
    decimal CurrentBetBb,
    decimal PotBb,
    decimal EffectiveStackBb,
    string OpenSizeBucket,
    string IsoSizeBucket,
    string ThreeBetBucket,
    string SqueezeBucket,
    string FourBetBucket,
    decimal JamThreshold,
    IReadOnlyList<PreflopNodeTraceActionDto> RawActionHistory);

public sealed record PreflopNodeTraceActionDto(
    Guid PlayerId,
    Position? Position,
    string ActionType,
    decimal AmountBb);
