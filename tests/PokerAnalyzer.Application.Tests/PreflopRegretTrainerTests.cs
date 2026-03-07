using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopRegretTrainerTests
{
    [Fact]
    public void RunIteration_UpdatesOnlyTraversalPlayerInfosets()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var traverser = new StubTrajectoryTraverser(root, traversalPlayer, opponent);
        var regrets = new InMemoryRegretStore();
        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets);

        trainer.RunIteration(new Random(1));

        Assert.NotEqual(0d, regrets.Get("traversal_infoset", traverser.Fold));
        Assert.NotEqual(0d, regrets.Get("traversal_infoset", traverser.Call));
        Assert.Equal(0d, regrets.Get("opponent_infoset", traverser.Fold));
        Assert.Equal(0d, regrets.Get("opponent_infoset", traverser.Call));
    }

    [Fact]
    public void RunIteration_UpdatesAllLegalActions_AndUsesActionValueMinusNodeValue()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var traverser = new StubTrajectoryTraverser(root, traversalPlayer, opponent);
        var regrets = new InMemoryRegretStore();
        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets);

        trainer.RunIteration(new Random(7));

        // fold utility = 10, call utility = 4, node value = 0.25*10 + 0.75*4 = 5.5
        Assert.Equal(4.5d, regrets.Get("traversal_infoset", traverser.Fold), 10);
        Assert.Equal(-1.5d, regrets.Get("traversal_infoset", traverser.Call), 10);
    }

    [Fact]
    public void RunIteration_DoesNotUpdateRegretsForChanceOrLeafNodes()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var traverser = new StubTrajectoryTraverser(root, traversalPlayer, opponent);
        var regrets = new InMemoryRegretStore();
        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets);

        trainer.RunIteration(new Random(13));

        Assert.Equal(0d, regrets.Get("chance_infoset", traverser.Fold));
        Assert.Equal(0d, regrets.Get("leaf_infoset", traverser.Fold));
    }


    [Fact]
 
    public void RunIteration_ReplaysActionsFromStateBeforeAction()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var traverser = new StubTrajectoryTraverser(root, traversalPlayer, opponent);
        var regrets = new InMemoryRegretStore();
        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets);

        trainer.RunIteration(new Random(19));

        Assert.NotEmpty(traverser.RolloutRoots);
        Assert.All(
            traverser.RolloutRoots,
            state => Assert.Equal(root.ActionHistory.Count + 1, state.ActionHistory.Count));
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

    private sealed class StubTrajectoryTraverser : IPreflopTrajectoryTraverser
    {
        private readonly SolverHandState _root;
        private readonly PlayerId _traversalPlayer;
        private readonly PlayerId _opponent;
        private bool _returnedInitialTrajectory;

        public StubTrajectoryTraverser(SolverHandState root, PlayerId traversalPlayer, PlayerId opponent)
        {
            _root = root;
            _traversalPlayer = traversalPlayer;
            _opponent = opponent;
        }

        public LegalAction Fold { get; } = new(ActionType.Fold);
        public LegalAction Call { get; } = new(ActionType.Call, new ChipAmount(1));

        public List<SolverHandState> RolloutRoots { get; } = new();

        public TrajectorySample RunIteration(Random rng) => SampleTrajectory(_root, rng);

        public TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng)
        {
            if (!_returnedInitialTrajectory)
            {
                _returnedInitialTrajectory = true;

                var path = new List<VisitedNode>
            {
                VisitedNode.CreateAction(
                    depth: 0,
                    street: Street.Preflop,
                    actingPlayerId: _traversalPlayer,
                    infoSetKey: "traversal_infoset",
                    legalActions: new[] { Fold, Call },
                    policy: new Dictionary<LegalAction, double> { [Fold] = 0.25d, [Call] = 0.75d },
                    sampledAction: Fold,
                    stateBeforeAction: rootState),

                VisitedNode.CreateAction(
                    depth: 1,
                    street: Street.Preflop,
                    actingPlayerId: _opponent,
                    infoSetKey: "opponent_infoset",
                    legalActions: new[] { Fold, Call },
                    policy: new Dictionary<LegalAction, double> { [Fold] = 0.5d, [Call] = 0.5d },
                    sampledAction: Call,
                    stateBeforeAction: rootState.Apply(Fold)),

                VisitedNode.CreateChance(2, Street.Preflop, Street.Flop, "sample chance"),
                VisitedNode.CreateLeaf(3, Street.Flop, "leaf")
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
}
