using Microsoft.Extensions.Logging;

namespace PokerAnalyzer.Api.Logging;

public sealed class UiLogLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly UiLogStore _store;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public UiLogLoggerProvider(UiLogStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName) => new UiLogLogger(categoryName, _store, () => _scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
    }

    private sealed class UiLogLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly UiLogStore _store;
        private readonly Func<IExternalScopeProvider> _scopeProviderAccessor;

        public UiLogLogger(string categoryName, UiLogStore store, Func<IExternalScopeProvider> scopeProviderAccessor)
        {
            _categoryName = categoryName;
            _store = store;
            _scopeProviderAccessor = scopeProviderAccessor;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            _scopeProviderAccessor().Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var correlationId = FindCorrelationId(state);
            if (string.IsNullOrWhiteSpace(correlationId))
                return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = $"{message}{Environment.NewLine}{exception}";

            _store.Add(new UiLogEntry(
                DateTimeOffset.UtcNow,
                logLevel.ToString(),
                _categoryName,
                message,
                correlationId));
        }

        private string? FindCorrelationId<TState>(TState state)
        {
            if (TryGetCorrelationId(state, out var correlationId))
                return correlationId;

            string? found = null;
            _scopeProviderAccessor().ForEachScope((scope, _) =>
            {
                if (found is not null)
                    return;

                if (TryGetCorrelationId(scope, out var value))
                    found = value;
            }, state);

            return found;
        }

        private static bool TryGetCorrelationId(object? source, out string? correlationId)
        {
            correlationId = null;
            if (source is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var kvp in values)
                {
                    if (string.Equals(kvp.Key, "CorrelationId", StringComparison.OrdinalIgnoreCase))
                    {
                        correlationId = kvp.Value?.ToString();
                        return !string.IsNullOrWhiteSpace(correlationId);
                    }
                }
            }

            return false;
        }
    }
}
