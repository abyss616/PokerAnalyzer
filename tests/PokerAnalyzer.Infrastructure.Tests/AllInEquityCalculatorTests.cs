using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

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
        Assert.InRange(result.Equities[p1], 0.817m, 0.821m);
        Assert.InRange(result.Equities[p2], 0.179m, 0.183m);
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
        var p1 = PlayerId.New();
        var p2 = PlayerId.New();
        var p3 = PlayerId.New();
        var players = new List<(PlayerId, HoleCards)>
        {
            (p1, HoleCards.Parse("AsKd")),
            (p2, HoleCards.Parse("QhQc")),
            (p3, HoleCards.Parse("9s9d"))
        };

        var result = await _sut.ComputePreflopAsync(players, 100_000, 12345, CancellationToken.None);

        Assert.Equal("MonteCarlo", result.Method);
        Assert.Equal(100_000, result.SamplesUsed);
        Assert.Equal(12345, result.SeedUsed);
        Assert.InRange(result.Equities.Values.Sum(), 0.999m, 1.001m);
        Assert.InRange(result.Equities[p2], 0.40m, 0.45m);
        Assert.InRange(result.Equities[p3], 0.20m, 0.25m);
        Assert.InRange(result.Equities[p1], 0.30m, 0.35m);
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
