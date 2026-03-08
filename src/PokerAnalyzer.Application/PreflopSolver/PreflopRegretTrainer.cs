using PokerAnalyzer.Domain.Game;
using System.Diagnostics;

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

public sealed class RegretMatchingPolicyProvider : IPreflopPolicyProvider
{
    private readonly IRegretStore _regretStore;

    public RegretMatchingPolicyProvider(IRegretStore regretStore)
    {
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
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

        policy = UniformPolicyBuilder.Build(legalActions);
        return true;
    }
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

        var selected = rootState.Players[_index % rootState.Players.Count].PlayerId;
        _index++;
        return selected;
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
    private readonly IPreflopTrainingProgressStore _trainingProgressStore;


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
        IPreflopTrainingProgressStore? trainingProgressStore = null)
        : this(
            rootStateProvider,
            new PreflopTrajectoryTraverser(
                rootStateProvider,
                chanceSampler,
                infoSetMapper,
                new RegretMatchingPolicyProvider(regretStore),
                actionSampler,
                leafEvaluator,
                leafDetector),
            traversalPlayerSelector,
            regretStore,
            averageStrategyStore,
            trainingProgressStore)
    {
    }

    public PreflopRegretTrainer(
        IPreflopRootStateProvider rootStateProvider,
        IPreflopTrajectoryTraverser trajectoryTraverser,
        ITraversalPlayerSelector traversalPlayerSelector,
        IRegretStore regretStore,
        IAverageStrategyStore averageStrategyStore,
        IPreflopTrainingProgressStore? trainingProgressStore = null)
    {
        _rootStateProvider = rootStateProvider ?? throw new ArgumentNullException(nameof(rootStateProvider));
        _trajectoryTraverser = trajectoryTraverser ?? throw new ArgumentNullException(nameof(trajectoryTraverser));
        _traversalPlayerSelector = traversalPlayerSelector ?? throw new ArgumentNullException(nameof(traversalPlayerSelector));
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
        _averageStrategyStore = averageStrategyStore ?? throw new ArgumentNullException(nameof(averageStrategyStore));
        _trainingProgressStore = trainingProgressStore ?? NullPreflopTrainingProgressStore.Instance;
    }

    public void RunIteration(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var rootState = _rootStateProvider.CreateRootState();
        var traversalPlayerId = _traversalPlayerSelector.Select(rootState);
        var sample = _trajectoryTraverser.SampleTrajectory(rootState, rng);

        foreach (var node in sample.Path)
        {
            if (node.NodeKind != TraversalNodeKind.Action)
                continue;

            if (node.ActingPlayerId != traversalPlayerId)
                continue;

            if (string.IsNullOrWhiteSpace(node.InfoSetKey) || node.StateBeforeAction is null || node.LegalActions.Count == 0)
                continue;

            var actionValues = EvaluateActionValues(node.StateBeforeAction, traversalPlayerId, node.LegalActions, rng);
            var nodeValue = 0d;

            foreach (var action in node.LegalActions)
            {
                var policyProbability = node.Policy.TryGetValue(action, out var probability)
                    ? probability
                    : 0d;

                nodeValue += policyProbability * actionValues[action];
            }

            foreach (var action in node.LegalActions)
            {
                var regretDelta = actionValues[action] - nodeValue;
                _regretStore.Add(node.InfoSetKey, action, regretDelta);
            }

            foreach (var action in node.LegalActions)
            {
                var probability = node.Policy.TryGetValue(action, out var policyProbability)
                    ? policyProbability
                    : 0d;

                _averageStrategyStore.Add(node.InfoSetKey, action, probability);
            }

            // Future hook: MCCFR-style weighting can adjust regretDelta before Add.
        }

        _trainingProgressStore.IncrementIterations(1);
    }

    public PreflopTrainingResult RunTraining(
        PreflopTrainingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= PreflopTrainingOptions.Default;
        options.Validate();

        var rng = new Random();
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
            ReachedIterationLimit = reachedIterationLimit
        };
    }

    private Dictionary<LegalAction, double> EvaluateActionValues(
        SolverHandState stateBeforeAction,
        PlayerId traversalPlayerId,
        IReadOnlyList<LegalAction> legalActions,
        Random rng)
    {
        var actionValues = new Dictionary<LegalAction, double>(legalActions.Count);

        foreach (var action in legalActions)
        {
            var afterActionState = stateBeforeAction.Apply(action);
            var rollout = _trajectoryTraverser.SampleTrajectory(afterActionState, rng);
            var utility = rollout.UtilityByPlayer.TryGetValue(traversalPlayerId, out var value)
                ? value
                : 0d;

            actionValues[action] = utility;
        }

        return actionValues;
    }
}
