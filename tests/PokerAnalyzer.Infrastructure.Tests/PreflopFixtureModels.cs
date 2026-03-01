using System.Text.Json.Serialization;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class PreflopPlayersFixture
{
    public required PreflopTableInfo Table { get; init; }
    public required List<PreflopSeatFixture> Seats { get; init; }
}

public sealed class PreflopTableInfo
{
    public required decimal SmallBlind { get; init; }
    public required decimal BigBlind { get; init; }
    public required int ButtonSeat { get; init; }
}

public sealed class PreflopSeatFixture
{
    public required string PlayerId { get; init; }
    public required int Seat { get; init; }
    public required long StackChips { get; init; }
    public required string Position { get; init; }
    public required bool IsHero { get; init; }
}

public sealed class PreflopActionsFixture
{
    public required List<PreflopActionFixture> Actions { get; init; }
}

public sealed class PreflopActionFixture
{
    public required string Actor { get; init; }
    public required string Type { get; init; }
    public required decimal AmountBb { get; init; }
}

public sealed class PreflopExpectedFixture
{
    public required ExpectedExtraction ExpectedExtraction { get; init; }
}

public sealed class ExpectedExtraction
{
    public required string ActingPosition { get; init; }
    public string? FacingPosition { get; init; }
    public required string HistorySignature { get; init; }
    public required int RaiseDepth { get; init; }
    public required decimal ToCallBb { get; init; }
    public required decimal EffectiveStackBb { get; init; }
    public required ExpectedBuckets Buckets { get; init; }
    public required string SolverKey { get; init; }
}

/// <summary>
/// Bucket completeness convention for preflop fixtures:
/// - OPEN/UNOPENED_SB: <see cref="OpenSizeBucket"/> set when there is a live opening size; others are "NA".
/// - VS_OPEN: <see cref="OpenSizeBucket"/> reflects the open that hero faces; 3B/4B are "NA".
/// - VS_3BET: <see cref="ThreeBetSizeBucket"/> must be set.
/// - VS_4BET and deeper: <see cref="FourBetSizeBucket"/> must be set.
/// - Non-participating buckets are encoded as "NA" (not null) for deterministic assertions.
/// - <see cref="JamThreshold"/> must always be present because it influences solver behavior.
/// </summary>
public sealed class ExpectedBuckets
{
    [JsonPropertyName("openSizeBucket")]
    public required string OpenSizeBucket { get; init; }

    [JsonPropertyName("isoSizeBucket")]
    public required string IsoSizeBucket { get; init; }

    [JsonPropertyName("threeBetSizeBucket")]
    public required string ThreeBetSizeBucket { get; init; }

    [JsonPropertyName("squeezeSizeBucket")]
    public required string SqueezeSizeBucket { get; init; }

    [JsonPropertyName("fourBetSizeBucket")]
    public required string FourBetSizeBucket { get; init; }

    [JsonPropertyName("jamThreshold")]
    public required decimal JamThreshold { get; init; }
}
