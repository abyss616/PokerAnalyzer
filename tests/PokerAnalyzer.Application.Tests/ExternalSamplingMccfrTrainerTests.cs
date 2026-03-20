using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class ExternalSamplingMccfrTrainerTests
{
    [Fact]
    public void RunIteration_EnumeratesTraverserActions_AndUpdatesRegretFromNodeValue()
    {
        var root = CreateHeadsUpPreflopState();
        var traverser = root.ActingPlayerId;
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        var regrets = new InMemoryRegretStore();
        regrets.Add("traversal_infoset", fold, 3d);
        regrets.Add("traversal_infoset", call, 1d);

        var averages = new InMemoryAverageStrategyStore();
        var leafEvaluator = new RootActionUtilityLeafEvaluator(
            traverser,
            new Dictionary<ActionType, double>
            {
                [ActionType.Fold] = 10d,
                [ActionType.Call] = 4d
            });

        var trainer = CreateTrainer(
            root,
            traverser,
            regrets,
            averages,
            leafEvaluator,
            new DepthLeafDetector(root.ActionHistory.Count + 1));

        trainer.RunIteration(new Random(7));

        Assert.Equal(4.5d, regrets.Get("traversal_infoset", fold), 10);
        Assert.Equal(-3.5d, regrets.Get("traversal_infoset", call), 10);
        Assert.Equal(0.75d, averages.Get("traversal_infoset", fold), 10);
        Assert.Equal(0.25d, averages.Get("traversal_infoset", call), 10);
        Assert.Equal(new[] { "traversal_infoset", "traversal_infoset" }, leafEvaluator.CapturedSolverKeys);
    }

    [Fact]
    public void RunIteration_WeightsAverageStrategyByOpponentSamplingReach_OnSampledOpponentPrefix()
    {
        var root = CreateHeadsUpPreflopState();
        var traverser = root.Players[1].PlayerId;
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        var regrets = new InMemoryRegretStore();
        regrets.Add("opponent_infoset", fold, 1d);
        regrets.Add("opponent_infoset", call, 3d);
        regrets.Add("traversal_infoset", fold, 3d);
        regrets.Add("traversal_infoset", call, 1d);

        var averages = new InMemoryAverageStrategyStore();
        var trainer = CreateTrainer(
            root,
            traverser,
            regrets,
            averages,
            new RootActionUtilityLeafEvaluator(
                traverser,
                new Dictionary<ActionType, double>
                {
                    [ActionType.Fold] = 10d,
                    [ActionType.Call] = 4d
                }),
            new DepthLeafDetector(root.ActionHistory.Count + 2));

        trainer.RunIteration(new Random(11));

        Assert.Equal(1d, averages.Get("traversal_infoset", fold), 10);
        Assert.Equal(1d / 3d, averages.Get("traversal_infoset", call), 10);
    }

    [Fact]
    public void RunIteration_WeightsUpdatesByChanceSamplingReach_OnSampledChancePrefix()
    {
        var root = CreateHeadsUpPreflopState();
        var traverser = root.ActingPlayerId;
        var fold = new LegalAction(ActionType.Fold);
        var call = new LegalAction(ActionType.Call, new ChipAmount(1));

        var regrets = new InMemoryRegretStore();
        regrets.Add("traversal_infoset", fold, 3d);
        regrets.Add("traversal_infoset", call, 1d);

        var averages = new InMemoryAverageStrategyStore();
        var trainer = CreateTrainer(
            root,
            traverser,
            regrets,
            averages,
            new RootActionUtilityLeafEvaluator(
                traverser,
                new Dictionary<ActionType, double>
                {
                    [ActionType.Fold] = 10d,
                    [ActionType.Call] = 4d
                }),
            new DepthLeafDetector(root.ActionHistory.Count + 1),
            new SingleShotChanceSampler(root.Pot, new ChipAmount(root.Pot.Value + 1), 0.2d));

        trainer.RunIteration(new Random(13));

        Assert.Equal(3.75d, averages.Get("traversal_infoset", fold), 10);
        Assert.Equal(1.25d, averages.Get("traversal_infoset", call), 10);
        Assert.Equal(10.5d, regrets.Get("traversal_infoset", fold), 10);
        Assert.Equal(-21.5d, regrets.Get("traversal_infoset", call), 10);
    }

    private static PreflopRegretTrainer CreateTrainer(
        SolverHandState root,
        PlayerId traverser,
        InMemoryRegretStore regrets,
        InMemoryAverageStrategyStore averages,
        IPreflopLeafEvaluator leafEvaluator,
        IPreflopLeafDetector leafDetector,
        IChanceSampler? chanceSampler = null)
        => new(
            new FixedRootStateProvider(root),
            chanceSampler ?? new NeverChanceSampler(),
            new FixedInfoSetMapper(traverser),
            new HighestProbabilityActionSampler(),
            leafEvaluator,
            leafDetector,
            new FixedTraversalPlayerSelector(traverser),
            regrets,
            averages);

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
            actionHistory:
            [
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(2))
            ],
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [sbId] = HoleCards.Parse("AsKh"),
                [bbId] = HoleCards.Parse("QdJd")
            });
    }

    private sealed class FixedInfoSetMapper : IPreflopInfoSetMapper
    {
        private readonly PlayerId _traverserId;

        public FixedInfoSetMapper(PlayerId traverserId)
        {
            _traverserId = traverserId;
        }

        public string MapInfoSetKey(SolverHandState state, PlayerId actingPlayerId)
            => actingPlayerId == _traverserId ? "traversal_infoset" : "opponent_infoset";
    }

    private sealed class HighestProbabilityActionSampler : IActionSampler
    {
        public LegalAction Sample(IReadOnlyList<LegalAction> legalActions, IReadOnlyDictionary<LegalAction, double> policy, Random rng)
            => legalActions
                .OrderByDescending(action => policy.TryGetValue(action, out var probability) ? probability : 0d)
                .ThenBy(action => action.ActionType)
                .First();
    }

    private sealed class RootActionUtilityLeafEvaluator : IPreflopLeafEvaluator
    {
        private readonly PlayerId _traverserId;
        private readonly IReadOnlyDictionary<ActionType, double> _utilities;

        public RootActionUtilityLeafEvaluator(PlayerId traverserId, IReadOnlyDictionary<ActionType, double> utilities)
        {
            _traverserId = traverserId;
            _utilities = utilities;
        }

        public List<string?> CapturedSolverKeys { get; } = new();

        public PreflopLeafEvaluation Evaluate(PreflopLeafEvaluationContext context)
        {
            CapturedSolverKeys.Add(context.SolverKey);
            var utility = _utilities.TryGetValue(context.RootAction.ActionType, out var value) ? value : 0d;
            return new PreflopLeafEvaluation(
                new Dictionary<PlayerId, double> { [_traverserId] = utility },
                $"utility:{context.RootAction.ActionType}");
        }
    }

    private sealed class DepthLeafDetector : IPreflopLeafDetector
    {
        private readonly int _leafActionHistoryCount;

        public DepthLeafDetector(int leafActionHistoryCount)
        {
            _leafActionHistoryCount = leafActionHistoryCount;
        }

        public bool IsLeaf(SolverHandState state) => state.ActionHistory.Count >= _leafActionHistoryCount;
    }

    private sealed class NeverChanceSampler : IChanceSampler
    {
        public bool IsChanceNode(SolverHandState state) => false;

        public SolverHandState Sample(SolverHandState state, Random rng) => state;

        public ChanceSampleResult SampleWithProbability(SolverHandState state, Random rng)
            => new(state, 1d);
    }

    private sealed class SingleShotChanceSampler : IChanceSampler
    {
        private readonly ChipAmount _fromPot;
        private readonly ChipAmount _toPot;
        private readonly double _samplingProbability;

        public SingleShotChanceSampler(ChipAmount fromPot, ChipAmount toPot, double samplingProbability)
        {
            _fromPot = fromPot;
            _toPot = toPot;
            _samplingProbability = samplingProbability;
        }

        public bool IsChanceNode(SolverHandState state) => state.Pot == _fromPot;

        public SolverHandState Sample(SolverHandState state, Random rng) => state.With(pot: _toPot);

        public ChanceSampleResult SampleWithProbability(SolverHandState state, Random rng)
            => new(state.With(pot: _toPot), _samplingProbability);
    }
}
