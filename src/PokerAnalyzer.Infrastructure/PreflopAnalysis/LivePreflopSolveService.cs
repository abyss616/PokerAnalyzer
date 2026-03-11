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

    public LivePreflopSolveService(
        IRegretStore regretStore,
        IAverageStrategyStore averageStrategyStore,
        IPreflopTrainingProgressStore trainingProgressStore,
        IPreflopInfoSetMapper infoSetMapper)
    {
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
        _averageStrategyStore = averageStrategyStore ?? throw new ArgumentNullException(nameof(averageStrategyStore));
        _trainingProgressStore = trainingProgressStore ?? throw new ArgumentNullException(nameof(trainingProgressStore));
        _infoSetMapper = infoSetMapper ?? throw new ArgumentNullException(nameof(infoSetMapper));
    }

    public Task<PreflopStrategyResultDto?> GetStrategyResultAsync(PreflopStrategyRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.LegalActions.Count == 0)
            return Task.FromResult<PreflopStrategyResultDto?>(null);

        var regretStore = request.UsePersistentTrainingState ? _regretStore : new InMemoryRegretStore();
        var averageStrategyStore = request.UsePersistentTrainingState ? _averageStrategyStore : new InMemoryAverageStrategyStore();
        var trainingProgressStore = request.UsePersistentTrainingState ? _trainingProgressStore : new InMemoryPreflopTrainingProgressStore();

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(request.RootState),
            new SolverChanceSampler(),
            _infoSetMapper,
            new WeightedRandomActionSampler(),
            new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator()),
            new DefaultPreflopLeafDetector(),
            new FixedTraversalPlayerSelector(request.RootState.ActingPlayerId),
            regretStore,
            averageStrategyStore,
            trainingProgressStore,
            request.SolverKey);

        var trainingResult = trainer.RunTraining(DefaultOptions, ct);

        var averagePolicy = averageStrategyStore.GetAveragePolicy(request.SolverKey, request.LegalActions);
        var strategy = request.LegalActions.ToDictionary(
            ToActionKey,
            action => (decimal)(averagePolicy.TryGetValue(action, out var prob) ? prob : 0d),
            StringComparer.Ordinal);

        var regretMagnitude = request.LegalActions.Sum(a => Math.Max(0d, regretStore.Get(request.SolverKey, a)));

        return Task.FromResult<PreflopStrategyResultDto?>(new PreflopStrategyResultDto(
            request.SolverKey,
            strategy,
            trainingResult.IterationsCompleted,
            regretMagnitude,
            "LiveSolved",
            (long)trainingResult.Elapsed.TotalMilliseconds,
            request.UsePersistentTrainingState ? "Persistent" : "Fresh"));
    }

    private static string ToActionKey(LegalAction action)
    {
        if (action.Amount?.Value > 0L)
            return $"{action.ActionType}:{action.Amount.Value.Value / 100m:0.##}";

        return action.ActionType.ToString();
    }
}
