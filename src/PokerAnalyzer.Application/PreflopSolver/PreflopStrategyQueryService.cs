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

    public PreflopStrategyQueryService(
        IAverageStrategyStore averageStrategyStore,
        IRegretStore regretStore,
        IPreflopTrainingProgressStore trainingProgressStore)
    {
        _averageStrategyStore = averageStrategyStore ?? throw new ArgumentNullException(nameof(averageStrategyStore));
        _regretStore = regretStore ?? throw new ArgumentNullException(nameof(regretStore));
        _trainingProgressStore = trainingProgressStore ?? throw new ArgumentNullException(nameof(trainingProgressStore));
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

        var regretMagnitude = 0d;
        var diagnostics = new List<PreflopActionDiagnosticDto>(legalActions.Count);
        foreach (var action in legalActions)
        {
            var regret = _regretStore.Get(infoSetKey, action);
            regretMagnitude += Math.Max(0d, regret);
            var actionKey = ToActionKey(action);
            var frequency = averageStrategy.TryGetValue(actionKey, out var value) ? value : 0m;
            diagnostics.Add(new PreflopActionDiagnosticDto(actionKey, frequency, regret, Math.Max(0d, regret), false));
        }

        var ordered = diagnostics.OrderByDescending(x => x.Frequency).ToList();
        var bestMargin = ordered.Count > 1 ? (double)(ordered[0].Frequency - ordered[1].Frequency) : 0d;
        var separation = diagnostics.Sum(x => x.PositiveRegret);

        return new PreflopStrategyResultDto(
            infoSetKey,
            averageStrategy,
            _trainingProgressStore.TotalIterationsCompleted,
            regretMagnitude,
            "StoreBacked",
            0,
            "None",
            null,
            diagnostics,
            "Regret/average strategy only (no explicit per-action EV rollouts stored).",
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
