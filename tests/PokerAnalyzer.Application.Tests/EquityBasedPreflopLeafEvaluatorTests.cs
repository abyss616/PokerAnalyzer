using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class EquityBasedPreflopLeafEvaluatorTests
{
    [Theory]
    [InlineData("v2/UNOPENED/BTN/eff=100", PreflopNodeFamily.Unopened)]
    [InlineData("v2/VS_OPEN/BTN/eff=100", PreflopNodeFamily.FacingRaise)]
    [InlineData("v2/VS_3BET/BTN/eff=100", PreflopNodeFamily.Facing3Bet)]
    public void Evaluate_UsesSameEvaluatorPathAcrossFamilies(string solverKey, PreflopNodeFamily expectedFamily)
    {
        var fallback = new HeuristicPreflopLeafEvaluator();
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), fallback, samplesPerMatchup: 120);
        var context = CreateHeadsUpContext(HoleCards.Parse("AsKh"), HoleCards.Parse("QdJd"), ActionType.Raise, solverKey);

        var result = evaluator.Evaluate(context);

        Assert.Contains("equity leaf evaluator:", result.Reason);
        Assert.Contains($"family={expectedFamily}", result.Reason);
        Assert.DoesNotContain("fallback", result.Reason);
        Assert.NotNull(result.Details);
        Assert.True(result.Details!.UsedEquityEvaluator);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.Equal("TrueHeadsUp", result.Details.EvaluatorType);
        Assert.Equal(expectedFamily.ToString(), result.Details.NodeFamily);
        Assert.Equal("BTN", result.Details.HeroPosition);
        Assert.Equal("BB", result.Details.VillainPosition);
        Assert.NotNull(result.Details.HeroEquity);
        Assert.NotNull(result.Details.HeroUtility);
        Assert.Equal("AKo", result.Details.HeroHand);
        Assert.NotNull(result.Details.HandClass);
        Assert.NotNull(result.Details.RationaleSummary);
    }

    [Fact]
    public void Evaluate_AppliesBlockerFiltering_BeforeEquity()
    {
        var provider = new StaticOpponentRangeProvider(
            new WeightedHoleCards(HoleCards.Parse("AsAd"), 1d),
            new WeightedHoleCards(HoleCards.Parse("KdQd"), 1d));

        var evaluator = new EquityBasedPreflopLeafEvaluator(provider, new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateHeadsUpContext(HoleCards.Parse("AsKh"), HoleCards.Parse("QcJc"), ActionType.Raise, "v2/UNOPENED/BTN/eff=100");

        var result = evaluator.Evaluate(context);

        Assert.Contains("filteredCombos=1", result.Reason);
        Assert.DoesNotContain("fallback", result.Reason);
        Assert.NotNull(result.Details);
        Assert.Equal(1, result.Details!.FilteredCombos);
        Assert.Equal("static-test", result.Details.RangeDescription);
        Assert.Equal("static-test", result.Details.RangeDetail);
    }


    [Fact]
    public void Evaluate_UnopenedBtnMultiway_UsesAbstractedHeadsUp()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("AsKh"));

        var result = evaluator.Evaluate(context);

        Assert.NotNull(result.Details);
        Assert.Equal("AbstractedHeadsUp", result.Details!.EvaluatorType);
        Assert.True(result.Details.UsedEquityEvaluator);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.Equal("WeightedBlindsBTNUnopened", result.Details.AbstractionSource);
        Assert.Equal(2, result.Details.ActualActiveOpponentCount);
        Assert.Equal(1, result.Details.AbstractedOpponentCount);
        Assert.NotNull(result.Details.FoldProbability);
        Assert.NotNull(result.Details.ContinueProbability);
    }

    [Fact]
    public void Evaluate_UnopenedBtnMultiway_SeparatesDifferentHands()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var j9Result = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Jc9d")));
        var q4Result = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Qc4d")));

        Assert.NotNull(j9Result.Details);
        Assert.NotNull(q4Result.Details);
        Assert.Equal("AbstractedHeadsUp", j9Result.Details!.EvaluatorType);
        Assert.Equal("AbstractedHeadsUp", q4Result.Details!.EvaluatorType);
        Assert.NotEqual(j9Result.Details.HeroUtility, q4Result.Details.HeroUtility);
        Assert.NotEqual(j9Result.Details.HeroEquity, q4Result.Details.HeroEquity);
    }

    [Fact]
    public void Evaluate_MultiwayContext_FallsBackToHeuristic()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateThreeWayContext();

        var result = evaluator.Evaluate(context);

        Assert.Contains("equity evaluator fallback", result.Reason);
        Assert.Contains("heuristic preflop", result.Reason);
        Assert.NotNull(result.Details);
        Assert.True(result.Details!.UsedFallbackEvaluator);
        Assert.Equal("HeuristicFallback", result.Details.EvaluatorType);
        Assert.Equal("AKo", result.Details.HeroHand);
        Assert.NotNull(result.Details.FallbackReason);
        Assert.Contains("expected heads-up", result.Details.FallbackReason!);
    }

    [Fact]
    public void TableDrivenOpponentRangeProvider_CachesBuiltRanges_ByContext()
    {
        var provider = new TableDrivenOpponentRangeProvider();
        var request = new OpponentRangeRequest(
            Position.BTN,
            Position.BB,
            PreflopNodeFamily.FacingRaise,
            RaiseDepth: 1,
            IsHeadsUp: true,
            SolverKey: "v2/VS_OPEN/BTN/eff=100");

        var first = provider.TryGetRange(request, out var rangeA, out _);
        var second = provider.TryGetRange(request, out var rangeB, out _);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, provider.RangeBuildCount);
        Assert.Equal(1, provider.CachedRangeCount);
        Assert.Same(rangeA.WeightedCombos, rangeB.WeightedCombos);
    }

    [Fact]
    public void DeterministicPreflopEquity_CachesCanonicalMatchups()
    {
        var hero = HoleCards.Parse("AsKh");
        var villain = HoleCards.Parse("QdJd");
        var samples = 96;

        var before = DeterministicPreflopEquity.MatchupComputationCount;
        var heroVsVillain = DeterministicPreflopEquity.CalculateHeadsUpEquity(hero, villain, samples);
        var villainVsHero = DeterministicPreflopEquity.CalculateHeadsUpEquity(villain, hero, samples);
        var after = DeterministicPreflopEquity.MatchupComputationCount;

        Assert.Equal(before + 1, after);
        Assert.InRange(Math.Abs((heroVsVillain + villainVsHero) - 1d), 0d, 0.0000001d);
    }

    private static PreflopLeafEvaluationContext CreateHeadsUpContext(HoleCards heroCards, HoleCards villainCards, ActionType rootAction, string solverKey)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var villainId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var config = new GameConfig(2, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(villainId, 1, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var root = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(200),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 1,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(villainId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [villainId] = villainCards
            });

        return new PreflopLeafEvaluationContext(
            root,
            root,
            heroId,
            Position.BTN,
            heroCards,
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? new ChipAmount(250) : ChipAmount.Zero),
            solverKey);
    }

    private static PreflopLeafEvaluationContext CreateThreeWayContext(string solverKey = "v2/VS_OPEN/BTN/eff=100", HoleCards? heroCards = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var v1 = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var v2 = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var config = new GameConfig(3, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(v1, 1, Position.SB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(v2, 2, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(300),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 1,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(v1, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(v2, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards ?? HoleCards.Parse("AsKh"),
                [v1] = HoleCards.Parse("QdJd"),
                [v2] = HoleCards.Parse("9c9d")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.BTN,
            heroCards ?? HoleCards.Parse("AsKh"),
            100,
            new LegalAction(ActionType.Raise, new ChipAmount(250)),
            solverKey);
    }

    private sealed class StaticOpponentRangeProvider : IOpponentRangeProvider
    {
        private readonly WeightedHoleCards[] _combos;

        public StaticOpponentRangeProvider(params WeightedHoleCards[] combos)
        {
            _combos = combos;
        }

        public bool TryGetRange(OpponentRangeRequest request, out OpponentWeightedRange range, out string reason)
        {
            range = new OpponentWeightedRange(_combos, "static-test");
            reason = "static-test";
            return true;
        }
    }
}
