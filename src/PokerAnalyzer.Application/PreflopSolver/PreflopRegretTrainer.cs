using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopSolver;

public interface IRegretStore
{
    void Add(string infoSetKey, LegalAction action, double regretDelta);
    double Get(string infoSetKey, LegalAction action);
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

public sealed class PreflopRegretTrainer
{
    private readonly IPreflopRootStateProvider _rootStateProvider;
    private readonly IPreflopTrajectoryTraverser _trajectoryTraverser;
    private readonly ITraversalPlayerSelector _traversalPlayerSelector;
    private readonly IRegretStore _regretStore;

    public PreflopRegretTrainer(
        IPreflopRootStateProvider rootStateProvider,
        IPreflopTrajectoryTraverser trajectoryTraverser,
        ITraversalPlayerSelector traversalPlayerSelector,
        IRegretStore regretStore)
    {
        _rootStateProvider = rootStateProvider ?? throw new ArgumentNullException(nameof(rootStateProvider));
        _trajectoryTraverser = trajectoryTraverser ?? throw new ArgumentNullException(nameof(trajectoryTraverser));
        _traversalPlayerSelector = traversalPlayerSelector ?? throw new ArgumentNullException(nameof(traversalPlayerSelector));
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
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

            // Future hook: average-strategy accumulation can be added here.
            // Future hook: MCCFR-style weighting can adjust regretDelta before Add.
        }
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
