using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopAnalysis;

public sealed class LivePreflopSolveService : IPreflopStrategyProvider
{
    private const int FreshSolveRunCount = 6;
    private const int FreshSolveIterationsPerRun = 100;
    private static readonly PreflopTrainingOptions PersistentTrainingOptions = PreflopTrainingOptions.ForIterations(300);
    private static readonly PreflopTrainingOptions FreshSolveTrainingOptions = PreflopTrainingOptions.ForIterations(FreshSolveIterationsPerRun);

    private readonly IRegretStore _regretStore;
    private readonly IAverageStrategyStore _averageStrategyStore;
    private readonly IPreflopTrainingProgressStore _trainingProgressStore;
    private readonly IPreflopInfoSetMapper _infoSetMapper;
    private readonly IPreflopPopulationProfileProvider _populationProfileProvider;
    private readonly IActionValueStore _actionValueStore;

    public LivePreflopSolveService(
        IRegretStore regretStore,
        IAverageStrategyStore averageStrategyStore,
        IPreflopTrainingProgressStore trainingProgressStore,
        IPreflopInfoSetMapper infoSetMapper,
        IPreflopPopulationProfileProvider populationProfileProvider,
        IActionValueStore actionValueStore)
    {
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
        _averageStrategyStore = averageStrategyStore ?? throw new ArgumentNullException(nameof(averageStrategyStore));
        _trainingProgressStore = trainingProgressStore ?? throw new ArgumentNullException(nameof(trainingProgressStore));
        _infoSetMapper = infoSetMapper ?? throw new ArgumentNullException(nameof(infoSetMapper));
        _populationProfileProvider = populationProfileProvider ?? throw new ArgumentNullException(nameof(populationProfileProvider));
        _actionValueStore = actionValueStore ?? throw new ArgumentNullException(nameof(actionValueStore));
    }

    public Task<PreflopStrategyResultDto?> GetStrategyResultAsync(PreflopStrategyRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.LegalActions.Count == 0)
            return Task.FromResult<PreflopStrategyResultDto?>(null);

        var profileProvider = ResolveProfileProvider(request.PopulationProfileName);

        var aggregated = request.UsePersistentTrainingState
            ? RunPersistentTraining(request, profileProvider, ct)
            : RunFreshMultiRunTraining(request, profileProvider, ct);

        var strategy = request.LegalActions.ToDictionary(
            ToActionKey,
            action => (decimal)(aggregated.AveragePolicy.TryGetValue(action, out var prob) ? prob : 0d),
            StringComparer.Ordinal);

        var diagnostics = request.LegalActions
            .Select(action =>
            {
                var key = ToActionKey(action);
                var averageFrequency = strategy.TryGetValue(key, out var f) ? f : 0m;
                var currentFrequency = (decimal)(aggregated.CurrentPolicy.TryGetValue(action, out var cp) ? cp : 0d);
                var regret = aggregated.Regrets.TryGetValue(action, out var r) ? r : 0d;
                return new PreflopActionDiagnosticDto(key, averageFrequency, currentFrequency, regret, Math.Max(0d, regret), false);
            })
            .OrderByDescending(x => x.Frequency)
            .ToList();

        var bestActionKey = diagnostics
            .OrderByDescending(x => x.Frequency)
            .ThenByDescending(x => x.CurrentPolicyFrequency)
            .ThenByDescending(x => x.Regret)
            .Select(x => x.ActionKey)
            .FirstOrDefault();

        diagnostics = diagnostics
            .Select(x => x with { IsBestByFrequency = string.Equals(x.ActionKey, bestActionKey, StringComparison.Ordinal) })
            .ToList();

        var orderedByAverage = diagnostics.OrderByDescending(x => x.Frequency).ToList();
        var bestMargin = orderedByAverage.Count > 1 ? (double)(orderedByAverage[0].Frequency - orderedByAverage[1].Frequency) : 0d;
        var separation = orderedByAverage.Count > 1 ? (double)(orderedByAverage[0].Frequency - orderedByAverage[1].Frequency) : 0d;

        var explanations = new List<PreflopActionExplanationDto>(request.LegalActions.Count);
        PreflopLeafEvaluationDetailsDto? bestActionLeafDetails = null;
        foreach (var action in request.LegalActions)
        {
            var leafDetails = MapLeafDetails(aggregated.ExplanationTrainer.ExplainDisplayedActionDeterministically(action, request.LegalActions));
            explanations.Add(new PreflopActionExplanationDto(ToActionKey(action), leafDetails));
            if (string.Equals(ToActionKey(action), bestActionKey, StringComparison.Ordinal))
                bestActionLeafDetails = leafDetails;
        }

        return Task.FromResult<PreflopStrategyResultDto?>(new PreflopStrategyResultDto(
            request.SolverKey,
            strategy,
            aggregated.IterationsCompleted,
            aggregated.RegretMagnitude,
            "LiveSolved",
            aggregated.ElapsedMilliseconds,
            request.UsePersistentTrainingState ? "Persistent" : "Fresh",
            bestActionLeafDetails,
            explanations,
            diagnostics,
            $"Average frequencies come from {(request.UsePersistentTrainingState ? "cumulative average strategy" : $"mean frequencies across {FreshSolveRunCount} independent stochastic fresh runs")} ; current-policy frequencies come from {(request.UsePersistentTrainingState ? "regret matching on cumulative persistent regrets" : "mean regret-matching policies across independent fresh runs")} and action-value-based stochastic fallback when all regrets are non-positive; regrets are {(request.UsePersistentTrainingState ? "cumulative counterfactual regrets" : "mean cumulative counterfactual regrets across fresh runs")}. Profile={profileProvider.ActiveProfileName}.",
            bestMargin,
            separation));
    }

    private AggregatedSolveData RunPersistentTraining(PreflopStrategyRequestDto request, IPreflopPopulationProfileProvider profileProvider, CancellationToken ct)
    {
        var trainer = CreateTrainer(
            request,
            profileProvider,
            _regretStore,
            _averageStrategyStore,
            _trainingProgressStore,
            _actionValueStore);

        var trainingResult = trainer.RunTraining(PersistentTrainingOptions, ct);
        var averagePolicy = _averageStrategyStore.GetAveragePolicy(request.SolverKey, request.LegalActions);
        _ = new RegretMatchingPolicyProvider(_regretStore, _actionValueStore).TryGetPolicy(request.SolverKey, request.LegalActions, out var currentPolicy);
        currentPolicy ??= UniformPolicyBuilder.Build(request.LegalActions);

        var regrets = request.LegalActions.ToDictionary(action => action, action => _regretStore.Get(request.SolverKey, action));
        var regretMagnitude = request.LegalActions.Sum(a => Math.Max(0d, _regretStore.Get(request.SolverKey, a)));

        return new AggregatedSolveData(
            averagePolicy,
            currentPolicy,
            regrets,
            trainingResult.IterationsCompleted,
            regretMagnitude,
            (long)trainingResult.Elapsed.TotalMilliseconds,
            trainer);
    }

    private AggregatedSolveData RunFreshMultiRunTraining(PreflopStrategyRequestDto request, IPreflopPopulationProfileProvider profileProvider, CancellationToken ct)
    {
        var perRunAveragePolicies = new List<IReadOnlyDictionary<LegalAction, double>>(FreshSolveRunCount);
        var perRunCurrentPolicies = new List<IReadOnlyDictionary<LegalAction, double>>(FreshSolveRunCount);
        var regretSums = request.LegalActions.ToDictionary(action => action, _ => 0d);
        var iterationsCompleted = 0;
        long elapsedMilliseconds = 0;

        PreflopRegretTrainer? explanationTrainer = null;

        for (var runIndex = 0; runIndex < FreshSolveRunCount && !ct.IsCancellationRequested; runIndex++)
        {
            var runRegretStore = new InMemoryRegretStore();
            var runAverageStore = new InMemoryAverageStrategyStore();
            var runProgressStore = new InMemoryPreflopTrainingProgressStore();
            var runActionValueStore = new InMemoryActionValueStore();

            var trainer = CreateTrainer(
                request,
                profileProvider,
                runRegretStore,
                runAverageStore,
                runProgressStore,
                runActionValueStore);

            var trainingResult = trainer.RunTraining(FreshSolveTrainingOptions, ct, randomSeed: Random.Shared.Next());
            iterationsCompleted += trainingResult.IterationsCompleted;
            elapsedMilliseconds += (long)trainingResult.Elapsed.TotalMilliseconds;

            var averagePolicy = runAverageStore.GetAveragePolicy(request.SolverKey, request.LegalActions);
            _ = new RegretMatchingPolicyProvider(runRegretStore, runActionValueStore).TryGetPolicy(request.SolverKey, request.LegalActions, out var currentPolicy);
            currentPolicy ??= UniformPolicyBuilder.Build(request.LegalActions);

            perRunAveragePolicies.Add(averagePolicy);
            perRunCurrentPolicies.Add(currentPolicy);

            foreach (var action in request.LegalActions)
                regretSums[action] += runRegretStore.Get(request.SolverKey, action);

            explanationTrainer ??= trainer;
        }

        if (perRunAveragePolicies.Count == 0)
        {
            var uniform = UniformPolicyBuilder.Build(request.LegalActions);
            var zeroRegrets = request.LegalActions.ToDictionary(action => action, _ => 0d);
            explanationTrainer ??= CreateTrainer(
                request,
                profileProvider,
                new InMemoryRegretStore(),
                new InMemoryAverageStrategyStore(),
                new InMemoryPreflopTrainingProgressStore(),
                new InMemoryActionValueStore());

            return new AggregatedSolveData(
                uniform,
                uniform,
                zeroRegrets,
                0,
                0d,
                elapsedMilliseconds,
                explanationTrainer);
        }

        var averagedPolicy = AveragePolicies(perRunAveragePolicies, request.LegalActions);
        var averagedCurrentPolicy = AveragePolicies(perRunCurrentPolicies, request.LegalActions);
        var averagedRegrets = regretSums.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / perRunAveragePolicies.Count);
        var regretMagnitude = request.LegalActions.Sum(a => Math.Max(0d, averagedRegrets[a]));

        return new AggregatedSolveData(
            averagedPolicy,
            averagedCurrentPolicy,
            averagedRegrets,
            iterationsCompleted,
            regretMagnitude,
            elapsedMilliseconds,
            explanationTrainer!);
    }

    private PreflopRegretTrainer CreateTrainer(
        PreflopStrategyRequestDto request,
        IPreflopPopulationProfileProvider profileProvider,
        IRegretStore regretStore,
        IAverageStrategyStore averageStrategyStore,
        IPreflopTrainingProgressStore trainingProgressStore,
        IActionValueStore actionValueStore)
        => new(
            new FixedRootStateProvider(request.RootState),
            new SolverChanceSampler(),
            _infoSetMapper,
            new WeightedRandomActionSampler(),
            new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), populationProfileProvider: profileProvider),
            new DefaultPreflopLeafDetector(),
            new FixedTraversalPlayerSelector(request.RootState.ActingPlayerId),
            regretStore,
            averageStrategyStore,
            trainingProgressStore,
            request.SolverKey,
            actionValueStore);

    private static IReadOnlyDictionary<LegalAction, double> AveragePolicies(
        IReadOnlyList<IReadOnlyDictionary<LegalAction, double>> policies,
        IReadOnlyList<LegalAction> legalActions)
    {
        if (policies.Count == 0)
            return UniformPolicyBuilder.Build(legalActions);

        var aggregated = legalActions.ToDictionary(action => action, _ => 0d);
        foreach (var policy in policies)
        {
            foreach (var action in legalActions)
                aggregated[action] += policy.TryGetValue(action, out var value) ? value : 0d;
        }

        var count = policies.Count;
        return aggregated.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / count);
    }

    private IPreflopPopulationProfileProvider ResolveProfileProvider(string? populationProfileName)
    {
        if (string.IsNullOrWhiteSpace(populationProfileName))
            return _populationProfileProvider;

        return new NamedPreflopPopulationProfileProvider(populationProfileName);
    }

    private static PreflopLeafEvaluationDetailsDto? MapLeafDetails(PreflopLeafEvaluationDetails? details)
    {
        if (details is null)
            return null;

        return new PreflopLeafEvaluationDetailsDto(
            details.HeroHand,
            details.UsedEquityEvaluator,
            details.UsedFallbackEvaluator,
            details.EvaluatorType,
            details.AbstractionSource,
            details.ActualActiveOpponentCount,
            details.AbstractedOpponentCount,
            details.SyntheticDefenderLabel,
            details.NodeFamily,
            details.HeroPosition,
            details.VillainPosition,
            details.IsHeadsUp,
            details.RangeDescription,
            details.RangeDetail,
            details.FoldProbability,
            details.ContinueProbability,
            details.RootActionType,
            details.ImmediateWinComponent,
            details.ContinueComponent,
            details.ContinueBranchUtility,
            details.FilteredCombos,
            details.HeroEquity,
            details.HeroUtility,
            details.EquityVsRangePercentile,
            details.HandClass,
            details.BlockerSummary,
            details.RationaleSummary,
            details.FallbackReason,
            details.DisplaySummary,
            details.RootEvaluatorMode,
            details.RootActiveOpponentCount,
            details.LeafActiveOpponentCount,
            details.SampledTrajectoryDepth,
            details.UsedDirectAbstractionShortcut,
            details.TraversalMilliseconds,
            details.LeafEvaluationMilliseconds,
            details.ActivePopulationProfile);
    }

    private static string ToActionKey(LegalAction action)
    {
        if (action.Amount?.Value > 0L)
            return $"{action.ActionType}:{action.Amount.Value.Value / 100m:0.##}";

        return action.ActionType.ToString();
    }

    private sealed record AggregatedSolveData(
        IReadOnlyDictionary<LegalAction, double> AveragePolicy,
        IReadOnlyDictionary<LegalAction, double> CurrentPolicy,
        IReadOnlyDictionary<LegalAction, double> Regrets,
        int IterationsCompleted,
        double RegretMagnitude,
        long ElapsedMilliseconds,
        PreflopRegretTrainer ExplanationTrainer);
}
