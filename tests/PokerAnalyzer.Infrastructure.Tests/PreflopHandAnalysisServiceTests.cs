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
    public async Task QueryPreflopNodeByHandNumberAsync_OrdersActionsBySequenceNumber()
    {
        var hand = BuildStandardHeroFacingOpenHand();
        hand.Actions =
        [
            new HandAction { SequenceNumber = 3, Street = Street.Preflop, Player = "Hero", Type = ActionType.Call, Amount = 2.5m },
            new HandAction { SequenceNumber = 2, Street = Street.Preflop, Player = "Villain", Type = ActionType.Raise, Amount = 2.5m },
            new HandAction { SequenceNumber = 1, Street = Street.Preflop, Player = "BB", Type = ActionType.PostBigBlind, Amount = 1m },
            new HandAction { SequenceNumber = 0, Street = Street.Preflop, Player = "SB", Type = ActionType.PostSmallBlind, Amount = 0.5m }
        ];

        var result = await BuildService(hand).QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsSupported);
        Assert.Equal("VS_OPEN", result.HistorySignature);
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
        string[] expected = { "Fold", "Call:1", "Raise:2.5" };

        Assert.Equal(
            expected,
            result.LegalActions.Select(a => a.ActionKey).ToArray());
    }


    [Fact]
    public async Task QueryPreflopNodeByHandNumberAsync_MapsLeafEvaluationDetailsIntoSolveMetadata()
    {
        var hand = BuildStandardHeroFacingOpenHand();
        var details = new PreflopLeafEvaluationDetailsDto(
            true,
            false,
            "EquityBased",
            "FacingRaise",
            "BTN",
            "BB",
            true,
            "FacingRaise",
            "table-range percentile=0.18",
            120,
            0.57,
            0.14,
            null,
            "level-2 test summary");

        var strategyProvider = new TestStrategyProvider(new Dictionary<string, decimal> { ["Fold"] = 0.1m }, details);
        var result = await BuildService(hand, strategyProvider).QueryPreflopNodeByHandNumberAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.SolveMetadata.LeafEvaluationDetails);
        Assert.Equal("EquityBased", result.SolveMetadata.LeafEvaluationDetails!.EvaluatorType);
        Assert.Equal(0.57, result.SolveMetadata.LeafEvaluationDetails.HeroEquity);
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
        return WithSequenceNumbers(new Hand
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
        });
    }

    private static Hand BuildStandardHeroFacingOpenHand()
    {
        return WithSequenceNumbers(new Hand
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
        });
    }

    private static Hand BuildLimpedPotHand()
    {
        return WithSequenceNumbers(new Hand
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
        });
    }

    private static Hand BuildUnopenedHeroDecisionHand()
    {
        return WithSequenceNumbers(new Hand
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
        });
    }

    private static Hand BuildUnopenedHeroDecisionHandWithCentStacks()
    {
        return WithSequenceNumbers(new Hand
        {
            GameCode = 9005,
            Players =
            [
                new HandPlayer { Id = Guid.NewGuid(), Name = "UTG", Seat = 1, StackStart = 10699.98m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "Hero", Seat = 2, StackStart = 10700.00m, IsHero = true },
                new HandPlayer { Id = Guid.NewGuid(), Name = "SB", Seat = 3, StackStart = 10500.00m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "BB", Seat = 4, StackStart = 10800.00m, IsHero = false }
            ],
            Actions =
            [
                new HandAction { Street = Street.Preflop, Player = "SB", Type = ActionType.PostSmallBlind, Amount = 0.5m },
                new HandAction { Street = Street.Preflop, Player = "BB", Type = ActionType.PostBigBlind, Amount = 1m },
                new HandAction { Street = Street.Preflop, Player = "Hero", Type = ActionType.Call, Amount = 1m }
            ]
        });
    }

    private static Hand BuildSeatWraparoundUnopenedHand()
    {
        return WithSequenceNumbers(new Hand
        {
            GameCode = 9006,
            Players =
            [
                new HandPlayer { Id = Guid.NewGuid(), Name = "Hero", Seat = 1, StackStart = 100m, IsHero = true },
                new HandPlayer { Id = Guid.NewGuid(), Name = "BTN", Seat = 3, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "SB", Seat = 7, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "BB", Seat = 9, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "UTG", Seat = 10, StackStart = 100m, IsHero = false },
                new HandPlayer { Id = Guid.NewGuid(), Name = "HJ", Seat = 12, StackStart = 100m, IsHero = false }
            ],
            Actions =
            [
                new HandAction { Street = Street.Preflop, Player = "SB", Type = ActionType.PostSmallBlind, Amount = 0.5m },
                new HandAction { Street = Street.Preflop, Player = "BB", Type = ActionType.PostBigBlind, Amount = 1m },
                new HandAction { Street = Street.Preflop, Player = "Hero", Type = ActionType.Call, Amount = 1m }
            ]
        });
    }

    private static Hand WithSequenceNumbers(Hand hand)
    {
        for (var i = 0; i < hand.Actions.Count; i++)
            hand.Actions[i].SequenceNumber = i;

        return hand;
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
        private readonly PreflopLeafEvaluationDetailsDto? _details;

        public TestStrategyProvider(IReadOnlyDictionary<string, decimal>? strategy, PreflopLeafEvaluationDetailsDto? details = null)
        {
            _strategy = strategy;
            _details = details;
        }

        public Task<PreflopStrategyResultDto?> GetStrategyResultAsync(PreflopStrategyRequestDto request, CancellationToken ct)
        {
            if (_strategy is null)
                return Task.FromResult<PreflopStrategyResultDto?>(null);

            var selected = _strategy
                .Where(kv => request.LegalActions.Select(ToActionKey).Contains(kv.Key, StringComparer.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            return Task.FromResult<PreflopStrategyResultDto?>(new PreflopStrategyResultDto(request.SolverKey, selected, 0, 0d, "TestProvider", 0, "None", _details));
        }

        private static string ToActionKey(LegalAction action)
            => action.Amount is null ? action.ActionType.ToString() : $"{action.ActionType}:{action.Amount.Value.Value / 100m:0.##}";
    }
}
