using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopRegretTrainerTests
{


    [Fact]
    public void RunTraining_WithParallelOptions_WorkerCountOne_CompletesIterations()
    {
        var trainer = CreateTrainerWithRegretAwareTraverser(out var regrets, out var averageStrategy, out var fold, out var call);

        var result = trainer.RunTraining(new PreflopTrainerOptions(Iterations: 8, WorkerCount: 1, BatchSize: 2, Deterministic: true, RandomSeed: 123));

        Assert.Equal(8, result.IterationsCompleted);
        Assert.True(result.ReachedIterationLimit);
        Assert.True(regrets.Get("traversal_infoset", fold) != 0d || regrets.Get("traversal_infoset", call) != 0d);
        Assert.NotEqual(0d, averageStrategy.Get("traversal_infoset", fold) + averageStrategy.Get("traversal_infoset", call));
    }

    [Fact]
    public void RunTraining_WithParallelOptions_WorkerCountGreaterThanOne_CompletesAndAccumulatesStrategy()
    {
        var trainer = CreateTrainerWithRegretAwareTraverser(out var regrets, out var averageStrategy, out var fold, out var call);

        var result = trainer.RunTraining(new PreflopTrainerOptions(Iterations: 24, WorkerCount: 4, BatchSize: 3, Deterministic: true, RandomSeed: 456));

        Assert.Equal(24, result.IterationsCompleted);
        Assert.True(result.ReachedIterationLimit);
        Assert.NotEqual(0d, regrets.Get("traversal_infoset", fold) + regrets.Get("traversal_infoset", call));
        Assert.NotEqual(0d, averageStrategy.Get("traversal_infoset", fold) + averageStrategy.Get("traversal_infoset", call));
    }

    [Fact]
    public void RunTraining_WithDeterministicSeed_IsReproducibleAcrossRuns()
    {
        var options = new PreflopTrainerOptions(Iterations: 16, WorkerCount: 3, BatchSize: 2, Deterministic: true, RandomSeed: 77);

        var trainerA = CreateTrainerWithRegretAwareTraverser(out var regretsA, out var avgA, out var fold, out var call);
        var trainerB = CreateTrainerWithRegretAwareTraverser(out var regretsB, out var avgB, out _, out _);

        var resultA = trainerA.RunTraining(options);
        var resultB = trainerB.RunTraining(options);

        Assert.Equal(resultA.IterationsCompleted, resultB.IterationsCompleted);
        Assert.Equal(regretsA.Get("traversal_infoset", fold), regretsB.Get("traversal_infoset", fold), 10);
        Assert.Equal(regretsA.Get("traversal_infoset", call), regretsB.Get("traversal_infoset", call), 10);
        Assert.Equal(avgA.Get("traversal_infoset", fold), avgB.Get("traversal_infoset", fold), 10);
        Assert.Equal(avgA.Get("traversal_infoset", call), avgB.Get("traversal_infoset", call), 10);
    }

    [Fact]
    public void RunTraining_ParallelAndSingleWorker_AreMateriallySimilar_ForFixedSeed()
    {
        const int iterations = 20;
        const int seed = 99;

        var singleTrainer = CreateTrainerWithRegretAwareTraverser(out var singleRegrets, out var singleAvg, out var fold, out var call);
        var parallelTrainer = CreateTrainerWithRegretAwareTraverser(out var parallelRegrets, out var parallelAvg, out _, out _);

        singleTrainer.RunTraining(new PreflopTrainerOptions(iterations, WorkerCount: 1, BatchSize: 4, RandomSeed: seed, Deterministic: true));
        parallelTrainer.RunTraining(new PreflopTrainerOptions(iterations, WorkerCount: 4, BatchSize: 2, RandomSeed: seed, Deterministic: true));

        var regretFoldDiff = Math.Abs(singleRegrets.Get("traversal_infoset", fold) - parallelRegrets.Get("traversal_infoset", fold));
        var regretCallDiff = Math.Abs(singleRegrets.Get("traversal_infoset", call) - parallelRegrets.Get("traversal_infoset", call));
        var avgFoldDiff = Math.Abs(singleAvg.Get("traversal_infoset", fold) - parallelAvg.Get("traversal_infoset", fold));
        var avgCallDiff = Math.Abs(singleAvg.Get("traversal_infoset", call) - parallelAvg.Get("traversal_infoset", call));

        Assert.True(regretFoldDiff < 5d, $"Fold regret drift too large: {regretFoldDiff}");
        Assert.True(regretCallDiff < 5d, $"Call regret drift too large: {regretCallDiff}");
        Assert.True(avgFoldDiff < 5d, $"Fold avg drift too large: {avgFoldDiff}");
        Assert.True(avgCallDiff < 5d, $"Call avg drift too large: {avgCallDiff}");
    }

    [Fact]
    public void RunTraining_InIterationMode_RunsRequestedIterationsAndReportsIterationLimit()
    {
        var trainer = CreateTrainerWithRegretAwareTraverser(out var regrets, out var averageStrategy, out _, out _);

        var result = trainer.RunTraining(PreflopTrainingOptions.ForIterations(3));

        Assert.Equal(3, result.IterationsCompleted);
        Assert.Equal(PreflopTrainingMode.Iterations, result.ModeUsed);
        Assert.True(result.ReachedIterationLimit);
        Assert.False(result.ReachedTimeLimit);
        Assert.False(result.StoppedByCancellation);
        Assert.NotEqual(0d, averageStrategy.Get("traversal_infoset", new LegalAction(ActionType.Fold)));
        Assert.NotEqual(0d, regrets.Get("traversal_infoset", new LegalAction(ActionType.Fold)));
    }

    [Fact]
    public void RunTraining_InTimeMode_StopsOnTimeBudget_AndRunsAtLeastOneIteration()
    {
        var trainer = CreateTrainerWithRegretAwareTraverser(out _, out _, out _, out _);

        var result = trainer.RunTraining(PreflopTrainingOptions.ForTime(TimeSpan.FromMilliseconds(30)));

        Assert.Equal(PreflopTrainingMode.Time, result.ModeUsed);
        Assert.True(result.ReachedTimeLimit);
        Assert.False(result.ReachedIterationLimit);
        Assert.False(result.StoppedByCancellation);
        Assert.True(result.IterationsCompleted >= 1);
        Assert.True(result.Elapsed >= TimeSpan.FromMilliseconds(20));
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RunTraining_DefaultOptions_UsesTimeMode()
    {
        Assert.Equal(PreflopTrainingMode.Time, PreflopTrainingOptions.Default.Mode);
        Assert.Equal(TimeSpan.FromSeconds(20), PreflopTrainingOptions.Default.MaxDuration);
    }

    [Fact]
    public void TrainingOptions_InvalidBudgets_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PreflopTrainingOptions.ForIterations(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PreflopTrainingOptions.ForTime(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => PreflopTrainingOptions.ForTime(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void RunTraining_Cancellation_StopsPromptly()
    {
        var trainer = CreateTrainerWithRegretAwareTraverser(out _, out _, out _, out _);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10));

        var result = trainer.RunTraining(PreflopTrainingOptions.ForTime(TimeSpan.FromSeconds(1)), cts.Token);

        Assert.True(result.StoppedByCancellation);
        Assert.False(result.ReachedIterationLimit);
        Assert.False(result.ReachedTimeLimit);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(1));
    }


    [Fact]
    public void RunIteration_IncrementsTrainingProgressStore()
    {
        var progress = new InMemoryPreflopTrainingProgressStore();

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
            new InMemoryAverageStrategyStore(),
            progress);

        trainer.RunIteration(new Random(21));
        trainer.RunIteration(new Random(22));

        Assert.Equal(2, progress.TotalIterationsCompleted);
    }

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
    public void RunIteration_WhenAllLegalRegretsNonPositive_UsesActionValueBasedTraversalPolicy()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var regrets = new InMemoryRegretStore();
        var actionValues = new InMemoryActionValueStore();
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));
        regrets.Add("traversal_infoset", fold, -2d);
        regrets.Add("traversal_infoset", call, 0d);
        actionValues.AddSamples("traversal_infoset", fold, 10d, 1);
        actionValues.AddSamples("traversal_infoset", call, 4d, 1);

        var traverser = new RegretAwareStubTrajectoryTraverser(
            root,
            traversalPlayer,
            opponent,
            new RegretMatchingPolicyProvider(regrets, actionValues),
            fold,
            call);

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            new InMemoryAverageStrategyStore(),
            actionValueStore: actionValues);

        trainer.RunIteration(new Random(19));

        Assert.True(traverser.InitialTraversalPolicy[fold] > traverser.InitialTraversalPolicy[call]);
        Assert.NotEqual(0.5d, traverser.InitialTraversalPolicy[fold], 10);
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

    [Fact]
    public void RunIteration_WithCanonicalStorageKey_WritesAndReadsUsingCanonicalKey()
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
        const string solverKey = "v2/UNOPENED/BTN/eff=130.5/jam=18";

        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            averageStrategy,
            canonicalStorageKey: solverKey);

        trainer.RunIteration(new Random(17));

        Assert.Equal(0.6d, averageStrategy.Get(solverKey, fold), 10);
        Assert.Equal(0.4d, averageStrategy.Get(solverKey, call), 10);
        Assert.Equal(0d, averageStrategy.Get("traversal_infoset", fold), 10);
        Assert.Equal(0d, averageStrategy.Get("traversal_infoset", call), 10);
    }


    [Fact]
    public void RunIteration_UsesInfoSetKeyAsSolverKey_ForActionValueLeafEvaluation()
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        var fold = new LegalAction(ActionType.Fold);
        var check = new LegalAction(ActionType.Check);
        var raise = new LegalAction(ActionType.Raise, new ChipAmount(11));
        var traverser = new SolverKeyCapturingTrajectoryTraverser(root, traversalPlayer, opponent, fold, check, raise);

        var regrets = new InMemoryRegretStore();
        var trainer = new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            new InMemoryAverageStrategyStore());

        trainer.RunIteration(new Random(13));

        Assert.NotEmpty(traverser.CapturedSolverKeys);
        Assert.All(traverser.CapturedSolverKeys, key => Assert.Equal("traversal_infoset", key));
        Assert.NotEqual(0d, regrets.Get("traversal_infoset", fold));
        Assert.NotEqual(0d, regrets.Get("traversal_infoset", raise));
        Assert.NotEqual(regrets.Get("traversal_infoset", fold), regrets.Get("traversal_infoset", raise));
    }

    private static PreflopRegretTrainer CreateTrainerWithRegretAwareTraverser(
        out InMemoryRegretStore regrets,
        out InMemoryAverageStrategyStore averageStrategy,
        out LegalAction fold,
        out LegalAction call)
    {
        var root = CreateHeadsUpPreflopState();
        var traversalPlayer = root.Players[0].PlayerId;
        var opponent = root.Players[1].PlayerId;

        regrets = new InMemoryRegretStore();
        averageStrategy = new InMemoryAverageStrategyStore();
        fold = new LegalAction(ActionType.Fold);
        call = new LegalAction(ActionType.Call, new ChipAmount(1));

        regrets.Add("traversal_infoset", fold, 3d);
        regrets.Add("traversal_infoset", call, 1d);

        var traverser = new RegretAwareStubTrajectoryTraverser(
            root,
            traversalPlayer,
            opponent,
            new RegretMatchingPolicyProvider(regrets),
            fold,
            call);

        return new PreflopRegretTrainer(
            new FixedRootStateProvider(root),
            traverser,
            new FixedTraversalPlayerSelector(traversalPlayer),
            regrets,
            averageStrategy);
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
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [sbId] = HoleCards.Parse("AsKh"),
                [bbId] = HoleCards.Parse("QdJd")
            });
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

        public TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng, PreflopLeafEvaluationContext? evaluationContext = null)
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


    private sealed class SolverKeyCapturingTrajectoryTraverser : IPreflopTrajectoryTraverser
    {
        private readonly SolverHandState _root;
        private readonly PlayerId _traversalPlayer;
        private readonly PlayerId _opponent;
        private readonly LegalAction _fold;
        private readonly LegalAction _check;
        private readonly LegalAction _raise;
        private bool _returnedInitialTrajectory;

        public SolverKeyCapturingTrajectoryTraverser(SolverHandState root, PlayerId traversalPlayer, PlayerId opponent, LegalAction fold, LegalAction check, LegalAction raise)
        {
            _root = root;
            _traversalPlayer = traversalPlayer;
            _opponent = opponent;
            _fold = fold;
            _check = check;
            _raise = raise;
        }

        public List<string?> CapturedSolverKeys { get; } = new();

        public TrajectorySample RunIteration(Random rng) => SampleTrajectory(_root, rng);

        public TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng, PreflopLeafEvaluationContext? evaluationContext = null)
        {
            if (!_returnedInitialTrajectory)
            {
                _returnedInitialTrajectory = true;
                var legalActions = new[] { _fold, _check, _raise };
                var path = new List<VisitedNode>
                {
                    VisitedNode.CreateAction(
                        depth: 0,
                        street: Street.Preflop,
                        actingPlayerId: _traversalPlayer,
                        infoSetKey: "traversal_infoset",
                        legalActions: legalActions,
                        policy: new Dictionary<LegalAction, double> { [_fold] = 1d/3d, [_check] = 1d/3d, [_raise] = 1d/3d },
                        sampledAction: _check,
                        stateBeforeAction: rootState),
                    VisitedNode.CreateAction(
                        depth: 1,
                        street: Street.Preflop,
                        actingPlayerId: _opponent,
                        infoSetKey: "opponent_infoset",
                        legalActions: new[] { _fold, _check },
                        policy: new Dictionary<LegalAction, double> { [_fold] = 0.5d, [_check] = 0.5d },
                        sampledAction: _check,
                        stateBeforeAction: rootState.Apply(_check)),
                    VisitedNode.CreateLeaf(2, Street.Preflop, "leaf")
                };

                return new TrajectorySample(rootState, new Dictionary<PlayerId, double> { [_traversalPlayer] = 0d, [_opponent] = 0d }, path);
            }

            CapturedSolverKeys.Add(evaluationContext?.SolverKey);
            var action = evaluationContext?.RootAction.ActionType ?? ActionType.Check;
            var utility = action switch
            {
                ActionType.Fold => -0.20d,
                ActionType.Check => 0.12d,
                ActionType.Raise => 0.31d,
                _ => 0d
            };

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

        public TrajectorySample SampleTrajectory(SolverHandState rootState, Random rng, PreflopLeafEvaluationContext? evaluationContext = null)
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
