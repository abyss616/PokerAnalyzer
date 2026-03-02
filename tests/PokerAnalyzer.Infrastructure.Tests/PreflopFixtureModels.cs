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

public sealed class ExpectedBuckets
{
    [JsonPropertyName("openSizeBucketBb")]
    public decimal? OpenSizeBucketBb { get; init; }

    [JsonPropertyName("isoSizeBucketBb")]
    public decimal? IsoSizeBucketBb { get; init; }

    [JsonPropertyName("threeBetSizeBucketBb")]
    public decimal? ThreeBetSizeBucketBb { get; init; }

    [JsonPropertyName("squeezeSizeBucketBb")]
    public decimal? SqueezeSizeBucketBb { get; init; }

    [JsonPropertyName("fourBetSizeBucketBb")]
    public decimal? FourBetSizeBucketBb { get; init; }

    [JsonPropertyName("jamThresholdBucketBb")]
    public decimal? JamThresholdBucketBb { get; init; }
}
