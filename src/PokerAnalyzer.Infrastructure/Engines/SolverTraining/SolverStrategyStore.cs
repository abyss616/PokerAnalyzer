using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines.SolverTraining;

public sealed class SolverStrategyStore
{
    private readonly Dictionary<string, SolverStrategyRow> _rows = new(StringComparer.Ordinal);

    public SolverStrategyRow GetOrCreate(string canonicalInfoSetKey, IReadOnlyList<LegalAction> legalActions)
    {
        if (canonicalInfoSetKey is null)
            throw new ArgumentNullException(nameof(canonicalInfoSetKey));

        if (legalActions is null)
            throw new ArgumentNullException(nameof(legalActions));

        if (legalActions.Count == 0)
            throw new InvalidOperationException("Cannot create a strategy row without legal actions.");

        if (_rows.TryGetValue(canonicalInfoSetKey, out var existing))
        {
            if (HasSameActions(existing.LegalActions, legalActions))
                return existing;

            // Keep this robust for early training loops: if the action abstraction for this key changes,
            // reset behavior probabilities to a fresh uniform distribution over the latest legal action set.
            var replaced = CreateUniformRow(canonicalInfoSetKey, legalActions);
            _rows[canonicalInfoSetKey] = replaced;
            return replaced;
        }

        var row = CreateUniformRow(canonicalInfoSetKey, legalActions);
        _rows.Add(canonicalInfoSetKey, row);
        return row;
    }

    private static SolverStrategyRow CreateUniformRow(string canonicalInfoSetKey, IReadOnlyList<LegalAction> legalActions)
    {
        var probability = 1d / legalActions.Count;
        var probabilities = Enumerable.Repeat(probability, legalActions.Count).ToArray();
        return new SolverStrategyRow(canonicalInfoSetKey, legalActions.ToArray(), probabilities);
    }

    private static bool HasSameActions(IReadOnlyList<LegalAction> left, IReadOnlyList<LegalAction> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }
}
