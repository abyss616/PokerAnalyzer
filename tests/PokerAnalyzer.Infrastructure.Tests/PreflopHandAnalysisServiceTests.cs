using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.HandHistories;
using PokerAnalyzer.Infrastructure.PreflopAnalysis;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class PreflopHandAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzePreflopByHandNumberAsync_ReturnsNull_WhenHandNotFound()
    {
        var svc = BuildService(hand: null);

        var result = await svc.AnalyzePreflopByHandNumberAsync(123, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AnalyzePreflopByHandNumberAsync_ReturnsNotYetImplemented_WhenMappingUnsupported()
    {
        var hand = BuildHandWithoutHero();
        var svc = BuildService(hand);

        var result = await svc.AnalyzePreflopByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(PreflopHandAnalysisResultDto.NotYetImplemented, result!.CanonicalPreflopSolverNode);
        Assert.Equal(PreflopHandAnalysisResultDto.NotYetImplemented, result.HeroLegalActions);
    }

    [Fact]
    public async Task AnalyzePreflopByHandNumberAsync_ReturnsLegalActions_WhenExtractionWorks()
    {
        var hand = BuildStandardHeroFacingOpenHand();
        var svc = BuildService(hand);

        var result = await svc.AnalyzePreflopByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Fold", result!.HeroLegalActions);
        Assert.Contains("Call", result.HeroLegalActions);
        Assert.Contains("Raise", result.HeroLegalActions);
    }

    [Fact]
    public async Task AnalyzePreflopByHandNumberAsync_ReturnsNotYetImplementedMixedStrategy_WhenNoPolicyExists()
    {
        var hand = BuildStandardHeroFacingOpenHand();
        var svc = BuildService(hand, strategyProvider: new TestStrategyProvider(null));

        var result = await svc.AnalyzePreflopByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(PreflopHandAnalysisResultDto.NotYetImplemented, result!.CurrentMixedStrategy);
    }

    [Fact]
    public async Task AnalyzePreflopByHandNumberAsync_ReturnsSensibleFallbackComparison_WhenNoPolicyExists()
    {
        var hand = BuildStandardHeroFacingOpenHand();
        var svc = BuildService(hand, strategyProvider: new TestStrategyProvider(null));

        var result = await svc.AnalyzePreflopByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Hero Call", result!.ActualActionVsRecommendation);
        Assert.Contains(PreflopHandAnalysisResultDto.NotYetImplemented, result.ActualActionVsRecommendation);
    }

    private static PreflopHandAnalysisService BuildService(Hand? hand, IPreflopStrategyProvider? strategyProvider = null)
    {
        var repo = new TestRepo(hand);
        return new PreflopHandAnalysisService(
            repo,
            new PreflopStateExtractor(),
            strategyProvider ?? new TestStrategyProvider(null));
    }

    private static Hand BuildHandWithoutHero()
    {
        return new Hand
        {
            GameCode = 9001,
            Players =
            [
                new HandPlayer { Id = Guid.NewGuid(), Name = "Villain", Seat = 1, StackStart = 100m, IsHero = false }
            ],
            Actions =
            [
                new HandAction { Street = Street.Preflop, Player = "Villain", Type = ActionType.PostBigBlind, Amount = 1m }
            ]
        };
    }

    private static Hand BuildStandardHeroFacingOpenHand()
    {
        return new Hand
        {
            GameCode = 9002,
            Players =
            [
                new HandPlayer { Id = Guid.NewGuid(), Name = "Hero", Seat = 6, StackStart = 100m, IsHero = true },
                new HandPlayer { Id = Guid.NewGuid(), Name = "Villain", Seat = 5, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "SB", Seat = 1, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "BB", Seat = 2, StackStart = 100m, IsHero = false }
            ],
            Actions =
            [
                new HandAction { Street = Street.Preflop, Player = "SB", Type = ActionType.PostSmallBlind, Amount = 0.5m },
                new HandAction { Street = Street.Preflop, Player = "BB", Type = ActionType.PostBigBlind, Amount = 1m },
                new HandAction { Street = Street.Preflop, Player = "Villain", Type = ActionType.Raise, Amount = 2.5m },
                new HandAction { Street = Street.Preflop, Player = "Hero", Type = ActionType.Call, Amount = 2.5m }
            ]
        };
    }

    private sealed class TestRepo : IHandHistoryRepository
    {
        private readonly Hand? _hand;

        public TestRepo(Hand? hand)
        {
            _hand = hand;
        }

        public Task<HandHistorySession?> GetSessionAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<HandHistorySession?>(null);

        public Task<Hand?> GetHandAsync(Guid handId, CancellationToken ct) =>
            Task.FromResult<Hand?>(_hand);

        public Task<Hand?> GetHandByGameCodeAsync(long handNumber, CancellationToken ct) =>
            Task.FromResult(handNumber == 1 ? _hand : null);
    }

    private sealed class TestStrategyProvider : IPreflopStrategyProvider
    {
        private readonly IReadOnlyDictionary<string, decimal>? _strategy;

        public TestStrategyProvider(IReadOnlyDictionary<string, decimal>? strategy)
        {
            _strategy = strategy;
        }

        public Task<PreflopStrategyResultDto?> GetStrategyResultAsync(string solverKey, IReadOnlyList<string> legalActions, CancellationToken ct)
            => Task.FromResult(_strategy is null
                ? null
                : new PreflopStrategyResultDto(solverKey, _strategy, 0, 0d));
    }
}
