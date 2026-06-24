using HPD.Events;

namespace HPD.Agent.Middleware;

/// <summary>
/// Descriptor for runtime-owned background work.
/// </summary>
public sealed record BackgroundTaskDescriptor
{
    public required string Name { get; init; }

    public required BackgroundTaskSourceKind SourceKind { get; init; }

    public required BackgroundTaskNotificationPolicy NotificationPolicy { get; init; }

    public string? SourceId { get; init; }

    public string? ParentRuntimeRunId { get; init; }

    public string? SessionId { get; init; }

    public string? ThreadId { get; init; }

    public FunctionInvocationSnapshot? Invocation { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Safe runtime context supplied to background work.
/// </summary>
public sealed record BackgroundTaskContext
{
    public required string TaskId { get; init; }

    public required BackgroundTaskDescriptor Descriptor { get; init; }

    public string Name => Descriptor.Name;

    public BackgroundTaskSourceKind SourceKind => Descriptor.SourceKind;

    public BackgroundTaskNotificationPolicy NotificationPolicy => Descriptor.NotificationPolicy;

    public string? SourceId => Descriptor.SourceId;

    public string? ParentRuntimeRunId => Descriptor.ParentRuntimeRunId;

    public string? SessionId => Descriptor.SessionId;

    public string? ThreadId => Descriptor.ThreadId;

    public FunctionInvocationSnapshot? Invocation => Descriptor.Invocation;

    public IReadOnlyDictionary<string, string>? Metadata => Descriptor.Metadata;

    public IEventCoordinator? EventCoordinator { get; init; }

    public IEventFlowRegistry? EventFlows => EventCoordinator?.EventFlows;

    public IServiceProvider? Services { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}
