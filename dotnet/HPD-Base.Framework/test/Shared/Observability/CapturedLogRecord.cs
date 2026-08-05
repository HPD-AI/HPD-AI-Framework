using Microsoft.Extensions.Logging;

namespace HPD.Base.Tests.Observability;

internal sealed record CapturedLogRecord(
    long Sequence,
    DateTimeOffset Timestamp,
    string Category,
    LogLevel Level,
    EventId EventId,
    string? OriginalFormat,
    string RenderedMessage,
    IReadOnlyList<KeyValuePair<string, object?>> State,
    IReadOnlyList<object?> Scopes,
    Exception? Exception);
