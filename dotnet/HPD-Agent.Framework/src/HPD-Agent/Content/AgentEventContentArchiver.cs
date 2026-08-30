using System.Text;
using HPD.Agent.Serialization;

namespace HPD.Agent;

/// <summary>Reports a best-effort event archive failure without altering journal publication.</summary>
public readonly record struct AgentEventArchiveDiagnostic(
    Type EventType,
    string Reason,
    Exception? Exception = null) : AgentStructEvent
{
    public HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
    public long SequenceNumber { get; init; }
    public long TimestampNs { get; init; }
}

/// <summary>Archives canonical event representations after successful publication.</summary>
public interface IAgentEventContentArchiver
{
    /// <summary>Archives an event when its generated descriptor declares a content policy.</summary>
    ValueTask ArchiveAsync(
        AgentEventCodec codec,
        AgentEvent value,
        CancellationToken cancellationToken = default);
}

/// <summary>Idempotent content-store-backed event archiver.</summary>
public sealed class AgentEventContentArchiver : IAgentEventContentArchiver
{
    private readonly IContentStore? _store;
    private readonly Action<AgentEventArchiveDiagnostic>? _diagnostics;

    /// <summary>Creates an archiver. A null store makes policy-bearing publications diagnostic-only.</summary>
    public AgentEventContentArchiver(
        IContentStore? store,
        Action<AgentEventArchiveDiagnostic>? diagnostics = null)
    {
        _store = store;
        _diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public async ValueTask ArchiveAsync(
        AgentEventCodec codec,
        AgentEvent value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(value);
        if (!codec.TryGetByType(value.GetType(), out var descriptor))
            throw new InvalidOperationException($"Event '{value.GetType().FullName}' is absent from codec '{codec.Digest}'.");
        if (descriptor.ContentPolicy is not { } policy)
            return;
        if (_store is null)
        {
            Report(value, "No content store is configured.");
            return;
        }

        var scopeValue = policy.Scope ?? value.SessionId;
        if (string.IsNullOrWhiteSpace(scopeValue))
        {
            Report(value, "A retained event requires an explicit policy scope or a session identity.");
            return;
        }

        var eventId = string.IsNullOrWhiteSpace(value.EventId)
            ? throw new InvalidOperationException("A retained event requires an event identity.")
            : value.EventId;
        var contentId = $"events/{descriptor.Discriminator}/{eventId}.json";
        var scope = ContentScope.Create(scopeValue);

        try
        {
            if (await _store.StatAsync(new ContentAddress(scope, contentId), cancellationToken).ConfigureAwait(false) is not null)
                return;

            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = policy.Kind,
                ["event.type"] = descriptor.Discriminator,
                ["event.id"] = eventId
            };
            Add(tags, "session", value.SessionId);
            Add(tags, "thread", value.ThreadId);
            Add(tags, "trace", value.TraceId);
            Add(tags, "threadExecution", value.ThreadExecutionId);
            Add(tags, "span", value.SpanId);
            Add(tags, "agent.name", value.Metadata?.AgentName);
            Add(tags, "agent.id", value.Metadata?.AgentId);

            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(codec.Serialize(value)));
            await _store.WriteAsync(
                scope,
                stream,
                new ContentMetadata
                {
                    Name = contentId,
                    ContentType = policy.ContentType,
                    Origin = policy.Origin,
                    Tags = tags
                },
                new ContentWriteOptions
                {
                    Mode = ContentWriteMode.Create,
                    ContentId = contentId,
                    FailIfNameExists = true
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Publication already succeeded. Archival is observable best-effort work and never
            // changes caller-visible publication semantics or requests a journal retry.
            Report(value, "Content archival failed.", exception);
        }
    }

    private void Report(AgentEvent value, string reason, Exception? exception = null) =>
        _diagnostics?.Invoke(new AgentEventArchiveDiagnostic(value.GetType(), reason, exception));

    private static void Add(Dictionary<string, string> tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            tags[key] = value;
    }
}

internal sealed class NullAgentEventContentArchiver : IAgentEventContentArchiver
{
    public static NullAgentEventContentArchiver Instance { get; } = new();

    public ValueTask ArchiveAsync(AgentEventCodec codec, AgentEvent value, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
