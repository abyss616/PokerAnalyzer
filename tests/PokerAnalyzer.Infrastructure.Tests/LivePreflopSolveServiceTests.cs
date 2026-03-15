using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopAnalysis;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class LivePreflopSolveServiceTests
{
    [Fact]
    public async Task GetStrategyResultAsync_DefaultMode_DoesNotPersistTrainingState()
    {
        var sharedRegrets = new InMemoryRegretStore();
        var sharedAverage = new InMemoryAverageStrategyStore();
        var sharedProgress = new InMemoryPreflopTrainingProgressStore();
        var sut = new LivePreflopSolveService(sharedRegrets, sharedAverage, sharedProgress, new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2:test",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var first = await sut.GetStrategyResultAsync(request, CancellationToken.None);
        var second = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Fresh", first!.SolveMode);
        Assert.Equal("Fresh", second!.SolveMode);
        Assert.Equal(0, sharedProgress.TotalIterationsCompleted);

        foreach (var action in request.LegalActions)
        {
            Assert.Equal(0d, sharedRegrets.Get(request.SolverKey, action));
            Assert.Equal(0d, sharedAverage.Get(request.SolverKey, action));
        }
    }

    [Fact]
    public async Task GetStrategyResultAsync_PersistentMode_PersistsTrainingState()
    {
        var sharedRegrets = new InMemoryRegretStore();
        var sharedAverage = new InMemoryAverageStrategyStore();
        var sharedProgress = new InMemoryPreflopTrainingProgressStore();
        var sut = new LivePreflopSolveService(sharedRegrets, sharedAverage, sharedProgress, new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2:test",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))],
            UsePersistentTrainingState: true);

        var first = await sut.GetStrategyResultAsync(request, CancellationToken.None);
        var second = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Persistent", first!.SolveMode);
        Assert.Equal("Persistent", second!.SolveMode);
        Assert.True(sharedProgress.TotalIterationsCompleted > 0);

        var hasStoredState = request.LegalActions.Any(action =>
            sharedRegrets.Get(request.SolverKey, action) != 0d ||
            sharedAverage.Get(request.SolverKey, action) != 0d);

        Assert.True(hasStoredState);
    }


    [Fact]
    public async Task GetStrategyResultAsync_FreshMode_UsesConfiguredMultiRunIterationBudget()
    {
        var sut = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), new InMemoryPreflopTrainingProgressStore(), new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2:test:multi",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var result = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Fresh", result!.SolveMode);
        Assert.Equal(600, result.IterationsCompleted);
    }

    [Fact]
    public async Task GetStrategyResultAsync_PersistentMode_KeepsSingleRunIterationBudget()
    {
        var progress = new InMemoryPreflopTrainingProgressStore();
        var sut = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), progress, new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2:test:persistent",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))],
            UsePersistentTrainingState: true);

        var result = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Persistent", result!.SolveMode);
        Assert.Equal(300, result.IterationsCompleted);
        Assert.Equal(300, progress.TotalIterationsCompleted);
    }

    [Fact]
    public async Task GetStrategyResultAsync_RecommendationIsDerivedFromReturnedAveragedFrequencies()
    {
        var sut = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), new InMemoryPreflopTrainingProgressStore(), new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2:test:best",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var result = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.ActionDiagnostics);
        var bestByFlag = result.ActionDiagnostics!.Single(x => x.IsBestByFrequency);
        var bestByReturnedAverage = result.AverageStrategy
            .OrderByDescending(kvp => kvp.Value)
            .First()
            .Key;

        Assert.Equal(bestByReturnedAverage, bestByFlag.ActionKey);
    }


    [Fact]
    public async Task GetStrategyResultAsync_MapsLeafEvaluatorMetadata()
    {
        var sut = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), new InMemoryPreflopTrainingProgressStore(), new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2/UNOPENED/BTN/eff=100",
            CreateBtnThreeWayRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var result = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.LeafEvaluationDetails);
        Assert.Equal("AbstractedHeadsUp", result.LeafEvaluationDetails!.EvaluatorType);
        Assert.Equal("WeightedBlindsBTNUnopened", result.LeafEvaluationDetails.AbstractionSource);
        Assert.Equal(2, result.LeafEvaluationDetails.ActualActiveOpponentCount);
    }

    [Fact]
    public async Task GetStrategyResultAsync_UsesRequestPopulationProfile_WhenProvided()
    {
        var sut = new LivePreflopSolveService(
            new InMemoryRegretStore(),
            new InMemoryAverageStrategyStore(),
            new InMemoryPreflopTrainingProgressStore(),
            new PreflopInfoSetMapper(),
            new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2/UNOPENED/BTN/eff=100",
            CreateBtnThreeWayRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))],
            PopulationProfileName: PreflopPopulationProfiles.MicroStakesLoosePassiveName);

        var result = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.LeafEvaluationDetails);
        Assert.Equal(PreflopPopulationProfiles.MicroStakesLoosePassiveName, result.LeafEvaluationDetails!.ActivePopulationProfile);
        Assert.Contains(PreflopPopulationProfiles.MicroStakesLoosePassiveName, result.ActionValueSupport);
    }


    [Fact]
    public async Task GetStrategyResultAsync_UsesDeterministicExplanationForDisplayedAction()
    {
        var sut = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), new InMemoryPreflopTrainingProgressStore(), new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2/UNOPENED/BTN/eff=100",
            CreateBtnThreeWayRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var result = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.LeafEvaluationDetails);
        Assert.NotNull(result.ActionExplanations);
        Assert.Equal(request.LegalActions.Count, result.ActionExplanations!.Count);

        var displayedAction = result.ActionDiagnostics
            .OrderByDescending(x => x.Frequency)
            .First()
            .ActionKey;

        Assert.StartsWith(result.LeafEvaluationDetails!.RootActionType!, displayedAction);
        Assert.Equal("AbstractedHeadsUp", result.LeafEvaluationDetails.EvaluatorType);
        Assert.Equal("WeightedBlindsBTNUnopened", result.LeafEvaluationDetails.AbstractionSource);
        Assert.NotNull(result.LeafEvaluationDetails.HeroEquity);
    }

    [Fact]
    public async Task GetStrategyResultAsync_ExplanationIsStableAcrossRepeatedRuns()
    {
        var sut = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), new InMemoryPreflopTrainingProgressStore(), new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2/UNOPENED/BTN/eff=100",
            CreateBtnThreeWayRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var first = await sut.GetStrategyResultAsync(request, CancellationToken.None);
        var second = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(first?.LeafEvaluationDetails);
        Assert.NotNull(second?.LeafEvaluationDetails);
        Assert.Equal(first!.LeafEvaluationDetails!.EvaluatorType, second!.LeafEvaluationDetails!.EvaluatorType);
        Assert.Equal(first.LeafEvaluationDetails.AbstractionSource, second.LeafEvaluationDetails.AbstractionSource);
        Assert.Equal(first.LeafEvaluationDetails.RootActionType, second.LeafEvaluationDetails.RootActionType);
        Assert.Equal(first.LeafEvaluationDetails.ActualActiveOpponentCount, second.LeafEvaluationDetails.ActualActiveOpponentCount);
        Assert.NotEqual("HeuristicFallback", first.LeafEvaluationDetails.EvaluatorType);
        Assert.NotEqual("HeuristicFallback", second.LeafEvaluationDetails.EvaluatorType);
    }


    private static SolverHandState CreateBtnThreeWayRootState()
    {
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();

        return new SolverHandState(
            new GameConfig(3, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000)),
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: btnId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players:
            [
                new SolverPlayerState(btnId, 0, Position.BTN, new ChipAmount(10000), ChipAmount.Zero, ChipAmount.Zero, false, false),
                new SolverPlayerState(sbId, 1, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
                new SolverPlayerState(bbId, 2, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
            ],
            actionHistory:
            [
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            ]);
    }

    private static SolverHandState CreateRootState()
    {
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();

        return new SolverHandState(
            new GameConfig(2, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000)),
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: bbId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players:
            [
                new SolverPlayerState(sbId, 0, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
                new SolverPlayerState(bbId, 1, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
            ],
            actionHistory:
            [
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            ]);
    }


    [Fact]
    public async Task GetStrategyResultAsync_DiagnosticsExposeAverageAndCurrentPolicySeparately()
    {
        var sut = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), new InMemoryPreflopTrainingProgressStore(), new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        var request = new PreflopStrategyRequestDto(
            "v2/UNOPENED/BTN/eff=100",
            CreateBtnThreeWayRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var result = await sut.GetStrategyResultAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.ActionDiagnostics!);
        Assert.All(result.ActionDiagnostics!, x => Assert.InRange(x.Frequency, 0m, 1m));
        Assert.All(result.ActionDiagnostics!, x => Assert.InRange(x.CurrentPolicyFrequency, 0m, 1m));

        var avgFreqSpread = result.ActionDiagnostics!.Select(x => x.Frequency).Distinct().Count();
        var currFreqSpread = result.ActionDiagnostics!.Select(x => x.CurrentPolicyFrequency).Distinct().Count();
        Assert.True(avgFreqSpread > 1 || currFreqSpread > 1);

        Assert.Equal(result.BestActionMargin, result.SeparationScore);
    }

    [Fact]
    public async Task GetStrategyResultAsync_FreshMode_MultiRunAveragingReducesFrequencyVolatilityAgainstSingleShortRuns()
    {
        var request = new PreflopStrategyRequestDto(
            "v2:test:stability",
            CreateRootState(),
            [new LegalAction(ActionType.Fold), new LegalAction(ActionType.Call, new ChipAmount(100)), new LegalAction(ActionType.Raise, new ChipAmount(250))]);

        var liveService = new LivePreflopSolveService(new InMemoryRegretStore(), new InMemoryAverageStrategyStore(), new InMemoryPreflopTrainingProgressStore(), new PreflopInfoSetMapper(), new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName), new InMemoryActionValueStore());

        const int samples = 20;
        var averagedFoldFrequencies = new List<decimal>(samples);
        var singleRunFoldFrequencies = new List<double>(samples);

        for (var i = 0; i < samples; i++)
        {
            var averaged = await liveService.GetStrategyResultAsync(request, CancellationToken.None);
            Assert.NotNull(averaged);
            averagedFoldFrequencies.Add(averaged!.AverageStrategy["Fold"]);

            var foldFreq = RunSingleShortSolveFoldFrequency(request);
            singleRunFoldFrequencies.Add(foldFreq);
        }

        var averagedRange = averagedFoldFrequencies.Max() - averagedFoldFrequencies.Min();
        var singleRange = singleRunFoldFrequencies.Max() - singleRunFoldFrequencies.Min();

        Assert.True((double)averagedRange <= singleRange, $"Expected averaged fresh solves to have equal/lower fold-frequency range than single short runs, got averagedRange={averagedRange}, singleRange={singleRange}.");
    }

    private static double RunSingleShortSolveFoldFrequency(PreflopStrategyRequestDto request)
    {
        var regretStore = new InMemoryRegretStore();
        var averageStore = new InMemoryAverageStrategyStore();
        var actionValueStore = new InMemoryActionValueStore();

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(request.RootState),
            new SolverChanceSampler(),
            new PreflopInfoSetMapper(),
            new WeightedRandomActionSampler(),
            new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName)),
            new DefaultPreflopLeafDetector(),
            new FixedTraversalPlayerSelector(request.RootState.ActingPlayerId),
            regretStore,
            averageStore,
            new InMemoryPreflopTrainingProgressStore(),
            request.SolverKey,
            actionValueStore);

        _ = trainer.RunTraining(PreflopTrainingOptions.ForIterations(100), CancellationToken.None, randomSeed: Random.Shared.Next());
        var policy = averageStore.GetAveragePolicy(request.SolverKey, request.LegalActions);
        var foldAction = request.LegalActions.Single(x => x.ActionType == ActionType.Fold);
        return policy.TryGetValue(foldAction, out var value) ? value : 0d;
    }

}
