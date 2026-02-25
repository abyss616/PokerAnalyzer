using System.Collections.Concurrent;

namespace PokerAnalyzer.Api.Logging;

public sealed class UiLogStore
{
    private const int MaxEntriesPerCorrelation = 500;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<UiLogEntry>> _entriesByCorrelationId = new(StringComparer.Ordinal);

    public void Clear(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return;

        _entriesByCorrelationId.TryRemove(correlationId, out _);
    }

    public void Add(UiLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.CorrelationId))
            return;

        var queue = _entriesByCorrelationId.GetOrAdd(entry.CorrelationId, _ => new ConcurrentQueue<UiLogEntry>());
        queue.Enqueue(entry);

        while (queue.Count > MaxEntriesPerCorrelation && queue.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<UiLogEntry> GetSnapshot(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return Array.Empty<UiLogEntry>();

        if (!_entriesByCorrelationId.TryGetValue(correlationId, out var queue))
            return Array.Empty<UiLogEntry>();

        return queue.ToArray();
    }
}

public sealed record UiLogEntry(DateTimeOffset Timestamp, string Level, string Category, string Message, string CorrelationId);
