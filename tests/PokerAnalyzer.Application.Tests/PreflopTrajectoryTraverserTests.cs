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
            new PublicStateInfoSetMapper(),
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
    public void SampleTrajectory_WhenPreflopRoundAlreadyClosed_SamplesChanceAndStopsAtFlopCutoff()
    {
        var state = CreateClosedPreflopStateWithoutPrivateCards();
        var traverser = new PreflopTrajectoryTraverser(
            new FixedRootStateProvider(state),
            new SolverChanceSampler(),
            new PublicStateInfoSetMapper(),
            new InMemoryPolicyProvider(),
            new WeightedRandomActionSampler(),
            new PlaceholderPreflopLeafEvaluator(),
            new DefaultPreflopLeafDetector());

        var result = traverser.RunIteration(new Random(17));

        Assert.Equal(Street.Flop, result.FinalState.Street);
        Assert.Equal(3, result.FinalState.BoardCards.Count);
        Assert.Contains(result.Path, node => node.NodeKind == TraversalNodeKind.Chance);
        Assert.Equal(TraversalNodeKind.Leaf, result.Path[^1].NodeKind);
    }

    private static SolverHandState CreateHeadsUpPreflopState()
    {
        var sbId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var bbId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var config = new GameConfig(maxPlayers: 2, smallBlind: new ChipAmount(1), bigBlind: new ChipAmount(2), ante: ChipAmount.Zero, startingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(sbId, seatIndex: 0, Position.SB, stack: new ChipAmount(99), currentStreetContribution: new ChipAmount(1), totalContribution: new ChipAmount(1), IsFolded: false, IsAllIn: false),
            new SolverPlayerState(bbId, seatIndex: 1, Position.BB, stack: new ChipAmount(98), currentStreetContribution: new ChipAmount(2), totalContribution: new ChipAmount(2), IsFolded: false, IsAllIn: false)
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
        var config = new GameConfig(maxPlayers: 2, smallBlind: new ChipAmount(1), bigBlind: new ChipAmount(2), ante: ChipAmount.Zero, startingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(sbId, seatIndex: 0, Position.SB, stack: new ChipAmount(97), currentStreetContribution: ChipAmount.Zero, totalContribution: new ChipAmount(3), IsFolded: false, IsAllIn: false),
            new SolverPlayerState(bbId, seatIndex: 1, Position.BB, stack: new ChipAmount(97), currentStreetContribution: ChipAmount.Zero, totalContribution: new ChipAmount(3), IsFolded: false, IsAllIn: false)
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
