using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines.SolverTraining;

public sealed class SolverTrajectorySampler
{
    private readonly SolverStrategyStore _strategyStore;
    private readonly SolverTerminalDetector _terminalDetector;

    public SolverTrajectorySampler(SolverStrategyStore strategyStore, SolverTerminalDetector? terminalDetector = null)
    {
        _strategyStore = strategyStore ?? throw new ArgumentNullException(nameof(strategyStore));
        _terminalDetector = terminalDetector ?? new SolverTerminalDetector();
    }

    public SolverTrajectory Sample(
        SolverHandState root,
        IChanceSampler chanceSampler,
        SolverInfoSetKeyMapper infoSetKeyMapper,
        Random rng)
    {
        if (root is null)
            throw new ArgumentNullException(nameof(root));

        if (chanceSampler is null)
            throw new ArgumentNullException(nameof(chanceSampler));

        if (infoSetKeyMapper is null)
            throw new ArgumentNullException(nameof(infoSetKeyMapper));

        if (rng is null)
            throw new ArgumentNullException(nameof(rng));

        var current = root;
        var steps = new List<SolverTrajectoryStep>();
        var chanceSamplesTaken = 0;

        while (!_terminalDetector.IsTerminal(current))
        {
            if (chanceSampler.IsChanceNode(current))
            {
                current = chanceSampler.Sample(current, rng);
                chanceSamplesTaken++;
                continue;
            }

            var legalActions = current.GenerateLegalActions();
            if (legalActions.Count == 0)
                break;

            var mapping = infoSetKeyMapper.Map(current);
            var usedFallbackInfoSetKey = !mapping.IsSupported || mapping.Key is null;
            var infoSetCanonicalKey = usedFallbackInfoSetKey
                ? BuildFallbackPublicStateKey(current)
                : mapping.Key.CanonicalKey;

            var row = _strategyStore.GetOrCreate(infoSetCanonicalKey, legalActions);
            var actionIndex = SampleActionIndex(row.BehaviorProbabilities, rng);
            var sampledAction = row.LegalActions[actionIndex];

            steps.Add(new SolverTrajectoryStep(
                PreActionStateSignature: BuildPreActionStateSignature(current),
                InfoSetCanonicalKey: infoSetCanonicalKey,
                ActingPlayerId: current.ActingPlayerId,
                LegalActions: row.LegalActions,
                SampledAction: sampledAction,
                StrategyProbabilities: row.BehaviorProbabilities,
                UsedFallbackInfoSetKey: usedFallbackInfoSetKey));

            current = SolverStateStepper.Step(current, sampledAction);
        }

        return new SolverTrajectory(steps, current, chanceSamplesTaken);
    }

    private static int SampleActionIndex(IReadOnlyList<double> probabilities, Random rng)
    {
        var roll = rng.NextDouble();
        var cumulative = 0d;

        for (var index = 0; index < probabilities.Count; index++)
        {
            cumulative += probabilities[index];
            if (roll <= cumulative)
                return index;
        }

        return probabilities.Count - 1;
    }

    private static string BuildPreActionStateSignature(SolverHandState state)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"street={state.Street}|actor={state.ActingPlayerId}|pot={state.Pot.Value}|bet={state.CurrentBetSize.Value}|tocall={state.ToCall.Value}|board={state.BoardCards.Count}|actions={state.ActionHistorySignature}");

    private static string BuildFallbackPublicStateKey(SolverHandState state)
    {
        var actingPlayer = state.Players.First(player => player.PlayerId == state.ActingPlayerId);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"fallback/{state.Street}/pos={actingPlayer.Position}/pot={state.Pot.Value}/bet={state.CurrentBetSize.Value}/tocall={state.ToCall.Value}/raises={state.RaisesThisStreet}/board={state.BoardCards.Count}/hist={state.ActionHistorySignature}");
    }
}
