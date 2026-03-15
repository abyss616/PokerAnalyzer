using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Domain.Game;
using System.Globalization;

namespace PokerAnalyzer.Application.PreflopSolver;

public interface IPreflopStrategyQueryService
{
    PreflopStrategyResultDto GetStrategyResult(string infoSetKey, IReadOnlyList<LegalAction> legalActions);
}

public sealed class PreflopStrategyQueryService : IPreflopStrategyQueryService
{
    private readonly IAverageStrategyStore _averageStrategyStore;
    private readonly IRegretStore _regretStore;
    private readonly IPreflopTrainingProgressStore _trainingProgressStore;
    private readonly IActionValueStore _actionValueStore;

    public PreflopStrategyQueryService(
        IAverageStrategyStore averageStrategyStore,
        IRegretStore regretStore,
        IPreflopTrainingProgressStore trainingProgressStore,
        IActionValueStore actionValueStore)
    {
        _averageStrategyStore = averageStrategyStore ?? throw new ArgumentNullException(nameof(averageStrategyStore));
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
        _trainingProgressStore = trainingProgressStore ?? throw new ArgumentNullException(nameof(trainingProgressStore));
        _actionValueStore = actionValueStore ?? throw new ArgumentNullException(nameof(actionValueStore));
    }

    public PreflopStrategyResultDto GetStrategyResult(string infoSetKey, IReadOnlyList<LegalAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(infoSetKey);
        ArgumentNullException.ThrowIfNull(legalActions);

        var averagePolicy = _averageStrategyStore.GetAveragePolicy(infoSetKey, legalActions);
        var averageStrategy = new Dictionary<string, decimal>(legalActions.Count, StringComparer.Ordinal);

        foreach (var action in legalActions)
        {
            var probability = averagePolicy.TryGetValue(action, out var value) ? value : 0d;
            averageStrategy[ToActionKey(action)] = (decimal)probability;
        }

        _ = new RegretMatchingPolicyProvider(_regretStore, _actionValueStore).TryGetPolicy(infoSetKey, legalActions, out var currentPolicy);
        currentPolicy ??= UniformPolicyBuilder.Build(legalActions);

        var regretMagnitude = 0d;
        var diagnostics = new List<PreflopActionDiagnosticDto>(legalActions.Count);
        foreach (var action in legalActions)
        {
            var regret = _regretStore.Get(infoSetKey, action);
            regretMagnitude += Math.Max(0d, regret);
            var actionKey = ToActionKey(action);
            var averageFrequency = averageStrategy.TryGetValue(actionKey, out var value) ? value : 0m;
            var currentFrequency = (decimal)(currentPolicy.TryGetValue(action, out var cp) ? cp : 0d);
            diagnostics.Add(new PreflopActionDiagnosticDto(actionKey, averageFrequency, currentFrequency, regret, Math.Max(0d, regret), false));
        }

        var bestActionKey = diagnostics
            .OrderByDescending(x => x.Frequency)
            .ThenByDescending(x => x.CurrentPolicyFrequency)
            .ThenByDescending(x => x.Regret)
            .Select(x => x.ActionKey)
            .FirstOrDefault();
        diagnostics = diagnostics
            .Select(x => x with { IsBestByFrequency = string.Equals(x.ActionKey, bestActionKey, StringComparison.Ordinal) })
            .ToList();

        var ordered = diagnostics.OrderByDescending(x => x.Frequency).ToList();
        var bestMargin = ordered.Count > 1 ? (double)(ordered[0].Frequency - ordered[1].Frequency) : 0d;
        var separation = ordered.Count > 1 ? (double)(ordered[0].Frequency - ordered[1].Frequency) : 0d;

        return new PreflopStrategyResultDto(
            infoSetKey,
            averageStrategy,
            _trainingProgressStore.TotalIterationsCompleted,
            regretMagnitude,
            "StoreBacked",
            0,
            "None",
            null,
            null,
            diagnostics,
            "Average frequencies come from cumulative average strategy; current-policy frequencies come from regret matching on positive cumulative regret and action-value-based stochastic fallback when all regrets are non-positive; regrets are cumulative counterfactual regrets.",
            bestMargin,
            separation);
    }

    private static string ToActionKey(LegalAction action)
    {
        if (action.Amount?.Value > 0L)
            return $"{action.ActionType}:{(action.Amount.Value.Value / 100m).ToString("0.##", CultureInfo.InvariantCulture)}";

        return action.ActionType.ToString();
    }
}
