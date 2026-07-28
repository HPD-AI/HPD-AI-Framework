using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Tests.Observability;

internal sealed class LogCollector : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLogRecord> _records = new();
    private readonly Func<string, LogLevel, bool> _isEnabled;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private long _sequence;

    public LogCollector(
        LogLevel minimumLevel = LogLevel.Trace,
        Func<string, LogLevel, bool>? filter = null)
    {
        _isEnabled = filter ?? ((_, level) => level >= minimumLevel);
    }

    public CapturedLogRecord[] Records => _records.OrderBy(record => record.Sequence).ToArray();

    public ILogger CreateLogger(string categoryName) => new CollectorLogger(this, categoryName);

    public ILogger<T> CreateLogger<T>() => new TypedCollectorLogger<T>(CreateLogger(typeof(T).FullName ?? typeof(T).Name));

    public CapturedLogRecord[] RecordsFor(int eventId) =>
        Records.Where(record => record.EventId.Id == eventId).ToArray();

    public CapturedLogRecord[] RecordsFor<T>() =>
        Records.Where(record => record.Category == (typeof(T).FullName ?? typeof(T).Name)).ToArray();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));

    public void Dispose()
    {
    }

    private bool IsEnabled(string category, LogLevel level) =>
        level != LogLevel.None && _isEnabled(category, level);

    private void Capture<TState>(
        string category,
        LogLevel level,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var stateSnapshot = SnapshotState(state);
        var scopes = new List<object?>();
        _scopeProvider.ForEachScope(static (scope, target) => target.Add(SnapshotValue(scope)), scopes);

        _records.Enqueue(new CapturedLogRecord(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            category,
            level,
            eventId,
            FindOriginalFormat(stateSnapshot),
            formatter(state, exception),
            stateSnapshot,
            scopes.ToArray(),
            exception));
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> SnapshotState<TState>(TState state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            return properties
                .Select(property => new KeyValuePair<string, object?>(
                    property.Key,
                    SnapshotValue(property.Value)))
                .ToArray();
        }

        return [new KeyValuePair<string, object?>("State", SnapshotValue(state))];
    }

    private static object? SnapshotValue(object? value) => value switch
    {
        null => null,
        string text => string.Concat(text),
        byte[] bytes => bytes.ToArray(),
        char[] characters => characters.ToArray(),
        Array array => array.Clone(),
        _ => value
    };

    private static string? FindOriginalFormat(IReadOnlyList<KeyValuePair<string, object?>> state) =>
        state.LastOrDefault(property => property.Key == "{OriginalFormat}").Value as string;

    private sealed class CollectorLogger(LogCollector owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            owner._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => owner.IsEnabled(category, logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                owner.Capture(category, logLevel, eventId, state, exception, formatter);
            }
        }
    }

    private sealed class TypedCollectorLogger<T>(ILogger logger) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => logger.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
