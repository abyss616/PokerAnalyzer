using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopRegretTrainerTests
{
    [Fact]
    public void RunIteration_UsesRegretMatchedTraversalPolicy_AndUpdatesRegretAsActionValueMinusNodeValue()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var regrets = new InMemoryRegretStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        regrets.Add("traversal_infoset", fold, 3d);
        regrets.Add("traversal_infoset", call, 1d);

        var traverser = new RegretAwareStubTrajectoryTraverser(
            root,
            traversalPlayer,
            opponent,
            new RegretMatchingPolicyProvider(regrets),
            fold,
            call);

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            new InMemoryAverageStrategyStore());

        trainer.RunIteration(new Random(7));

        Assert.Equal(0.75d, traverser.InitialTraversalPolicy[fold], 10);
        Assert.Equal(0.25d, traverser.InitialTraversalPolicy[call], 10);

        // fold utility = 10, call utility = 4, node value = 0.75*10 + 0.25*4 = 8.5
        // regret deltas: fold +1.5, call -4.5
        Assert.Equal(4.5d, regrets.Get("traversal_infoset", fold), 10);
        Assert.Equal(-3.5d, regrets.Get("traversal_infoset", call), 10);
    }

    [Fact]
    public void RunIteration_WhenAllLegalRegretsNonPositive_UsesUniformTraversalPolicy()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var regrets = new InMemoryRegretStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        regrets.Add("traversal_infoset", fold, -2d);
        regrets.Add("traversal_infoset", call, 0d);

        var traverser = new RegretAwareStubTrajectoryTraverser(
            root,
            traversalPlayer,
            opponent,
            new RegretMatchingPolicyProvider(regrets),
            fold,
            call);

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            new InMemoryAverageStrategyStore());

        trainer.RunIteration(new Random(19));

        Assert.Equal(0.5d, traverser.InitialTraversalPolicy[fold], 10);
        Assert.Equal(0.5d, traverser.InitialTraversalPolicy[call], 10);
    }

    [Fact]
    public void RunIteration_ReplaysActionsFromStateBeforeAction()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var regrets = new InMemoryRegretStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var traverser = new RegretAwareStubTrajectoryTraverser(
            root,
            traversalPlayer,
            opponent,
            new RegretMatchingPolicyProvider(regrets),
            fold,
            call);

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            new InMemoryAverageStrategyStore());

        trainer.RunIteration(new Random(31));

        Assert.NotEmpty(traverser.RolloutRoots);
        Assert.All(traverser.RolloutRoots, state => Assert.Equal(root.ActionHistory.Count + 1, state.ActionHistory.Count));
    }


    [Fact]
    public void RunIteration_AccumulatesAverageStrategy_ForTraversalPlayerOnly()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var regrets = new InMemoryRegretStore();
        var averageStrategy = new InMemoryAverageStrategyStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        regrets.Add("traversal_infoset", fold, 3d);
        regrets.Add("traversal_infoset", call, 1d);

        var traverser = new RegretAwareStubTrajectoryTraverser(
            root,
            traversalPlayer,
            opponent,
            new RegretMatchingPolicyProvider(regrets),
            fold,
            call);

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            averageStrategy);

        trainer.RunIteration(new Random(5));

        Assert.Equal(0.75d, averageStrategy.Get("traversal_infoset", fold), 10);
        Assert.Equal(0.25d, averageStrategy.Get("traversal_infoset", call), 10);
        Assert.Equal(0d, averageStrategy.Get("opponent_infoset", fold), 10);
        Assert.Equal(0d, averageStrategy.Get("opponent_infoset", call), 10);
    }

    [Fact]
    public void RunIteration_AccumulatesAverageStrategyAcrossIterations()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;

        var regrets = new InMemoryRegretStore();
        var averageStrategy = new InMemoryAverageStrategyStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var policy = new Dictionary<LegalAction, double>
        {
            [fold] = 0.6d,
            [call] = 0.4d
        };

        var traverser = new StaticPolicyTrajectoryTraverser(root, traversalPlayer, fold, call, policy);

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            averageStrategy);

        trainer.RunIteration(new Random(11));
        trainer.RunIteration(new Random(12));

        Assert.Equal(1.2d, averageStrategy.Get("traversal_infoset", fold), 10);
        Assert.Equal(0.8d, averageStrategy.Get("traversal_infoset", call), 10);
    }

    [Fact]
    public void RunIteration_WhenPolicyMissingLegalAction_TreatsMissingProbabilityAsZero()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;

        var regrets = new InMemoryRegretStore();
        var averageStrategy = new InMemoryAverageStrategyStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(4));
        var partialPolicy = new Dictionary<LegalAction, double>
        {
            [fold] = 1d
        };

        var traverser = new StaticPolicyTrajectoryTraverser(root, traversalPlayer, fold, call, raise, partialPolicy);

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            averageStrategy);

        trainer.RunIteration(new Random(13));

        Assert.Equal(1d, averageStrategy.Get("traversal_infoset", fold), 10);
        Assert.Equal(0d, averageStrategy.Get("traversal_infoset", call), 10);
        Assert.Equal(0d, averageStrategy.Get("traversal_infoset", raise), 10);
    }

    private static SolverHandState CreateHeadsUpPreflopState()
    {
        var sbId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var bbId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var config = new GameConfig(
            MaxPlayers: 2,
            SmallBlind: new ChipAmount(1),
            BigBlind: new ChipAmount(2),
            Ante: ChipAmount.Zero,
            StartingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(
                sbId,
                SeatIndex: 0,
                Position: Position.SB,
                Stack: new ChipAmount(99),
                CurrentStreetContribution: new ChipAmount(1),
                TotalContribution: new ChipAmount(1),
                IsFolded: false,
                IsAllIn: false),

            new SolverPlayerState(
                bbId,
                SeatIndex: 1,
                Position: Position.BB,
                Stack: new ChipAmount(98),
                CurrentStreetContribution: new ChipAmount(2),
                TotalContribution: new ChipAmount(2),
                IsFolded: false,
                IsAllIn: false)
        };

        return new SolverHandState(
            config: config,
            street: Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: sbId,
            pot: new ChipAmount(3),
            currentBetSize: new ChipAmount(2),
            lastRaiseSize: new ChipAmount(1),
            raisesThisStreet: 0,
            players: players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(2))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>());
    }

    private sealed class RegretAwareStubTrajectoryTraverser : IPreflopTrajectoryTraverser
    {
        private readonly SolverHandState _root;
        private readonly PlayerId _traversalPlayer;
        private readonly PlayerId _opponent;
        private readonly IPreflopPolicyProvider _policyProvider;
        private readonly LegalAction _fold;
        private readonly LegalAction _call;
        private bool _returnedInitialTrajectory;

        public RegretAwareStubTrajectoryTraverser(
            SolverHandState root,
            PlayerId traversalPlayer,
            PlayerId opponent,
            IPreflopPolicyProvider policyProvider,
            LegalAction fold,
            LegalAction call)
        {
            _root = root;
            _traversalPlayer = traversalPlayer;
            _opponent = opponent;
            _policyProvider = policyProvider;
            _fold = fold;
            _call = call;
        }

        public IReadOnlyDictionary<LegalAction, double> InitialTraversalPolicy { get; private set; } = new Dictionary<LegalAction, double>();

        public List<SolverHandState> RolloutRoots { get; } = new();

        public TrajectorySample RunIteration(Random rng) => SampleTrajectory(_root, rng);

        public TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng)
        {
            if (!_returnedInitialTrajectory)
            {
                _returnedInitialTrajectory = true;

                var legalActions = new[] { _fold, _call };
                _policyProvider.TryGetPolicy("traversal_infoset", legalActions, out var traversalPolicy);
                InitialTraversalPolicy = traversalPolicy;

                var path = new List<VisitedNode>
                {
                    VisitedNode.CreateAction(
                        depth: 0,
                        street: Street.Preflop,
                        actingPlayerId: _traversalPlayer,
                        infoSetKey: "traversal_infoset",
                        legalActions: legalActions,
                        policy: traversalPolicy,
                        sampledAction: _fold,
                        stateBeforeAction: rootState),

                    VisitedNode.CreateAction(
                        depth: 1,
                        street: Street.Preflop,
                        actingPlayerId: _opponent,
                        infoSetKey: "opponent_infoset",
                        legalActions: legalActions,
                        policy: new Dictionary<LegalAction, double> { [_fold] = 0.5d, [_call] = 0.5d },
                        sampledAction: _call,
                        stateBeforeAction: rootState.Apply(_fold)),

                    VisitedNode.CreateLeaf(2, Street.Preflop, "leaf")
                };

                return new TrajectorySample(
                    rootState,
                    new Dictionary<PlayerId, double> { [_traversalPlayer] = 0d, [_opponent] = 0d },
                    path);
            }

            RolloutRoots.Add(rootState);

            var lastAction = rootState.ActionHistory[^1].ActionType;
            var utility = lastAction == ActionType.Fold ? 10d : 4d;

            return new TrajectorySample(
                rootState,
                new Dictionary<PlayerId, double> { [_traversalPlayer] = utility, [_opponent] = -utility },
                new[] { VisitedNode.CreateLeaf(0, rootState.Street, "rollout leaf") });
        }
    }


    private sealed class StaticPolicyTrajectoryTraverser : IPreflopTrajectoryTraverser
    {
        private readonly SolverHandState _root;
        private readonly PlayerId _traversalPlayer;
        private readonly IReadOnlyList<LegalAction> _legalActions;
        private readonly IReadOnlyDictionary<LegalAction, double> _policy;

        public StaticPolicyTrajectoryTraverser(
            SolverHandState root,
            PlayerId traversalPlayer,
            LegalAction first,
            LegalAction second,
            IReadOnlyDictionary<LegalAction, double> policy)
            : this(root, traversalPlayer, new[] { first, second }, policy)
        {
        }

        public StaticPolicyTrajectoryTraverser(
            SolverHandState root,
            PlayerId traversalPlayer,
            LegalAction first,
            LegalAction second,
            LegalAction third,
            IReadOnlyDictionary<LegalAction, double> policy)
            : this(root, traversalPlayer, new[] { first, second, third }, policy)
        {
        }

        private StaticPolicyTrajectoryTraverser(
            SolverHandState root,
            PlayerId traversalPlayer,
            IReadOnlyList<LegalAction> legalActions,
            IReadOnlyDictionary<LegalAction, double> policy)
        {
            _root = root;
            _traversalPlayer = traversalPlayer;
            _legalActions = legalActions;
            _policy = policy;
        }

        public TrajectorySample RunIteration(Random rng) => SampleTrajectory(_root, rng);

        public TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng)
        {
            if (rootState.ActionHistory.Count > _root.ActionHistory.Count)
            {
                return new TrajectorySample(
                    rootState,
                    new Dictionary<PlayerId, double> { [_traversalPlayer] = 0d },
                    new[] { VisitedNode.CreateLeaf(0, rootState.Street, "rollout leaf") });
            }

            var path = new List<VisitedNode>
            {
                VisitedNode.CreateAction(
                    depth: 0,
                    street: Street.Preflop,
                    actingPlayerId: _traversalPlayer,
                    infoSetKey: "traversal_infoset",
                    legalActions: _legalActions,
                    policy: _policy,
                    sampledAction: _legalActions[0],
                    stateBeforeAction: rootState),

                VisitedNode.CreateAction(
                    depth: 1,
                    street: Street.Preflop,
                    actingPlayerId: rootState.Players[1].PlayerId,
                    infoSetKey: "opponent_infoset",
                    legalActions: _legalActions,
                    policy: new Dictionary<LegalAction, double> { [_legalActions[0]] = 1d },
                    sampledAction: _legalActions[0],
                    stateBeforeAction: rootState.Apply(_legalActions[0])),

                VisitedNode.CreateLeaf(2, Street.Preflop, "leaf")
            };

            return new TrajectorySample(
                rootState,
                new Dictionary<PlayerId, double> { [_traversalPlayer] = 0d },
                path);
        }
    }

}
