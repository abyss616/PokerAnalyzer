using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class AllInEquityCalculatorTests
{
    private readonly AllInEquityCalculator _sut = new();

    [Fact]
    public async Task Exact_Hu_AA_vs_KK()
    {
        var p1 = PlayerId.New();
        var p2 = PlayerId.New();
        var players = new List<(PlayerId, HoleCards)>
        {
            (p1, HoleCards.Parse("AsAh")),
            (p2, HoleCards.Parse("KsKh"))
        };

        var result = await _sut.ComputePreflopAsync(players, null, null, CancellationToken.None);

        Assert.Equal("Exact", result.Method);
        Assert.InRange(result.Equities[p1], 0.817m, 0.827m);
        Assert.InRange(result.Equities[p2], 0.170m, 0.183m);
        Assert.InRange(result.Equities.Values.Sum(), 0.999999m, 1.000001m);
    }

    [Fact]
    public async Task Exact_Hu_TieHeavy_SameRanks_SplitsCorrectly()
    {
        var p1 = PlayerId.New();
        var p2 = PlayerId.New();
        var players = new List<(PlayerId, HoleCards)>
        {
            (p1, HoleCards.Parse("AhKh")),
            (p2, HoleCards.Parse("AdKd"))
        };

        var result = await _sut.ComputePreflopAsync(players, null, null, CancellationToken.None);

        Assert.InRange(result.Equities[p1], 0.48m, 0.52m);
        Assert.InRange(result.Equities[p2], 0.48m, 0.52m);
        Assert.InRange(result.Equities.Values.Sum(), 0.999999m, 1.000001m);
    }

    [Fact]
 
    public async Task MonteCarlo_Multiway_IsDeterministic_WithSeed()
    {
        // Arrange
        var p1 = PlayerId.New();
        var p2 = PlayerId.New();
        var p3 = PlayerId.New();

        var players = new List<(PlayerId, HoleCards)>
    {
        (p1, HoleCards.Parse("AsKd")), // AKo
        (p2, HoleCards.Parse("QhQc")), // QQ
        (p3, HoleCards.Parse("9s9d"))  // 99
    };

        const int samples = 100_000;
        const int seed = 12345;

        // Act
        var result1 = await _sut.ComputePreflopAsync(players, samples, seed, CancellationToken.None);
        var result2 = await _sut.ComputePreflopAsync(players, samples, seed, CancellationToken.None);

        // Assert: metadata
        Assert.Equal("MonteCarlo", result1.Method);
        Assert.Equal(samples, result1.SamplesUsed);
        Assert.Equal(seed, result1.SeedUsed);

        // Assert: determinism (same seed => identical results)
        Assert.Equal(result1.Equities[p1], result2.Equities[p1]);
        Assert.Equal(result1.Equities[p2], result2.Equities[p2]);
        Assert.Equal(result1.Equities[p3], result2.Equities[p3]);

        // Assert: equities sum to ~1
        var totalEquity = result1.Equities.Values.Sum();
        Assert.InRange(totalEquity, 0.999m, 1.001m);

        // Assert: sanity ranges (based on true multiway equities)
        Assert.InRange(result1.Equities[p2], 0.43m, 0.48m); // QQ ≈ mid 40%s
        Assert.InRange(result1.Equities[p1], 0.275m,0.375m); // AKo ≈ low 30%s
        Assert.InRange(result1.Equities[p3], 0.17m, 0.26m); // 99 ≈ low-mid 20%s
    }

    [Fact]
    public async Task DuplicateCards_ThrowsClearMessage()
    {
        var players = new List<(PlayerId, HoleCards)>
        {
            (PlayerId.New(), HoleCards.Parse("AsAh")),
            (PlayerId.New(), HoleCards.Parse("AsKd"))
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ComputePreflopAsync(players, null, null, CancellationToken.None));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MonteCarlo_HonorsCancellation()
    {
        var players = new List<(PlayerId, HoleCards)>
        {
            (PlayerId.New(), HoleCards.Parse("AsKd")),
            (PlayerId.New(), HoleCards.Parse("QhQc")),
            (PlayerId.New(), HoleCards.Parse("9s9d"))
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _sut.ComputePreflopAsync(players, 5_000_000, 7, cts.Token));
    }
}
