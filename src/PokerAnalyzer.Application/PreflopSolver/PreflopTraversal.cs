using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopSolver;

public interface IPreflopRootStateProvider
{
    SolverHandState CreateRootState();
}

public interface IPreflopInfoSetMapper
{
    string MapInfoSetKey(SolverHandState state, PlayerId actingPlayerId);
}

public interface IPreflopPolicyProvider
{
    bool TryGetPolicy(string infoSetKey, IReadOnlyList<LegalAction> legalActions, out IReadOnlyDictionary<LegalAction, double> policy);
}

public interface IActionSampler
{
    LegalAction Sample(IReadOnlyList<LegalAction> legalActions, IReadOnlyDictionary<LegalAction, double> policy, Random rng);
}

public interface IPreflopLeafEvaluator
{
    PreflopLeafEvaluation Evaluate(SolverHandState leafState);
}

public interface IPreflopLeafDetector
{
    bool IsLeaf(SolverHandState state);
}

public sealed class PreflopTrajectoryTraverser
{
    private readonly IPreflopRootStateProvider _rootStateProvider;
    private readonly IChanceSampler _chanceSampler;
    private readonly IPreflopInfoSetMapper _infoSetMapper;
    private readonly IPreflopPolicyProvider _policyProvider;
    private readonly IActionSampler _actionSampler;
    private readonly IPreflopLeafEvaluator _leafEvaluator;
    private readonly IPreflopLeafDetector _leafDetector;

    public PreflopTrajectoryTraverser(
        IPreflopRootStateProvider rootStateProvider,
        IChanceSampler chanceSampler,
        IPreflopInfoSetMapper infoSetMapper,
        IPreflopPolicyProvider policyProvider,
        IActionSampler actionSampler,
        IPreflopLeafEvaluator leafEvaluator,
        IPreflopLeafDetector leafDetector)
    {
        _rootStateProvider = rootStateProvider ?? throw new ArgumentNullException(nameof(rootStateProvider));
        _chanceSampler = chanceSampler ?? throw new ArgumentNullException(nameof(chanceSampler));
        _infoSetMapper = infoSetMapper ?? throw new ArgumentNullException(nameof(infoSetMapper));
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
        _actionSampler = actionSampler ?? throw new ArgumentNullException(nameof(actionSampler));
        _leafEvaluator = leafEvaluator ?? throw new ArgumentNullException(nameof(leafEvaluator));
        _leafDetector = leafDetector ?? throw new ArgumentNullException(nameof(leafDetector));
    }

    public TrajectorySample RunIteration(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return SampleTrajectory(_rootStateProvider.CreateRootState(), rng);
    }

    public TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rootState);
        ArgumentNullException.ThrowIfNull(rng);

        var visited = new List<VisitedNode>();
        var current = rootState;

        while (true)
        {
            if (_leafDetector.IsLeaf(current))
            {
                var leafEvaluation = _leafEvaluator.Evaluate(current);
                visited.Add(VisitedNode.CreateLeaf(visited.Count, current.Street, leafEvaluation.Reason));

                return new TrajectorySample(current, leafEvaluation.UtilityByPlayer, visited);
            }

            if (_chanceSampler.IsChanceNode(current))
            {
                var next = _chanceSampler.Sample(current, rng);
                visited.Add(VisitedNode.CreateChance(visited.Count, current.Street, next.Street, "sampled chance outcome"));
                current = next;
                continue;
            }

            var legalActions = current.GenerateLegalActions();
            if (legalActions.Count == 0)
            {
                var noActionEvaluation = _leafEvaluator.Evaluate(current);
                visited.Add(VisitedNode.CreateLeaf(visited.Count, current.Street, noActionEvaluation.Reason));
                return new TrajectorySample(current, noActionEvaluation.UtilityByPlayer, visited);
            }

            var actingPlayerId = current.ActingPlayerId;
            var infoSetKey = _infoSetMapper.MapInfoSetKey(current, actingPlayerId);
            var policy = _policyProvider.TryGetPolicy(infoSetKey, legalActions, out var learnedPolicy)
                ? learnedPolicy
                : UniformPolicyBuilder.Build(legalActions);

            var sampledAction = _actionSampler.Sample(legalActions, policy, rng);
            visited.Add(VisitedNode.CreateAction(visited.Count, current.Street, actingPlayerId, infoSetKey, legalActions, policy, sampledAction));

            current = current.Apply(sampledAction);
        }
    }
}

public sealed record TrajectorySample(
    SolverHandState FinalState,
    IReadOnlyDictionary<PlayerId, double> UtilityByPlayer,
    IReadOnlyList<VisitedNode> Path);

public enum TraversalNodeKind : byte
{
    Chance = 0,
    Action = 1,
    Leaf = 2
}

public sealed record VisitedNode(
    int Depth,
    TraversalNodeKind NodeKind,
    Street Street,
    PlayerId? ActingPlayerId,
    string? InfoSetKey,
    IReadOnlyList<LegalAction> LegalActions,
    IReadOnlyDictionary<LegalAction, double> Policy,
    LegalAction? SampledAction,
    string? Note)
{
    public static VisitedNode CreateChance(int depth, Street street, Street nextStreet, string note)
        => new(depth, TraversalNodeKind.Chance, street, null, null, Array.Empty<LegalAction>(), new Dictionary<LegalAction, double>(), null, $"{note} ({street}->{nextStreet})");

    public static VisitedNode CreateAction(
        int depth,
        Street street,
        PlayerId actingPlayerId,
        string infoSetKey,
        IReadOnlyList<LegalAction> legalActions,
        IReadOnlyDictionary<LegalAction, double> policy,
        LegalAction sampledAction)
        => new(depth, TraversalNodeKind.Action, street, actingPlayerId, infoSetKey, legalActions, policy, sampledAction, null);

    public static VisitedNode CreateLeaf(int depth, Street street, string note)
        => new(depth, TraversalNodeKind.Leaf, street, null, null, Array.Empty<LegalAction>(), new Dictionary<LegalAction, double>(), null, note);
}

public sealed record PreflopLeafEvaluation(
    IReadOnlyDictionary<PlayerId, double> UtilityByPlayer,
    string Reason);

public static class UniformPolicyBuilder
{
    public static IReadOnlyDictionary<LegalAction, double> Build(IReadOnlyList<LegalAction> legalActions)
    {
        if (legalActions is null)
            throw new ArgumentNullException(nameof(legalActions));

        if (legalActions.Count == 0)
            throw new ArgumentException("At least one legal action is required to build a policy.", nameof(legalActions));

        var probability = 1d / legalActions.Count;
        var weights = new Dictionary<LegalAction, double>(legalActions.Count);
        foreach (var action in legalActions)
            weights[action] = probability;

        return weights;
    }
}

public sealed class WeightedRandomActionSampler : IActionSampler
{
    public LegalAction Sample(IReadOnlyList<LegalAction> legalActions, IReadOnlyDictionary<LegalAction, double> policy, Random rng)
    {
        ArgumentNullException.ThrowIfNull(legalActions);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(rng);

        if (legalActions.Count == 0)
            throw new ArgumentException("At least one legal action is required.", nameof(legalActions));

        var totalWeight = 0d;
        foreach (var action in legalActions)
        {
            if (!policy.TryGetValue(action, out var weight) || weight <= 0d)
                continue;

            totalWeight += weight;
        }

        if (totalWeight <= 0d)
            return legalActions[rng.Next(legalActions.Count)];

        var roll = rng.NextDouble() * totalWeight;
        var cumulative = 0d;

        foreach (var action in legalActions)
        {
            if (!policy.TryGetValue(action, out var weight) || weight <= 0d)
                continue;

            cumulative += weight;
            if (roll <= cumulative)
                return action;
        }

        return legalActions[^1];
    }
}

public sealed class DefaultPreflopLeafDetector : IPreflopLeafDetector
{
    public bool IsLeaf(SolverHandState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Street != Street.Preflop)
            return true;

        var activePlayers = state.Players.Count(p => p.IsActive);
        return activePlayers <= 1;
    }
}

public sealed class PlaceholderPreflopLeafEvaluator : IPreflopLeafEvaluator
{
    public PreflopLeafEvaluation Evaluate(SolverHandState leafState)
    {
        ArgumentNullException.ThrowIfNull(leafState);

        var utility = leafState.Players.ToDictionary(player => player.PlayerId, _ => 0d);
        var reason = leafState.Street == Street.Preflop
            ? "preflop terminal placeholder utility"
            : "preflop cutoff placeholder utility";

        return new PreflopLeafEvaluation(utility, reason);
    }
}

public sealed class PublicStateInfoSetMapper : IPreflopInfoSetMapper
{
    public string MapInfoSetKey(SolverHandState state, PlayerId actingPlayerId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var acting = state.Players.FirstOrDefault(player => player.PlayerId == actingPlayerId)
            ?? throw new InvalidOperationException($"Acting player {actingPlayerId} not found.");

        var privateCards = state.PrivateCardsByPlayer.TryGetValue(actingPlayerId, out var holeCards)
            ? $"{holeCards.First}-{holeCards.Second}"
            : "unknown";

        return string.Join('|',
            $"street={state.Street}",
            $"position={acting.Position}",
            $"hero={privateCards}",
            $"history={state.ActionHistorySignature}",
            $"pot={state.Pot.Value}",
            $"bet={state.CurrentBetSize.Value}",
            $"toCall={state.ToCall.Value}");
    }
}

public sealed class InMemoryPolicyProvider : IPreflopPolicyProvider
{
    private readonly Dictionary<string, IReadOnlyDictionary<LegalAction, double>> _policyByInfoSet = new(StringComparer.Ordinal);

    public bool TryGetPolicy(string infoSetKey, IReadOnlyList<LegalAction> legalActions, out IReadOnlyDictionary<LegalAction, double> policy)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);
        ArgumentNullException.ThrowIfNull(legalActions);

        if (!_policyByInfoSet.TryGetValue(infoSetKey, out var storedPolicy))
        {
            policy = new Dictionary<LegalAction, double>();
            return false;
        }

        var filtered = new Dictionary<LegalAction, double>(legalActions.Count);
        foreach (var legalAction in legalActions)
        {
            if (storedPolicy.TryGetValue(legalAction, out var probability) && probability > 0d)
                filtered[legalAction] = probability;
        }

        if (filtered.Count == 0)
        {
            policy = new Dictionary<LegalAction, double>();
            return false;
        }

        policy = filtered;
        return true;
    }

    public void Store(string infoSetKey, IReadOnlyDictionary<LegalAction, double> policy)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);
        ArgumentNullException.ThrowIfNull(policy);
        _policyByInfoSet[infoSetKey] = new Dictionary<LegalAction, double>(policy);
    }
}

public sealed class FixedRootStateProvider : IPreflopRootStateProvider
{
    private readonly SolverHandState _root;

    public FixedRootStateProvider(SolverHandState root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public SolverHandState CreateRootState() => _root;
}
