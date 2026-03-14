using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class PreflopQueryTrace
{
    public required PlayerId ActingPlayerId { get; init; }
    public required Position ActingPosition { get; init; }
    public PlayerId? FacingPlayerId { get; init; }
    public Position? FacingPosition { get; init; }
    public required string HistorySignature { get; init; }
    public required int RaiseDepth { get; init; }
    public required decimal ToCallBb { get; init; }
    public required decimal CurrentBetBb { get; init; }
    public required decimal ActingContribBb { get; init; }
    public required decimal PotBb { get; init; }
    public required decimal EffectiveStackBb { get; init; }
    public required string OpenSizeBucket { get; init; }
    public required string IsoSizeBucket { get; init; }
    public required string ThreeBetBucket { get; init; }
    public required string SqueezeBucket { get; init; }
    public required string FourBetBucket { get; init; }
    public required decimal JamThreshold { get; init; }
    public required string SolverKey { get; init; }
    public required IReadOnlyList<PreflopRawActionTrace> RawActionHistory { get; init; }
    public required IReadOnlyList<PreflopRawActionTrace> PriorActionsBeforeActing { get; init; }
    public required bool HadPriorCallOrCompletion { get; init; }
    public string? ActingPlayersFirstActionType { get; init; }
}

public sealed record PreflopRawActionTrace(
    Street Street,
    PlayerId PlayerId,
    Position? Position,
    string ActionType,
    decimal AmountChips,
    decimal AmountBb);
