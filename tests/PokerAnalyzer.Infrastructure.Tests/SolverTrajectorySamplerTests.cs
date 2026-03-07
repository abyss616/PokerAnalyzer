using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.Engines.SolverTraining;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class SolverTrajectorySamplerTests
{
    private static readonly GameConfig Config = new(
        MaxPlayers: 2,
        SmallBlind: new ChipAmount(5),
        BigBlind: new ChipAmount(10),
        Ante: ChipAmount.Zero,
        StartingStack: new ChipAmount(100));

    [Fact]
    public void Sample_PreflopRoot_Returns_AtLeast_One_Decision_Step()
    {
        var sampler = new SolverTrajectorySampler(new SolverStrategyStore());
        var chanceSampler = new SolverChanceSampler();
        var mapper = new SolverInfoSetKeyMapper();
        var state = CreateHeadsUpPreflopState(includePrivateCards: true);

        var trajectory = sampler.Sample(state, chanceSampler, mapper, new Random(42));

        Assert.NotEmpty(trajectory.Steps);
    }

    [Fact]
    public void Sample_ChanceNode_Is_Resolved_Before_Decision_Steps()
    {
        var sampler = new SolverTrajectorySampler(new SolverStrategyStore());
        var chanceSampler = new SolverChanceSampler();
        var mapper = new SolverInfoSetKeyMapper();
        var state = CreateHeadsUpPreflopState(includePrivateCards: false);

        var trajectory = sampler.Sample(state, chanceSampler, mapper, new Random(123));

        Assert.True(trajectory.ChanceSamplesTaken > 0);
        Assert.NotEmpty(trajectory.Steps);
        Assert.All(trajectory.Steps, step => Assert.False(step.UsedFallbackInfoSetKey));
    }

    [Fact]
    public void Preflop_Blind_Root_Minimum_Raise_Target_Is_Fifteen_For_Sb()
    {
        var state = CreateHeadsUpPreflopState(includePrivateCards: false);
        var provider = new DefaultBetSizeSetProvider();

        var raiseSizes = provider.GetRaiseSizes(state);

        Assert.Equal(new ChipAmount(15), raiseSizes[0]);
    }

    [Fact]
    public void Uniform_Strategy_Sampling_Only_Chooses_Legal_Actions()
    {
        var sampler = new SolverTrajectorySampler(new SolverStrategyStore());
        var chanceSampler = new SolverChanceSampler();
        var mapper = new SolverInfoSetKeyMapper();
        var state = CreateHeadsUpPreflopState(includePrivateCards: true);

        for (var seed = 0; seed < 100; seed++)
        {
            var trajectory = sampler.Sample(state, chanceSampler, mapper, new Random(seed));
            Assert.NotEmpty(trajectory.Steps);
            var firstStep = trajectory.Steps[0];
            Assert.Contains(firstStep.SampledAction, firstStep.LegalActions);
        }
    }

    [Fact]
    public void Sampling_Preflop_Produces_Executable_Raise_Actions_With_Explicit_Target_Amounts()
    {
        var sampler = new SolverTrajectorySampler(new SolverStrategyStore());
        var chanceSampler = new SolverChanceSampler();
        var terminalDetector = new SolverTerminalDetector();
        var mapper = new SolverInfoSetKeyMapper();

        for (var seed = 0; seed < 100; seed++)
        {
            var rng = new Random(seed);
            var state = CreateHeadsUpPreflopState(includePrivateCards: true);
            var preflopDecisionStepsObserved = 0;

            while (state.Street == Street.Preflop && !terminalDetector.IsTerminal(state))
            {
                if (chanceSampler.IsChanceNode(state))
                {
                    var chanceResolved = chanceSampler.Sample(state, rng);
                    if (chanceResolved.Street != Street.Preflop)
                        break;

                    state = chanceResolved;
                    continue;
                }

                var trajectory = sampler.Sample(state, chanceSampler, mapper, rng);
                Assert.NotEmpty(trajectory.Steps);

                var step = trajectory.Steps[0];
                Assert.Equal(Street.Preflop, state.Street);

                if (step.SampledAction.ActionType is ActionType.Raise)
                    Assert.NotNull(step.SampledAction.Amount);

                Assert.All(
                    step.LegalActions.Where(a => a.ActionType is ActionType.Raise),
                    action => Assert.NotNull(action.Amount));

                preflopDecisionStepsObserved++;
                state = SolverStateStepper.Step(state, step.SampledAction);
            }

            Assert.True(preflopDecisionStepsObserved > 0);
        }
    }
  

    [Fact]
    public void Sampling_Is_Deterministic_For_Equivalent_Seeds()
    {
        var chanceSampler = new SolverChanceSampler();
        var mapper = new SolverInfoSetKeyMapper();
        var state = CreateHeadsUpPreflopState(includePrivateCards: false);

        var firstSampler = new SolverTrajectorySampler(new SolverStrategyStore());
        var secondSampler = new SolverTrajectorySampler(new SolverStrategyStore());

        var first = firstSampler.Sample(state, chanceSampler, mapper, new Random(999));
        var second = secondSampler.Sample(state, chanceSampler, mapper, new Random(999));

        Assert.Equal(first.ChanceSamplesTaken, second.ChanceSamplesTaken);
        Assert.Equal(first.Steps.Count, second.Steps.Count);

        for (var index = 0; index < first.Steps.Count; index++)
        {
            Assert.Equal(first.Steps[index].InfoSetCanonicalKey, second.Steps[index].InfoSetCanonicalKey);
            Assert.Equal(first.Steps[index].SampledAction, second.Steps[index].SampledAction);
            Assert.Equal(first.Steps[index].PreActionStateSignature, second.Steps[index].PreActionStateSignature);
        }

        Assert.Equal(first.TerminalState.ActionHistorySignature, second.TerminalState.ActionHistorySignature);
    }

    private static SolverHandState CreateHeadsUpPreflopState(bool includePrivateCards)
    {
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();

        var privateCards = includePrivateCards
            ? new Dictionary<PlayerId, HoleCards>
            {
                [sbId] = HoleCards.Parse("AsKh"),
                [bbId] = HoleCards.Parse("7c7d")
            }
            : new Dictionary<PlayerId, HoleCards>();

        return new SolverHandState(
            Config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: sbId,
            pot: new ChipAmount(15),
            currentBetSize: new ChipAmount(10),
            lastRaiseSize: new ChipAmount(5),
            raisesThisStreet: 0,
            players:
            [
                new SolverPlayerState(sbId, 0, Position.SB, new ChipAmount(95), new ChipAmount(5), new ChipAmount(5), false, false),
                new SolverPlayerState(bbId, 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false)
            ],
            actionHistory:
            [
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(5)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(10))
            ],
            privateCardsByPlayer: privateCards);
    }
}
