using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopAnalysis;

public sealed class LivePreflopSolveService : IPreflopStrategyProvider
{
    private static readonly PreflopTrainingOptions DefaultOptions = PreflopTrainingOptions.ForIterations(300);

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

        var regretStore = request.UsePersistentTrainingState ? _regretStore : new InMemoryRegretStore();
        var averageStrategyStore = request.UsePersistentTrainingState ? _averageStrategyStore : new InMemoryAverageStrategyStore();
        var trainingProgressStore = request.UsePersistentTrainingState ? _trainingProgressStore : new InMemoryPreflopTrainingProgressStore();
        var actionValueStore = request.UsePersistentTrainingState ? _actionValueStore : new InMemoryActionValueStore();

        var profileProvider = ResolveProfileProvider(request.PopulationProfileName);

        var trainer = new PreflopRegretTrainer(
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

        var trainingResult = trainer.RunTraining(DefaultOptions, ct);

        var averagePolicy = averageStrategyStore.GetAveragePolicy(request.SolverKey, request.LegalActions);
        _ = new RegretMatchingPolicyProvider(regretStore, actionValueStore).TryGetPolicy(request.SolverKey, request.LegalActions, out var currentPolicy);
        currentPolicy ??= UniformPolicyBuilder.Build(request.LegalActions);

        var strategy = request.LegalActions.ToDictionary(
            ToActionKey,
            action => (decimal)(averagePolicy.TryGetValue(action, out var prob) ? prob : 0d),
            StringComparer.Ordinal);

        var regretMagnitude = request.LegalActions.Sum(a => Math.Max(0d, regretStore.Get(request.SolverKey, a)));
        var diagnostics = request.LegalActions
            .Select(action =>
            {
                var key = ToActionKey(action);
                var averageFrequency = strategy.TryGetValue(key, out var f) ? f : 0m;
                var currentFrequency = (decimal)(currentPolicy.TryGetValue(action, out var cp) ? cp : 0d);
                var regret = regretStore.Get(request.SolverKey, action);
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
            var leafDetails = MapLeafDetails(trainer.ExplainDisplayedActionDeterministically(action, request.LegalActions));
            explanations.Add(new PreflopActionExplanationDto(ToActionKey(action), leafDetails));
            if (string.Equals(ToActionKey(action), bestActionKey, StringComparison.Ordinal))
                bestActionLeafDetails = leafDetails;
        }

        return Task.FromResult<PreflopStrategyResultDto?>(new PreflopStrategyResultDto(
            request.SolverKey,
            strategy,
            trainingResult.IterationsCompleted,
            regretMagnitude,
            "LiveSolved",
            (long)trainingResult.Elapsed.TotalMilliseconds,
            request.UsePersistentTrainingState ? "Persistent" : "Fresh",
            bestActionLeafDetails,
            explanations,
            diagnostics,
            $"Average frequencies come from cumulative average strategy; current-policy frequencies come from regret matching on positive cumulative regret and action-value-based stochastic fallback when all regrets are non-positive; regrets are cumulative counterfactual regrets. Profile={profileProvider.ActiveProfileName}.",
            bestMargin,
            separation));
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
}
