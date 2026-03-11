using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopTrajectoryTraverserTests
{
    [Fact]
    public void SampleTrajectory_WhenNoPolicyExists_FallsBackToUniformPolicyAndReturnsLeafSample()
    {
        var state = CreateHeadsUpPreflopState();
        var actionSampler = new CapturingActionSampler();
        var traverser = new PreflopTrajectoryTraverser(
            new FixedRootStateProvider(state),
            new SolverChanceSampler(),
            new PreflopInfoSetMapper(),
            new InMemoryPolicyProvider(),
            actionSampler,
            new PlaceholderPreflopLeafEvaluator(),
            new DefaultPreflopLeafDetector());

        var result = traverser.RunIteration(new Random(7));

        var firstActionNode = Assert.Single(result.Path.Where(node => node.NodeKind == TraversalNodeKind.Action));
        Assert.NotNull(firstActionNode.SampledAction);
        Assert.Equal(ActionType.Fold, firstActionNode.SampledAction!.ActionType);

        var probabilities = firstActionNode.Policy.Values.ToArray();
        Assert.NotEmpty(probabilities);
        Assert.All(probabilities, p => Assert.Equal(probabilities[0], p, precision: 10));

        Assert.Equal(TraversalNodeKind.Leaf, result.Path[^1].NodeKind);
        Assert.True(result.UtilityByPlayer.Count > 0);
        Assert.Contains(result.Path, node => node.NodeKind == TraversalNodeKind.Action);
        Assert.Equal(1, actionSampler.SampleCalls);
    }


    [Fact]
    public void UnopenedRoot_ExpandsDistinctBranchesAfterLimpVsRaiseToTwoPointFiveBb()
    {
        var root = CreateHeadsUpPreflopState();
        var actions = root.GenerateLegalActions();

        var limp = Assert.Single(actions.Where(action => action.ActionType == ActionType.Call));
        var raise = Assert.Single(actions.Where(action => action.ActionType == ActionType.Raise));

        Assert.Equal(1, limp.Amount!.Value.Value);
        Assert.Equal(5, raise.Amount!.Value.Value);

        var afterLimp = root.Apply(limp);
        var afterRaise = root.Apply(raise);

        Assert.NotEqual(afterLimp.ActionHistorySignature, afterRaise.ActionHistorySignature);
        Assert.Contains(":2:2", afterLimp.ActionHistorySignature, StringComparison.Ordinal);
        Assert.Contains(":4:5", afterRaise.ActionHistorySignature, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleTrajectory_WhenPreflopRoundAlreadyClosed_StopsAtPreflopTerminalWithoutChanceSampling()
    {
        var state = CreateClosedPreflopStateWithoutPrivateCards();
        var traverser = new PreflopTrajectoryTraverser(
            new FixedRootStateProvider(state),
            new SolverChanceSampler(),
            new PreflopInfoSetMapper(),
            new InMemoryPolicyProvider(),
            new WeightedRandomActionSampler(),
            new PlaceholderPreflopLeafEvaluator(),
            new DefaultPreflopLeafDetector());

        var result = traverser.RunIteration(new Random(17));

        Assert.Equal(Street.Preflop, result.FinalState.Street);
        Assert.Empty(result.FinalState.BoardCards);
        Assert.DoesNotContain(result.Path, node => node.NodeKind == TraversalNodeKind.Chance);
        Assert.Equal(TraversalNodeKind.Leaf, result.Path[^1].NodeKind);
        Assert.Equal("preflop terminal placeholder utility", result.Path[^1].Note);
    }



    [Fact]
    public void SampleTrajectory_WhenOnlyOneActivePlayer_Remains_ExitsImmediatelyWithoutInvokingLeafDetector()
    {
        var state = CreateOneActivePlayerPreflopState();
        var leafDetector = new CountingLeafDetector();
        var traverser = new PreflopTrajectoryTraverser(
            new FixedRootStateProvider(state),
            new SolverChanceSampler(),
            new PreflopInfoSetMapper(),
            new InMemoryPolicyProvider(),
            new WeightedRandomActionSampler(),
            new PlaceholderPreflopLeafEvaluator(),
            leafDetector);

        var result = traverser.RunIteration(new Random(11));

        Assert.Equal(0, leafDetector.Calls);
        Assert.Equal(TraversalNodeKind.Leaf, result.Path[^1].NodeKind);
    }

    [Fact]
    public void SampleTrajectory_WhenNoActionablePlayersRemain_ExitsImmediatelyWithoutInvokingLeafDetector()
    {
        var state = CreateNoActionablePlayersPreflopState();
        var leafDetector = new CountingLeafDetector();
        var traverser = new PreflopTrajectoryTraverser(
            new FixedRootStateProvider(state),
            new SolverChanceSampler(),
            new PreflopInfoSetMapper(),
            new InMemoryPolicyProvider(),
            new WeightedRandomActionSampler(),
            new PlaceholderPreflopLeafEvaluator(),
            leafDetector);

        var result = traverser.RunIteration(new Random(13));

        Assert.Equal(0, leafDetector.Calls);
        Assert.Equal(TraversalNodeKind.Leaf, result.Path[^1].NodeKind);
    }

    [Fact]
    public void SampleTrajectory_WhenStateIsNonTerminal_StillUsesLeafDetectorPath()
    {
        var state = CreateHeadsUpPreflopState();
        var leafDetector = new CountingLeafDetector();
        var traverser = new PreflopTrajectoryTraverser(
            new FixedRootStateProvider(state),
            new SolverChanceSampler(),
            new PreflopInfoSetMapper(),
            new InMemoryPolicyProvider(),
            new WeightedRandomActionSampler(),
            new PlaceholderPreflopLeafEvaluator(),
            leafDetector);

        _ = traverser.RunIteration(new Random(19));

        Assert.True(leafDetector.Calls > 0);
    }

    [Fact]
    public void SampleTrajectory_WhenTraversalDoesNotProgress_ThrowsDepthGuardException()
    {
        var state = CreateHeadsUpPreflopState();
        var traverser = new PreflopTrajectoryTraverser(
            new FixedRootStateProvider(state),
            new NonProgressingChanceSampler(),
            new PreflopInfoSetMapper(),
            new InMemoryPolicyProvider(),
            new WeightedRandomActionSampler(),
            new PlaceholderPreflopLeafEvaluator(),
            new DefaultPreflopLeafDetector());

        var ex = Assert.Throws<InvalidOperationException>(() => traverser.RunIteration(new Random(123)));
        Assert.Contains("exceeded max depth", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SolverHandState CreateHeadsUpPreflopState()
    {
        var sbId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var bbId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var config = new GameConfig(MaxPlayers: 2, SmallBlind: new ChipAmount(1), BigBlind: new ChipAmount(2), Ante: ChipAmount.Zero, StartingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(sbId, SeatIndex: 0, Position.SB, Stack: new ChipAmount(99), CurrentStreetContribution: new ChipAmount(1), TotalContribution: new ChipAmount(1), IsFolded: false, IsAllIn: false),
            new SolverPlayerState(bbId, SeatIndex: 1, Position.BB, Stack: new ChipAmount(98), CurrentStreetContribution: new ChipAmount(2), TotalContribution: new ChipAmount(2), IsFolded: false, IsAllIn: false)
        };

        return new SolverHandState(
            config,
            street: Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: sbId,
            pot: new ChipAmount(3),
            currentBetSize: new ChipAmount(2),
            lastRaiseSize: new ChipAmount(2),
            raisesThisStreet: 0,
            players,
            actionHistory: [],
            privateCardsByPlayer: new Dictionary<PlayerId, Domain.Cards.HoleCards>());
    }

    private static SolverHandState CreateClosedPreflopStateWithoutPrivateCards()
    {
        var sbId = new PlayerId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var bbId = new PlayerId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var config = new GameConfig(MaxPlayers: 2, SmallBlind: new ChipAmount(1), BigBlind: new ChipAmount(2), Ante: ChipAmount.Zero, StartingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(sbId, SeatIndex: 0, Position.SB, Stack: new ChipAmount(97), CurrentStreetContribution: ChipAmount.Zero, TotalContribution: new ChipAmount(3), IsFolded: false, IsAllIn: false),
            new SolverPlayerState(bbId, SeatIndex: 1, Position.BB, Stack: new ChipAmount(97), CurrentStreetContribution: ChipAmount.Zero, TotalContribution: new ChipAmount(3), IsFolded: false, IsAllIn: false)
        };

        return new SolverHandState(
            config,
            street: Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: sbId,
            pot: new ChipAmount(6),
            currentBetSize: ChipAmount.Zero,
            lastRaiseSize: new ChipAmount(2),
            raisesThisStreet: 0,
            players,
            actionHistory: [],
            privateCardsByPlayer: new Dictionary<PlayerId, Domain.Cards.HoleCards>());
    }


    private static SolverHandState CreateOneActivePlayerPreflopState()
    {
        var sbId = new PlayerId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var bbId = new PlayerId(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        var config = new GameConfig(MaxPlayers: 2, SmallBlind: new ChipAmount(1), BigBlind: new ChipAmount(2), Ante: ChipAmount.Zero, StartingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(sbId, SeatIndex: 0, Position.SB, Stack: new ChipAmount(99), CurrentStreetContribution: new ChipAmount(1), TotalContribution: new ChipAmount(1), IsFolded: false, IsAllIn: false),
            new SolverPlayerState(bbId, SeatIndex: 1, Position.BB, Stack: new ChipAmount(98), CurrentStreetContribution: new ChipAmount(2), TotalContribution: new ChipAmount(2), IsFolded: true, IsAllIn: false)
        };

        return new SolverHandState(
            config,
            street: Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: sbId,
            pot: new ChipAmount(3),
            currentBetSize: new ChipAmount(2),
            lastRaiseSize: new ChipAmount(2),
            raisesThisStreet: 0,
            players,
            actionHistory: [],
            privateCardsByPlayer: new Dictionary<PlayerId, Domain.Cards.HoleCards>());
    }

    private static SolverHandState CreateNoActionablePlayersPreflopState()
    {
        var sbId = new PlayerId(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        var bbId = new PlayerId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var config = new GameConfig(MaxPlayers: 2, SmallBlind: new ChipAmount(1), BigBlind: new ChipAmount(2), Ante: ChipAmount.Zero, StartingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(sbId, SeatIndex: 0, Position.SB, Stack: ChipAmount.Zero, CurrentStreetContribution: new ChipAmount(10), TotalContribution: new ChipAmount(10), IsFolded: false, IsAllIn: true),
            new SolverPlayerState(bbId, SeatIndex: 1, Position.BB, Stack: ChipAmount.Zero, CurrentStreetContribution: new ChipAmount(10), TotalContribution: new ChipAmount(10), IsFolded: false, IsAllIn: true)
        };

        return new SolverHandState(
            config,
            street: Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: sbId,
            pot: new ChipAmount(20),
            currentBetSize: new ChipAmount(10),
            lastRaiseSize: new ChipAmount(2),
            raisesThisStreet: 1,
            players,
            actionHistory: [
                new SolverActionEntry(sbId, ActionType.AllIn, new ChipAmount(10)),
                new SolverActionEntry(bbId, ActionType.Call, new ChipAmount(10))
            ],
            privateCardsByPlayer: new Dictionary<PlayerId, Domain.Cards.HoleCards>());
    }

    private sealed class CountingLeafDetector : IPreflopLeafDetector
    {
        public int Calls { get; private set; }

        public bool IsLeaf(SolverHandState state)
        {
            Calls++;
            return false;
        }
    }

    private sealed class NonProgressingChanceSampler : IChanceSampler
    {
        public bool IsChanceNode(SolverHandState state) => true;

        public SolverHandState Sample(SolverHandState state, Random rng) => state;
    }

    private sealed class CapturingActionSampler : IActionSampler
    {
        public int SampleCalls { get; private set; }

        public LegalAction Sample(IReadOnlyList<LegalAction> legalActions, IReadOnlyDictionary<LegalAction, double> policy, Random rng)
        {
            SampleCalls++;
            return legalActions[0];
        }
    }
}
