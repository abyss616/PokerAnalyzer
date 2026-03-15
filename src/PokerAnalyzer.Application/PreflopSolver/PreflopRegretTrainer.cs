using PokerAnalyzer.Domain.Game;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PokerAnalyzer.Application.PreflopSolver;

public interface IRegretStore
{
    void Add(string infoSetKey, LegalAction action, double regretDelta);
    double Get(string infoSetKey, LegalAction action);
}

public interface IAverageStrategyStore
{
    void Add(string infoSetKey, LegalAction action, double weight);
    double Get(string infoSetKey, LegalAction action);
    IReadOnlyDictionary<LegalAction, double> GetAveragePolicy(string infoSetKey, IReadOnlyList<LegalAction> legalActions);
}

public interface IActionValueStore
{
    void AddSamples(string infoSetKey, LegalAction action, double totalUtility, int sampleCount);
    bool TryGetAverage(string infoSetKey, LegalAction action, out double averageUtility);
}

public sealed class InMemoryRegretStore : IRegretStore
{
    private readonly Dictionary<string, Dictionary<LegalAction, double>> _values = new(StringComparer.Ordinal);

    public void Add(string infoSetKey, LegalAction action, double regretDelta)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);

        if (!_values.TryGetValue(infoSetKey, out var byAction))
        {
            byAction = new Dictionary<LegalAction, double>();
            _values[infoSetKey] = byAction;
        }

        byAction[action] = Get(infoSetKey, action) + regretDelta;
    }

    public double Get(string infoSetKey, LegalAction action)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);

        if (_values.TryGetValue(infoSetKey, out var byAction) && byAction.TryGetValue(action, out var regret))
            return regret;

        return 0d;
    }
}

public sealed class InMemoryAverageStrategyStore : IAverageStrategyStore
{
    private readonly Dictionary<string, Dictionary<LegalAction, double>> _values = new(StringComparer.Ordinal);

    public void Add(string infoSetKey, LegalAction action, double weight)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);

        if (!_values.TryGetValue(infoSetKey, out var byAction))
        {
            byAction = new Dictionary<LegalAction, double>();
            _values[infoSetKey] = byAction;
        }

        byAction[action] = Get(infoSetKey, action) + weight;
    }

    public double Get(string infoSetKey, LegalAction action)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);

        if (_values.TryGetValue(infoSetKey, out var byAction) && byAction.TryGetValue(action, out var value))
            return value;

        return 0d;
    }

    public IReadOnlyDictionary<LegalAction, double> GetAveragePolicy(string infoSetKey, IReadOnlyList<LegalAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);
        ArgumentNullException.ThrowIfNull(legalActions);

        if (legalActions.Count == 0)
            return new Dictionary<LegalAction, double>();

        var weights = new Dictionary<LegalAction, double>(legalActions.Count);
        var total = 0d;

        foreach (var action in legalActions)
        {
            var weight = Get(infoSetKey, action);
            weights[action] = weight;
            total += weight;
        }

        if (total > 0d)
            return weights.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / total);

        return UniformPolicyBuilder.Build(legalActions);
    }
}

public sealed class InMemoryActionValueStore : IActionValueStore
{
    private readonly Dictionary<string, Dictionary<LegalAction, (double TotalUtility, int Samples)>> _values = new(StringComparer.Ordinal);

    public void AddSamples(string infoSetKey, LegalAction action, double totalUtility, int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);
        if (sampleCount <= 0)
            return;

        if (!_values.TryGetValue(infoSetKey, out var byAction))
        {
            byAction = new Dictionary<LegalAction, (double TotalUtility, int Samples)>();
            _values[infoSetKey] = byAction;
        }

        var existing = byAction.TryGetValue(action, out var aggregate)
            ? aggregate
            : (0d, 0);

        byAction[action] = (existing.Item1 + totalUtility, existing.Item2 + sampleCount);
    }

    public bool TryGetAverage(string infoSetKey, LegalAction action, out double averageUtility)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);

        if (_values.TryGetValue(infoSetKey, out var byAction)
            && byAction.TryGetValue(action, out var aggregate)
            && aggregate.Item2 > 0)
        {
            averageUtility = aggregate.Item1 / aggregate.Item2;
            return true;
        }

        averageUtility = 0d;
        return false;
    }
}

public sealed class RegretMatchingPolicyProvider : IPreflopPolicyProvider
{
    private const double FallbackSoftmaxTemperature = 0.5d;
    private const double MaxScaledDisadvantage = 12d;

    private readonly IRegretStore _regretStore;
    private readonly IActionValueStore? _actionValueStore;

    public RegretMatchingPolicyProvider(IRegretStore regretStore, IActionValueStore? actionValueStore = null)
    {
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
        _actionValueStore = actionValueStore;
    }

    public bool TryGetPolicy(string infoSetKey, IReadOnlyList<LegalAction> legalActions, out IReadOnlyDictionary<LegalAction, double> policy)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);
        ArgumentNullException.ThrowIfNull(legalActions);

        if (legalActions.Count == 0)
        {
            policy = new Dictionary<LegalAction, double>();
            return false;
        }

        var positiveRegrets = new Dictionary<LegalAction, double>(legalActions.Count);
        var totalPositiveRegret = 0d;

        foreach (var legalAction in legalActions)
        {
            var regret = _regretStore.Get(infoSetKey, legalAction);
            if (regret <= 0d)
                continue;

            positiveRegrets[legalAction] = regret;
            totalPositiveRegret += regret;
        }

        if (totalPositiveRegret > 0d)
        {
            policy = positiveRegrets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / totalPositiveRegret);
            return true;
        }

        policy = ResolveActionValueFallbackPolicy(infoSetKey, legalActions);
        return true;
    }

    private IReadOnlyDictionary<LegalAction, double> ResolveActionValueFallbackPolicy(string infoSetKey, IReadOnlyList<LegalAction> legalActions)
    {
        if (_actionValueStore is null)
            return UniformPolicyBuilder.Build(legalActions);

        var knownUtilities = new Dictionary<LegalAction, double>(legalActions.Count);
        foreach (var action in legalActions)
        {
            if (_actionValueStore.TryGetAverage(infoSetKey, action, out var utility))
                knownUtilities[action] = utility;
        }

        if (knownUtilities.Count == 0)
            return UniformPolicyBuilder.Build(legalActions);

        var maxUtility = knownUtilities.Values.Max();
        var minUtility = knownUtilities.Values.Min();
        var weights = new Dictionary<LegalAction, double>(legalActions.Count);
        var totalWeight = 0d;

        foreach (var action in legalActions)
        {
            var utility = knownUtilities.TryGetValue(action, out var value) ? value : minUtility;
            var shifted = (utility - maxUtility) / FallbackSoftmaxTemperature;
            shifted = Math.Max(-MaxScaledDisadvantage, shifted);
            var weight = Math.Exp(shifted);
            weights[action] = weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0d || double.IsNaN(totalWeight) || double.IsInfinity(totalWeight))
            return UniformPolicyBuilder.Build(legalActions);

        return weights.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / totalWeight);
    }
}

public sealed class CanonicalKeyRegretMatchingPolicyProvider : IPreflopPolicyProvider
{
    private readonly RegretMatchingPolicyProvider _innerProvider;
    private readonly string _canonicalStorageKey;

    public CanonicalKeyRegretMatchingPolicyProvider(IRegretStore regretStore, string canonicalStorageKey, IActionValueStore? actionValueStore = null)
    {
        _innerProvider = new RegretMatchingPolicyProvider(regretStore ?? throw new ArgumentNullException(nameof(regretStore)), actionValueStore);
        _canonicalStorageKey = string.IsNullOrWhiteSpace(canonicalStorageKey)
            ? throw new ArgumentException("Canonical storage key cannot be null or whitespace.", nameof(canonicalStorageKey))
            : canonicalStorageKey;
    }

    public bool TryGetPolicy(string infoSetKey, IReadOnlyList<LegalAction> legalActions, out IReadOnlyDictionary<LegalAction, double> policy)
        => _innerProvider.TryGetPolicy(_canonicalStorageKey, legalActions, out policy);
}

public interface ITraversalPlayerSelector
{
    PlayerId Select(SolverHandState rootState);
}

public sealed class AlternatingTraversalPlayerSelector : ITraversalPlayerSelector
{
    private int _index;

    public PlayerId Select(SolverHandState rootState)
    {
        ArgumentNullException.ThrowIfNull(rootState);

        if (rootState.Players.Count == 0)
            throw new InvalidOperationException("Root state contains no players.");

        var selectedIndex = Interlocked.Increment(ref _index) - 1;
        var selected = rootState.Players[selectedIndex % rootState.Players.Count].PlayerId;
        return selected;
    }
}

public sealed record PreflopTrainerOptions(
    int Iterations,
    int WorkerCount = 12,
    int BatchSize = 64,
    int? RandomSeed = null,
    bool Deterministic = false)
{
    public void Validate()
    {
        if (Iterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(Iterations), "Iterations must be positive.");

        if (WorkerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(WorkerCount), "WorkerCount must be positive.");

        if (BatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(BatchSize), "BatchSize must be positive.");
    }
}

public sealed class FixedTraversalPlayerSelector : ITraversalPlayerSelector
{
    private readonly PlayerId _playerId;

    public FixedTraversalPlayerSelector(PlayerId playerId)
    {
        _playerId = playerId;
    }

    public PlayerId Select(SolverHandState rootState) => _playerId;
}

public enum PreflopTrainingMode
{
    Time,
    Iterations
}

public sealed class PreflopTrainingOptions
{
    public const int DefaultIterationBudget = 10_000;
    public static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromSeconds(20);
    public static PreflopTrainingOptions Default { get; } = ForTime(DefaultTimeBudget);

    public PreflopTrainingMode Mode { get; }
    public TimeSpan? MaxDuration { get; }
    public int? MaxIterations { get; }

    private PreflopTrainingOptions(PreflopTrainingMode mode, TimeSpan? maxDuration, int? maxIterations)
    {
        Mode = mode;
        MaxDuration = maxDuration;
        MaxIterations = maxIterations;
    }

    public static PreflopTrainingOptions ForTime(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");

        return new PreflopTrainingOptions(PreflopTrainingMode.Time, duration, null);
    }

    public static PreflopTrainingOptions ForIterations(int maxIterations)
    {
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations), "Max iterations must be positive.");

        return new PreflopTrainingOptions(PreflopTrainingMode.Iterations, null, maxIterations);
    }

    public void Validate()
    {
        switch (Mode)
        {
            case PreflopTrainingMode.Time when !MaxDuration.HasValue || MaxDuration.Value <= TimeSpan.Zero:
                throw new ArgumentOutOfRangeException(nameof(MaxDuration), "MaxDuration must be positive in time mode.");

            case PreflopTrainingMode.Iterations when !MaxIterations.HasValue || MaxIterations.Value <= 0:
                throw new ArgumentOutOfRangeException(nameof(MaxIterations), "MaxIterations must be positive in iterations mode.");
        }
    }
}

public sealed class PreflopTrainingResult
{
    public required int IterationsCompleted { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required PreflopTrainingMode ModeUsed { get; init; }
    public required bool StoppedByCancellation { get; init; }
    public required bool ReachedTimeLimit { get; init; }
    public required bool ReachedIterationLimit { get; init; }
    public PreflopLeafEvaluationDetails? LastSampledLeafEvaluationDetails { get; init; }
}

public interface IPreflopTrainingProgressStore
{
    int TotalIterationsCompleted { get; }
    void IncrementIterations(int count);
}

public sealed class InMemoryPreflopTrainingProgressStore : IPreflopTrainingProgressStore
{
    private int _totalIterationsCompleted;

    public int TotalIterationsCompleted => _totalIterationsCompleted;

    public void IncrementIterations(int count)
    {
        if (count <= 0)
            return;

        _totalIterationsCompleted += count;
    }
}

public sealed class NullPreflopTrainingProgressStore : IPreflopTrainingProgressStore
{
    public static NullPreflopTrainingProgressStore Instance { get; } = new();

    private NullPreflopTrainingProgressStore()
    {
    }

    public int TotalIterationsCompleted => 0;

    public void IncrementIterations(int count)
    {
    }
}

public sealed class PreflopRegretTrainer
{
    private readonly IPreflopRootStateProvider _rootStateProvider;
    private readonly IPreflopTrajectoryTraverser _trajectoryTraverser;
    private readonly ITraversalPlayerSelector _traversalPlayerSelector;
    private readonly IRegretStore _regretStore;
    private readonly IAverageStrategyStore _averageStrategyStore;
    private readonly IActionValueStore _actionValueStore;
    private readonly IPreflopTrainingProgressStore _trainingProgressStore;
    private readonly string? _canonicalStorageKey;
    private readonly RegretMatchingPolicyProvider _policyProvider;
    private PreflopLeafEvaluationDetails? _latestLeafEvaluationDetails;
    private readonly object _traversalSelectorLock = new();


    public PreflopRegretTrainer(
        IPreflopRootStateProvider rootStateProvider,
        IChanceSampler chanceSampler,
        IPreflopInfoSetMapper infoSetMapper,
        IActionSampler actionSampler,
        IPreflopLeafEvaluator leafEvaluator,
        IPreflopLeafDetector leafDetector,
        ITraversalPlayerSelector traversalPlayerSelector,
        IRegretStore regretStore,
        IAverageStrategyStore averageStrategyStore,
        IPreflopTrainingProgressStore? trainingProgressStore = null,
        string? canonicalStorageKey = null,
        IActionValueStore? actionValueStore = null)
        : this(
            rootStateProvider,
            new PreflopTrajectoryTraverser(
                rootStateProvider,
                chanceSampler,
                infoSetMapper,
                string.IsNullOrWhiteSpace(canonicalStorageKey)
                    ? new RegretMatchingPolicyProvider(regretStore, actionValueStore)
                    : new CanonicalKeyRegretMatchingPolicyProvider(regretStore, canonicalStorageKey, actionValueStore),
                actionSampler,
                leafEvaluator,
                leafDetector),
            traversalPlayerSelector,
            regretStore,
            averageStrategyStore,
            trainingProgressStore,
            canonicalStorageKey,
            actionValueStore)
    {
    }

    public PreflopRegretTrainer(
        IPreflopRootStateProvider rootStateProvider,
        IPreflopTrajectoryTraverser trajectoryTraverser,
        ITraversalPlayerSelector traversalPlayerSelector,
        IRegretStore regretStore,
        IAverageStrategyStore averageStrategyStore,
        IPreflopTrainingProgressStore? trainingProgressStore = null,
        string? canonicalStorageKey = null,
        IActionValueStore? actionValueStore = null)
    {
        _rootStateProvider = rootStateProvider ?? throw new ArgumentNullException(nameof(rootStateProvider));
        _trajectoryTraverser = trajectoryTraverser ?? throw new ArgumentNullException(nameof(trajectoryTraverser));
        _traversalPlayerSelector = traversalPlayerSelector ?? throw new ArgumentNullException(nameof(traversalPlayerSelector));
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
        _averageStrategyStore = averageStrategyStore ?? throw new ArgumentNullException(nameof(averageStrategyStore));
        _actionValueStore = actionValueStore ?? new InMemoryActionValueStore();
        _trainingProgressStore = trainingProgressStore ?? NullPreflopTrainingProgressStore.Instance;
        _canonicalStorageKey = string.IsNullOrWhiteSpace(canonicalStorageKey) ? null : canonicalStorageKey;
        _policyProvider = new RegretMatchingPolicyProvider(_regretStore, _actionValueStore);
    }

    public void RunIteration(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var localAccumulator = new WorkerAccumulator();
        RunIteration(rng, localAccumulator, null);
        MergeWorkerAccumulator(localAccumulator);

        _latestLeafEvaluationDetails = localAccumulator.LastLeafEvaluationDetails ?? _latestLeafEvaluationDetails;
        _trainingProgressStore.IncrementIterations(1);
    }

    private void RunIteration(Random rng, WorkerAccumulator accumulator, int? deterministicIterationIndex)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(accumulator);

        var rootState = _rootStateProvider.CreateRootState();
        var traversalPlayerId = SelectTraversalPlayer(rootState, deterministicIterationIndex);
        var sample = _trajectoryTraverser.SampleTrajectory(rootState, rng);

        foreach (var node in sample.Path)
        {
            if (node.NodeKind != TraversalNodeKind.Action)
                continue;

            if (node.ActingPlayerId != traversalPlayerId)
                continue;

            if (string.IsNullOrWhiteSpace(node.InfoSetKey) || node.StateBeforeAction is null || node.LegalActions.Count == 0)
                continue;

            var storageKey = _canonicalStorageKey ?? node.InfoSetKey;
            var (actionValues, leafDetails) = EvaluateActionValues(node.StateBeforeAction, traversalPlayerId, node.LegalActions, rng, storageKey);
            accumulator.LastLeafEvaluationDetails = leafDetails ?? accumulator.LastLeafEvaluationDetails;
            foreach (var action in node.LegalActions)
                accumulator.AddActionValue(storageKey, action, actionValues[action]);

            var nodeValue = 0d;

            var policy = ResolvePolicy(storageKey, node.LegalActions, node.Policy);
           // Trace.WriteLine($"preflop-trainer infoset={storageKey}, legalActions=[{string.Join(", ", node.LegalActions)}], chosenAction={node.SampledAction}");

            foreach (var action in node.LegalActions)
            {
                var policyProbability = policy.TryGetValue(action, out var probability)
                    ? probability
                    : 0d;

                nodeValue += policyProbability * actionValues[action];
            }

            //Trace.WriteLine($"preflop-trainer actionUtilities infoset={storageKey}: {string.Join(", ", actionValues.Select(kvp => $"{kvp.Key}={kvp.Value:0.0000}"))}, nodeValue={nodeValue:0.0000}");

            foreach (var action in node.LegalActions)
            {
                var regretDelta = actionValues[action] - nodeValue;
                accumulator.AddRegret(storageKey, action, regretDelta);
                var cumulativeRegret = _regretStore.Get(storageKey, action) + regretDelta;
                //Trace.WriteLine($"preflop-trainer regretUpdate infoset={storageKey}, action={action}, delta={regretDelta:0.0000}, cumulative={cumulativeRegret:0.0000}");
            }

            foreach (var action in node.LegalActions)
            {
                var probability = policy.TryGetValue(action, out var policyProbability)
                    ? policyProbability
                    : 0d;

                accumulator.AddAverageStrategy(storageKey, action, probability);
            }

            // Future hook: MCCFR-style weighting can adjust regretDelta before Add.
        }

        accumulator.IterationsCompleted++;

    }

    private IReadOnlyDictionary<LegalAction, double> ResolvePolicy(
        string infoSetKey,
        IReadOnlyList<LegalAction> legalActions,
        IReadOnlyDictionary<LegalAction, double> fallbackPolicy)
    {
        if (_canonicalStorageKey is null)
            return fallbackPolicy;

        return _policyProvider.TryGetPolicy(infoSetKey, legalActions, out var policy)
            ? policy
            : fallbackPolicy;
    }


    public PreflopLeafEvaluationDetails? ExplainDisplayedActionDeterministically(
        LegalAction rootAction,
        IReadOnlyList<LegalAction> legalActions,
        int deterministicSeed = 1337)
    {
        ArgumentNullException.ThrowIfNull(rootAction);
        ArgumentNullException.ThrowIfNull(legalActions);

        var rootState = _rootStateProvider.CreateRootState();
        var traversalPlayerId = _traversalPlayerSelector.Select(rootState);
        var hero = rootState.Players.FirstOrDefault(player => player.PlayerId == traversalPlayerId);
        if (hero is null || !rootState.PrivateCardsByPlayer.TryGetValue(traversalPlayerId, out var heroCards))
            return null;

        var afterActionState = SolverStateStepper.Step(rootState, rootAction, legalActions);
        var evaluationContext = new PreflopLeafEvaluationContext(
            rootState,
            afterActionState,
            traversalPlayerId,
            hero.Position,
            heroCards,
            ResolveEffectiveStackBb(rootState, traversalPlayerId),
            rootAction,
            _canonicalStorageKey);

        var rollout = _trajectoryTraverser.SampleTrajectory(afterActionState, new Random(deterministicSeed), evaluationContext);
        return rollout.LeafEvaluationDetails;
    }

    public PreflopTrainingResult RunTraining(
        PreflopTrainingOptions? options = null,
        CancellationToken cancellationToken = default,
        int? randomSeed = null)
    {
        options ??= PreflopTrainingOptions.Default;
        options.Validate();

        var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        var stopwatch = Stopwatch.StartNew();
        var iterationsCompleted = 0;

        var reachedIterationLimit = false;
        var reachedTimeLimit = false;

        switch (options.Mode)
        {
            case PreflopTrainingMode.Iterations:
            {
                var maxIterations = options.MaxIterations!.Value;
                while (!cancellationToken.IsCancellationRequested && iterationsCompleted < maxIterations)
                {
                    RunIteration(rng);
                    iterationsCompleted++;
                }

                reachedIterationLimit = !cancellationToken.IsCancellationRequested && iterationsCompleted >= maxIterations;
                break;
            }

            case PreflopTrainingMode.Time:
            {
                var maxDuration = options.MaxDuration!.Value;
                while (!cancellationToken.IsCancellationRequested && stopwatch.Elapsed < maxDuration)
                {
                    RunIteration(rng);
                    iterationsCompleted++;
                }

                reachedTimeLimit = !cancellationToken.IsCancellationRequested && stopwatch.Elapsed >= maxDuration;
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(options.Mode), options.Mode, "Unsupported preflop training mode.");
        }

        stopwatch.Stop();

        return new PreflopTrainingResult
        {
            IterationsCompleted = iterationsCompleted,
            Elapsed = stopwatch.Elapsed,
            ModeUsed = options.Mode,
            StoppedByCancellation = cancellationToken.IsCancellationRequested,
            ReachedTimeLimit = reachedTimeLimit,
            ReachedIterationLimit = reachedIterationLimit,
            LastSampledLeafEvaluationDetails = _latestLeafEvaluationDetails
        };
    }

    public PreflopTrainingResult RunTraining(
        PreflopTrainerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (options.WorkerCount == 1)
            return RunSingleWorkerTraining(options, cancellationToken);

        return RunParallelTraining(options, cancellationToken);
    }

    private PreflopTrainingResult RunSingleWorkerTraining(PreflopTrainerOptions options, CancellationToken cancellationToken)
    {
        var rng = CreateRandom(options, workerId: 0, epoch: 0);
        var stopwatch = Stopwatch.StartNew();
        var iterationsCompleted = 0;

        while (!cancellationToken.IsCancellationRequested && iterationsCompleted < options.Iterations)
        {
            var localAccumulator = new WorkerAccumulator();
            RunIteration(rng, localAccumulator, options.Deterministic ? iterationsCompleted : null);
            MergeWorkerAccumulator(localAccumulator);
            _latestLeafEvaluationDetails = localAccumulator.LastLeafEvaluationDetails ?? _latestLeafEvaluationDetails;
            iterationsCompleted++;
            _trainingProgressStore.IncrementIterations(1);
        }

        stopwatch.Stop();

        return new PreflopTrainingResult
        {
            IterationsCompleted = iterationsCompleted,
            Elapsed = stopwatch.Elapsed,
            ModeUsed = PreflopTrainingMode.Iterations,
            StoppedByCancellation = cancellationToken.IsCancellationRequested,
            ReachedTimeLimit = false,
            ReachedIterationLimit = !cancellationToken.IsCancellationRequested && iterationsCompleted >= options.Iterations,
            LastSampledLeafEvaluationDetails = _latestLeafEvaluationDetails
        };
    }

    private PreflopTrainingResult RunParallelTraining(PreflopTrainerOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var iterationsCompleted = 0;
        var epoch = 0;

        while (!cancellationToken.IsCancellationRequested && iterationsCompleted < options.Iterations)
        {
            var remaining = options.Iterations - iterationsCompleted;
            var epochIterations = Math.Min(remaining, options.WorkerCount * options.BatchSize);

            var workerAccumulators = new WorkerAccumulator[options.WorkerCount];
            var workerIterations = new int[options.WorkerCount];

            var baseIterations = epochIterations / options.WorkerCount;
            var extraIterations = epochIterations % options.WorkerCount;
            var workerStartIndices = new int[options.WorkerCount];
            var nextStartIndex = iterationsCompleted;

            for (var workerId = 0; workerId < options.WorkerCount; workerId++)
            {
                var assignedIterations = baseIterations + (workerId < extraIterations ? 1 : 0);
                workerIterations[workerId] = assignedIterations;
                workerStartIndices[workerId] = nextStartIndex;
                nextStartIndex += assignedIterations;
            }

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = options.WorkerCount
            };

            try
            {
                Parallel.For(0, options.WorkerCount, parallelOptions, workerId =>
                {
                    var assignedIterations = workerIterations[workerId];
                    if (assignedIterations <= 0)
                        return;

                    var rng = CreateRandom(options, workerId, epoch);
                    var local = new WorkerAccumulator();

                    for (var localIteration = 0; localIteration < assignedIterations; localIteration++)
                    {
                        parallelOptions.CancellationToken.ThrowIfCancellationRequested();
                        var deterministicIterationIndex = options.Deterministic
                            ? workerStartIndices[workerId] + localIteration
                            : 0;

                        RunIteration(rng, local, deterministicIterationIndex);
                    }

                    workerAccumulators[workerId] = local;
                });
            }
            catch (OperationCanceledException)
            {
            }

            for (var workerId = 0; workerId < options.WorkerCount; workerId++)
            {
                var accumulator = workerAccumulators[workerId];
                if (accumulator is null)
                    continue;

                MergeWorkerAccumulator(accumulator);
                _latestLeafEvaluationDetails = accumulator.LastLeafEvaluationDetails ?? _latestLeafEvaluationDetails;
                iterationsCompleted += accumulator.IterationsCompleted;
                _trainingProgressStore.IncrementIterations(accumulator.IterationsCompleted);
            }

            epoch++;
        }

        stopwatch.Stop();

        return new PreflopTrainingResult
        {
            IterationsCompleted = iterationsCompleted,
            Elapsed = stopwatch.Elapsed,
            ModeUsed = PreflopTrainingMode.Iterations,
            StoppedByCancellation = cancellationToken.IsCancellationRequested,
            ReachedTimeLimit = false,
            ReachedIterationLimit = !cancellationToken.IsCancellationRequested && iterationsCompleted >= options.Iterations,
            LastSampledLeafEvaluationDetails = _latestLeafEvaluationDetails
        };
    }

    private PlayerId SelectTraversalPlayer(SolverHandState rootState, int? deterministicIterationIndex)
    {
        if (deterministicIterationIndex.HasValue && _traversalPlayerSelector is AlternatingTraversalPlayerSelector)
        {
            var index = deterministicIterationIndex.Value % rootState.Players.Count;
            return rootState.Players[index].PlayerId;
        }

        lock (_traversalSelectorLock)
        {
            return _traversalPlayerSelector.Select(rootState);
        }
    }

    private Random CreateRandom(PreflopTrainerOptions options, int workerId, int epoch)
    {
        if (!options.Deterministic)
            return new Random();

        var seedBase = options.RandomSeed ?? 1337;
        var seed = HashCode.Combine(seedBase, workerId, epoch);
        return new Random(seed);
    }

    private void MergeWorkerAccumulator(WorkerAccumulator local)
    {
        foreach (var (infoSetKey, byAction) in local.RegretDeltas)
        {
            foreach (var (action, delta) in byAction)
                _regretStore.Add(infoSetKey, action, delta);
        }

        foreach (var (infoSetKey, byAction) in local.AverageStrategyDeltas)
        {
            foreach (var (action, delta) in byAction)
                _averageStrategyStore.Add(infoSetKey, action, delta);
        }

        foreach (var (infoSetKey, byAction) in local.ActionValueAggregates)
        {
            foreach (var (action, aggregate) in byAction)
                _actionValueStore.AddSamples(infoSetKey, action, aggregate.Item1, aggregate.Item2);
        }
    }

    private sealed class WorkerAccumulator
    {
        public int IterationsCompleted { get; set; }
        public Dictionary<string, Dictionary<LegalAction, double>> RegretDeltas { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<LegalAction, double>> AverageStrategyDeltas { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<LegalAction, (double TotalUtility, int Samples)>> ActionValueAggregates { get; } = new(StringComparer.Ordinal);
        public PreflopLeafEvaluationDetails? LastLeafEvaluationDetails { get; set; }

        public void AddRegret(string infoSetKey, LegalAction action, double regretDelta)
            => Add(RegretDeltas, infoSetKey, action, regretDelta);

        public void AddAverageStrategy(string infoSetKey, LegalAction action, double delta)
            => Add(AverageStrategyDeltas, infoSetKey, action, delta);

        public void AddActionValue(string infoSetKey, LegalAction action, double utility)
        {
            if (!ActionValueAggregates.TryGetValue(infoSetKey, out var byAction))
            {
                byAction = new Dictionary<LegalAction, (double TotalUtility, int Samples)>();
                ActionValueAggregates[infoSetKey] = byAction;
            }

            var aggregate = byAction.TryGetValue(action, out var existing)
                ? existing
                : (0d, 0);

            byAction[action] = (aggregate.Item1 + utility, aggregate.Item2 + 1);
        }

        private static void Add(Dictionary<string, Dictionary<LegalAction, double>> destination, string infoSetKey, LegalAction action, double delta)
        {
            if (!destination.TryGetValue(infoSetKey, out var byAction))
            {
                byAction = new Dictionary<LegalAction, double>();
                destination[infoSetKey] = byAction;
            }

            byAction[action] = byAction.TryGetValue(action, out var value)
                ? value + delta
                : delta;
        }
    }

    private (Dictionary<LegalAction, double> Values, PreflopLeafEvaluationDetails? LeafDetails) EvaluateActionValues(
        SolverHandState stateBeforeAction,
        PlayerId traversalPlayerId,
        IReadOnlyList<LegalAction> legalActions,
        Random rng,
        string? solverKey)
    {
        var actionValues = new Dictionary<LegalAction, double>(legalActions.Count);
        PreflopLeafEvaluationDetails? latestDetails = null;

        foreach (var action in legalActions)
        {
            var afterActionState = SolverStateStepper.Step(stateBeforeAction, action, legalActions);
            if (!stateBeforeAction.PrivateCardsByPlayer.TryGetValue(traversalPlayerId, out var heroCards))
                throw new InvalidOperationException($"Missing private cards for traversal player {traversalPlayerId} at root decision.");

            var hero = stateBeforeAction.Players.FirstOrDefault(player => player.PlayerId == traversalPlayerId)
                ?? throw new InvalidOperationException($"Traversal player {traversalPlayerId} not found at root decision.");

            var evaluationContext = new PreflopLeafEvaluationContext(
                stateBeforeAction,
                afterActionState,
                traversalPlayerId,
                hero.Position,
                heroCards,
                ResolveEffectiveStackBb(stateBeforeAction, traversalPlayerId),
                action,
                solverKey);

            var rollout = _trajectoryTraverser.SampleTrajectory(afterActionState, rng, evaluationContext);
            var utility = rollout.UtilityByPlayer.TryGetValue(traversalPlayerId, out var value)
                ? value
                : 0d;

            actionValues[action] = utility;
            latestDetails = rollout.LeafEvaluationDetails ?? latestDetails;

            //var leafReason = rollout.Path.LastOrDefault(node => node.NodeKind == TraversalNodeKind.Leaf)?.Note ?? "unknown leaf";
            //Trace.WriteLine($"preflop-eval action={action}, utility={utility:0.000}, reason={leafReason}");
        }

        return (actionValues, latestDetails);
    }

    private static double ResolveEffectiveStackBb(SolverHandState state, PlayerId heroPlayerId)
    {
        var hero = state.Players.FirstOrDefault(player => player.PlayerId == heroPlayerId)
            ?? throw new InvalidOperationException($"Hero player {heroPlayerId} was not found in state.");

        var villainMaxContribution = state.Players
            .Where(player => player.PlayerId != heroPlayerId && player.IsActive)
            .Select(player => player.Stack.Value + player.CurrentStreetContribution.Value)
            .DefaultIfEmpty(hero.Stack.Value + hero.CurrentStreetContribution.Value)
            .Min();

        var heroTotal = hero.Stack.Value + hero.CurrentStreetContribution.Value;
        var effectiveChips = Math.Min(heroTotal, villainMaxContribution);
        return effectiveChips / (double)Math.Max(1L, state.Config.BigBlind.Value);
    }
}
