using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.HandHistories;
using PokerAnalyzer.Infrastructure.PreflopAnalysis;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class PreflopHandAnalysisServiceTests
{
    [Fact]
    public async Task QueryPreflopNodeByHandNumberAsync_ReturnsNull_WhenHandNotFound()
    {
        var svc = BuildService(hand: null);

        var result = await svc.QueryPreflopNodeByHandNumberAsync(123, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task QueryPreflopNodeByHandNumberAsync_MapsExactState_ToExpectedCanonicalKey()
    {
        var hand = BuildStandardHeroFacingOpenHand();
        var svc = BuildService(hand, strategyProvider: new TestStrategyProvider(new Dictionary<string, decimal>
        {
            ["Fold"] = 0.20m,
            ["Call:1.5"] = 0.30m,
            ["Raise:4"] = 0.50m
        }));

        var result = await svc.QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsSupported);
        Assert.Equal("preflop/v2/VS_OPEN/UTG/eff=100/open=2.5/jam=18", result.CanonicalKey);
        Assert.Equal("VS_OPEN", result.HistorySignature);
    }

    [Fact]
    public async Task QueryPreflopNodeByHandNumberAsync_DifferentHistories_DoNotCollapseToFallbackNode()
    {
        var openResult = await BuildService(BuildStandardHeroFacingOpenHand())
            .QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        var limpedResult = await BuildService(BuildLimpedPotHand())
            .QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(openResult);
        Assert.NotNull(limpedResult);
        Assert.True(openResult!.IsSupported);
        Assert.True(limpedResult!.IsSupported);
        Assert.NotEqual(openResult.SolverKey, limpedResult.SolverKey);
        Assert.NotEqual(openResult.HistorySignature, limpedResult.HistorySignature);
    }

    [Fact]
    public async Task QueryPreflopNodeByHandNumberAsync_PreservesSizedRaiseActions_EndToEnd()
    {
        var hand = BuildStandardHeroFacingOpenHand();
        var strategyProvider = new TestStrategyProvider(new Dictionary<string, decimal>
        {
            ["Fold"] = 0.10m,
            ["Call:1.5"] = 0.20m,
            ["Raise:4"] = 0.30m,
            ["Raise:9"] = 0.40m
        });

        var result = await BuildService(hand, strategyProvider).QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result!.LegalActions, x => x.ActionKey == "Raise:4");
        Assert.Contains(result.LegalActions, x => x.ActionKey == "Raise:9");
        Assert.Contains(result.Strategy, x => x.ActionKey == "Raise:4");
        Assert.Contains(result.Strategy, x => x.ActionKey == "Raise:9");
    }

    [Fact]
    public async Task QueryPreflopNodeByHandNumberAsync_ReturnsUnsupportedSpot_WithReason()
    {
        var hand = BuildHandWithoutHero();

        var result = await BuildService(hand).QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.IsSupported);
        Assert.NotNull(result.UnsupportedReason);
    }

    [Fact]
    public async Task QueryPreflopNodeByHandNumberAsync_UnopenedSpot_UsesSolverUnopenedLegalActions()
    {
        var hand = BuildUnopenedHeroDecisionHand();

        var result = await BuildService(hand).QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsSupported);
        Assert.Equal("UNOPENED", result.HistorySignature);
        Assert.Equal(
            ["Fold", "Call:1", "Raise:2.5"],
            result.LegalActions.Select(a => a.ActionKey).ToArray());
    }

    [Fact]
    public async Task QueryPreflopNodeByHandNumberAsync_UsesLiveSolverAndReturnsSolveMetadata()
    {
        var hand = BuildStandardHeroFacingOpenHand();
        var liveProvider = new LivePreflopSolveService(
            new InMemoryRegretStore(),
            new InMemoryAverageStrategyStore(),
            new InMemoryPreflopTrainingProgressStore(),
            new PreflopInfoSetMapper());

        var result = await BuildService(hand, liveProvider).QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsSupported);
        Assert.Equal("LiveSolved", result.SolveMetadata.StrategySource);
        Assert.True(result.SolveMetadata.IterationsCompleted > 0);
        Assert.NotEmpty(result.Strategy);
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
                new HandPlayer { Id = Guid.NewGuid(), Name = "SB", Seat = 1, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "BB", Seat = 2, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "Villain", Seat = 5, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "Hero", Seat = 6, StackStart = 100m, IsHero = true }
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

    private static Hand BuildLimpedPotHand()
    {
        return new Hand
        {
            GameCode = 9003,
            Players =
            [
                new HandPlayer { Id = Guid.NewGuid(), Name = "SB", Seat = 1, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "BB", Seat = 2, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "Villain", Seat = 5, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "Hero", Seat = 6, StackStart = 100m, IsHero = true }
            ],
            Actions =
            [
                new HandAction { Street = Street.Preflop, Player = "SB", Type = ActionType.PostSmallBlind, Amount = 0.5m },
                new HandAction { Street = Street.Preflop, Player = "BB", Type = ActionType.PostBigBlind, Amount = 1m },
                new HandAction { Street = Street.Preflop, Player = "Villain", Type = ActionType.Call, Amount = 1m },
                new HandAction { Street = Street.Preflop, Player = "Hero", Type = ActionType.Check, Amount = 0m }
            ]
        };
    }

    private static Hand BuildUnopenedHeroDecisionHand()
    {
        return new Hand
        {
            GameCode = 9004,
            Players =
            [
                new HandPlayer { Id = Guid.NewGuid(), Name = "Hero", Seat = 1, StackStart = 100m, IsHero = true },
                new HandPlayer { Id = Guid.NewGuid(), Name = "Villain1", Seat = 2, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "SB", Seat = 3, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "BB", Seat = 4, StackStart = 100m, IsHero = false }
            ],
            Actions =
            [
                new HandAction { Street = Street.Preflop, Player = "SB", Type = ActionType.PostSmallBlind, Amount = 0.5m },
                new HandAction { Street = Street.Preflop, Player = "BB", Type = ActionType.PostBigBlind, Amount = 1m },
                new HandAction { Street = Street.Preflop, Player = "Hero", Type = ActionType.Call, Amount = 1m }
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

        public Task<PreflopStrategyResultDto?> GetStrategyResultAsync(PreflopStrategyRequestDto request, CancellationToken ct)
        {
            if (_strategy is null)
                return Task.FromResult<PreflopStrategyResultDto?>(null);

            var selected = _strategy
                .Where(kv => request.LegalActions.Select(ToActionKey).Contains(kv.Key, StringComparer.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            return Task.FromResult<PreflopStrategyResultDto?>(new PreflopStrategyResultDto(request.SolverKey, selected, 0, 0d, "TestProvider", 0, "None"));
        }

        private static string ToActionKey(LegalAction action)
            => action.Amount is null ? action.ActionType.ToString() : $"{action.ActionType}:{action.Amount.Value.Value / 100m:0.##}";
    }
}
