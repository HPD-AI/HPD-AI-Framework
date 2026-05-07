using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPDAgent.Graph.Connectors.Abstractions.Connections;

namespace HPDAgent.Graph.Connectors.Abstractions.Sources;

public sealed record WorkflowSourceDescriptor
{
    public required string SourceType { get; init; }
    public required string AppId { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public SourceTriggerKind TriggerKind { get; init; }
    public JsonElement? ConfigSchema { get; init; }
    public JsonElement? EventSchema { get; init; }
    public DedupeStrategy DefaultDedupeStrategy { get; init; } = DedupeStrategy.Unique;
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public enum SourceTriggerKind
{
    Webhook,
    Polling,
    Timer,
    Manual,
    Stream
}

public sealed record WorkflowSource
{
    public required string SourceId { get; init; }
    public required string GraphId { get; init; }
    public required string SourceType { get; init; }
    public string? ConnectionId { get; init; }

    public bool Enabled { get; init; } = true;
    public JsonElement? Config { get; init; }
    public JsonElement? DefaultInput { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record WorkflowSourceState
{
    public required string SourceId { get; init; }
    public JsonElement? Cursor { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; }
        = new Dictionary<string, string>();
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record WorkflowSourceStatus
{
    public required string SourceId { get; init; }
    public required string SourceType { get; init; }
    public bool Enabled { get; init; }
    public bool Active { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset? LastCheckedAt { get; init; }
    public DateTimeOffset? LastEventAt { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public interface IWorkflowSourceStore
{
    Task SaveAsync(WorkflowSource source, CancellationToken ct = default);
    Task<WorkflowSource?> LoadAsync(string sourceId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowSource>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowSource>> ListByGraphAsync(string graphId, CancellationToken ct = default);
    Task DeleteAsync(string sourceId, CancellationToken ct = default);

    Task<WorkflowSourceState?> LoadStateAsync(string sourceId, CancellationToken ct = default);
    Task SaveStateAsync(WorkflowSourceState state, CancellationToken ct = default);
}

public interface IWorkflowSourceProvider
{
    string SourceType { get; }

    Task RegisterAsync(WorkflowSource source, CancellationToken ct = default);
    Task UpdateAsync(WorkflowSource source, CancellationToken ct = default);
    Task UnregisterAsync(string sourceId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowSourceStatus>> GetStatusAsync(CancellationToken ct = default);
}

public interface IWebhookWorkflowSourceProvider : IWorkflowSourceProvider
{
    Task ReceiveAsync(
        WorkflowSource source,
        WebhookEnvelope envelope,
        CancellationToken ct = default);
}

public interface IPollingWorkflowSourceProvider : IWorkflowSourceProvider
{
    Task PollAsync(
        WorkflowSource source,
        WorkflowSourceState? state,
        CancellationToken ct = default);
}

public sealed record WebhookEnvelope
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public JsonElement? Body { get; init; }
    public byte[]? BodyBytes { get; init; }
    public string? EventType { get; init; }
    public string? QueryString { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record WorkflowSourceEvent(
    object Payload,
    string? EventId = null,
    string? Summary = null,
    DateTimeOffset? OccurredAt = null,
    DedupeStrategy? DedupeStrategy = null,
    IReadOnlyDictionary<string, object>? Metadata = null);

public enum DedupeStrategy
{
    None,
    Unique,
    Last,
    Greatest
}

public interface IWorkflowSourceDispatcher
{
    Task DispatchAsync(
        Events.WorkflowSourceEmittedEvent evt,
        CancellationToken ct = default);
}

public sealed record WebhookActivationContext
{
    public required WorkflowSource Source { get; init; }
    public required Uri Endpoint { get; init; }
    public required IWorkflowSourceStateAccessor State { get; init; }
    public required IConnectionProvider Connections { get; init; }
}

public sealed record PollingSourceContext
{
    public required WorkflowSource Source { get; init; }
    public required IWorkflowSourceStateAccessor State { get; init; }
    public required IConnectionProvider Connections { get; init; }
}

public interface IWorkflowSourceStateAccessor
{
    ValueTask<T?> GetAsync<T>(
        string key,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default);

    ValueTask SetAsync<T>(
        string key,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default);

    ValueTask RemoveAsync(string key, CancellationToken ct = default);
}
