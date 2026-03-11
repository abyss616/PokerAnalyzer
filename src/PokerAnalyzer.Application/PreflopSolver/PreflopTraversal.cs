using PokerAnalyzer.Domain.Game;
using System.Text.Json;

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
    PreflopLeafEvaluation Evaluate(PreflopLeafEvaluationContext context);
}

public interface IPreflopLeafDetector
{
    bool IsLeaf(SolverHandState state);
}

public interface IPreflopTrajectoryTraverser
{
    TrajectorySample RunIteration(Random rng);
    TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng, PreflopLeafEvaluationContext? evaluationContext = null);
}

public sealed class PreflopTrajectoryTraverser : IPreflopTrajectoryTraverser
{
    private const int MaxTraversalDepth = 256;

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

    public TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng, PreflopLeafEvaluationContext? evaluationContext = null)
    {
        ArgumentNullException.ThrowIfNull(rootState);
        ArgumentNullException.ThrowIfNull(rng);

        var visited = new List<VisitedNode>();
        var current = rootState;

        while (true)
        {
            if (visited.Count >= MaxTraversalDepth)
                throw new InvalidOperationException($"Preflop traversal exceeded max depth of {MaxTraversalDepth}. This usually indicates a non-progress loop.");

            if (SolverTraversalGuards.IsTerminalLikeState(current) || _leafDetector.IsLeaf(current))
            {
                var leafEvaluationContext = (evaluationContext ?? BuildContextFromSampledRootAction(rootState, current, visited)) with { LeafState = current };
                var leafEvaluation = _leafEvaluator.Evaluate(leafEvaluationContext);
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
                var leafEvaluationContext = (evaluationContext ?? BuildContextFromSampledRootAction(rootState, current, visited)) with { LeafState = current };
                var noActionEvaluation = _leafEvaluator.Evaluate(leafEvaluationContext);
                visited.Add(VisitedNode.CreateLeaf(visited.Count, current.Street, noActionEvaluation.Reason));
                return new TrajectorySample(current, noActionEvaluation.UtilityByPlayer, visited);
            }

            var actingPlayerId = current.ActingPlayerId;
            var infoSetKey = _infoSetMapper.MapInfoSetKey(current, actingPlayerId);
            var policy = _policyProvider.TryGetPolicy(infoSetKey, legalActions, out var learnedPolicy)
                ? learnedPolicy
                : UniformPolicyBuilder.Build(legalActions);

            var sampledAction = _actionSampler.Sample(legalActions, policy, rng);
            visited.Add(VisitedNode.CreateAction(visited.Count, current.Street, actingPlayerId, infoSetKey, legalActions, policy, sampledAction, current));

            current = SolverStateStepper.Step(current, sampledAction, legalActions);
        }
    }

    private static PreflopLeafEvaluationContext BuildContextFromSampledRootAction(
        SolverHandState rootState,
        SolverHandState leafState,
        IReadOnlyList<VisitedNode> visited)
    {
        var firstActionNode = visited.FirstOrDefault(node => node.NodeKind == TraversalNodeKind.Action && node.SampledAction is not null);
        if (firstActionNode?.ActingPlayerId is not PlayerId heroPlayerId || firstActionNode.SampledAction is null)
            throw new InvalidOperationException("Cannot build preflop leaf evaluation context because no sampled action node was visited.");

        var hero = rootState.Players.FirstOrDefault(player => player.PlayerId == heroPlayerId)
            ?? throw new InvalidOperationException($"Acting player {heroPlayerId} was not found in the root state.");

        if (!rootState.PrivateCardsByPlayer.TryGetValue(heroPlayerId, out var heroCards))
            throw new InvalidOperationException($"Missing private cards for acting player {heroPlayerId}.");

        var rootEffectiveStackBb = ResolveEffectiveStackBb(rootState, heroPlayerId);
        return new PreflopLeafEvaluationContext(
            rootState,
            leafState,
            heroPlayerId,
            hero.Position,
            heroCards,
            rootEffectiveStackBb,
            firstActionNode.SampledAction,
            firstActionNode.InfoSetKey);
    }

    private static double ResolveEffectiveStackBb(SolverHandState state, PlayerId heroPlayerId)
    {
        var hero = state.Players.FirstOrDefault(player => player.PlayerId == heroPlayerId)
            ?? throw new InvalidOperationException($"Hero player {heroPlayerId} was not found in the provided state.");

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
    string? Note,
    SolverHandState? StateBeforeAction)
{
    public static VisitedNode CreateChance(int depth, Street street, Street nextStreet, string note)
        => new(depth, TraversalNodeKind.Chance, street, null, null, Array.Empty<LegalAction>(), new Dictionary<LegalAction, double>(), null, $"{note} ({street}->{nextStreet})", null);

    public static VisitedNode CreateAction(
        int depth,
        Street street,
        PlayerId actingPlayerId,
        string infoSetKey,
        IReadOnlyList<LegalAction> legalActions,
        IReadOnlyDictionary<LegalAction, double> policy,
        LegalAction sampledAction,
        SolverHandState stateBeforeAction)
        => new(depth, TraversalNodeKind.Action, street, actingPlayerId, infoSetKey, legalActions, policy, sampledAction, null, stateBeforeAction);

    public static VisitedNode CreateLeaf(int depth, Street street, string note)
        => new(depth, TraversalNodeKind.Leaf, street, null, null, Array.Empty<LegalAction>(), new Dictionary<LegalAction, double>(), null, note, null);
}

public sealed record PreflopLeafEvaluation(
    IReadOnlyDictionary<PlayerId, double> UtilityByPlayer,
    string Reason);

public sealed record PreflopLeafEvaluationContext(
    SolverHandState RootState,
    SolverHandState LeafState,
    PlayerId HeroPlayerId,
    Position HeroPosition,
    Domain.Cards.HoleCards HeroCards,
    double RootEffectiveStackBb,
    LegalAction RootAction,
    string? SolverKey);

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

        return SolverTraversalGuards.IsTerminalLikeState(state);
    }
}

public sealed class PlaceholderPreflopLeafEvaluator : IPreflopLeafEvaluator
{
    public PreflopLeafEvaluation Evaluate(PreflopLeafEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var leafState = context.LeafState;

        var utility = leafState.Players.ToDictionary(player => player.PlayerId, _ => 0d);
        var reason = leafState.Street == Street.Preflop
            ? "preflop terminal placeholder utility"
            : "preflop cutoff placeholder utility";

        return new PreflopLeafEvaluation(utility, reason);
    }
}

public sealed class PreflopInfoSetMapper : IPreflopInfoSetMapper
{
    public string MapInfoSetKey(SolverHandState state, PlayerId actingPlayerId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var acting = state.Players.FirstOrDefault(player => player.PlayerId == actingPlayerId)
            ?? throw new InvalidOperationException($"Acting player {actingPlayerId} not found.");

        var privateCards = state.PrivateCardsByPlayer.TryGetValue(actingPlayerId, out var holeCards)
            ? ToCanonicalPreflopHand(holeCards)
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

    private static string ToCanonicalPreflopHand(Domain.Cards.HoleCards holeCards)
    {
        var first = holeCards.First;
        var second = holeCards.Second;

        if (first.Rank == second.Rank)
        {
            var pairRank = RankToChar(first.Rank);
            return string.Concat(pairRank, pairRank);
        }

        var (high, low) = first.Rank > second.Rank
            ? (first, second)
            : (second, first);

        var suitedness = first.Suit == second.Suit ? 's' : 'o';
        return string.Concat(RankToChar(high.Rank), RankToChar(low.Rank), suitedness);
    }

    private static char RankToChar(Domain.Cards.Rank rank) => rank switch
    {
        Domain.Cards.Rank.Two => '2',
        Domain.Cards.Rank.Three => '3',
        Domain.Cards.Rank.Four => '4',
        Domain.Cards.Rank.Five => '5',
        Domain.Cards.Rank.Six => '6',
        Domain.Cards.Rank.Seven => '7',
        Domain.Cards.Rank.Eight => '8',
        Domain.Cards.Rank.Nine => '9',
        Domain.Cards.Rank.Ten => 'T',
        Domain.Cards.Rank.Jack => 'J',
        Domain.Cards.Rank.Queen => 'Q',
        Domain.Cards.Rank.King => 'K',
        Domain.Cards.Rank.Ace => 'A',
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };
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

        var filteredTotal = filtered.Values.Sum();
        if (filteredTotal <= 0d)
        {
            policy = new Dictionary<LegalAction, double>();
            return false;
        }

        var normalized = new Dictionary<LegalAction, double>(filtered.Count);
        foreach (var (action, probability) in filtered)
            normalized[action] = probability / filteredTotal;

        policy = normalized;
        return true;
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
