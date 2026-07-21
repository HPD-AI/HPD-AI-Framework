using HPD.Events;

namespace HPD.Agent.Middleware;

/// <summary>
/// Descriptor for runtime-owned background work.
/// </summary>
public sealed record BackgroundTaskDescriptor
{
    public required string Name { get; init; }

    public required BackgroundTaskSourceKind SourceKind { get; init; }

    /// <summary>
    /// Rule that controls whether final-state task events should wake the model.
    /// </summary>
    public BackgroundTaskNotificationRule Notification { get; init; } =
        BackgroundTaskNotificationRule.None;

    public string? SourceId { get; init; }

    public string? OriginatingThreadExecutionId { get; init; }

    public string? SessionId { get; init; }

    public string? ThreadId { get; init; }

    public FunctionInvocationSnapshot? Invocation { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Completion details reported by runtime-owned background work.
/// </summary>
public sealed record BackgroundTaskCompletion
{
    /// <summary>
    /// Gets the model-facing summary used by final-state notifications.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Gets metadata merged into the final completed event.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Well-known metadata keys for transitional background task notification controls.
/// </summary>
public static class BackgroundTaskNotificationMetadataKeys
{
    /// <summary>
    /// Metadata key used to explicitly suppress a final-state notification.
    /// </summary>
    public const string SuppressNotification = "notification.suppress";

    /// <summary>
    /// Metadata key containing the machine-readable reason for explicit notification suppression.
    /// </summary>
    public const string SuppressNotificationReason = "notification.suppressReason";
}

/// <summary>
/// Safe runtime context supplied to background work.
/// </summary>
public sealed record BackgroundTaskContext
{
    private readonly object _completionLock = new();
    private BackgroundTaskCompletion? _completion;

    public required string TaskId { get; init; }

    public required BackgroundTaskDescriptor Descriptor { get; init; }

    public string Name => Descriptor.Name;

    public BackgroundTaskSourceKind SourceKind => Descriptor.SourceKind;

    /// <summary>
    /// Rule that controls whether this task's final-state events should wake the model.
    /// </summary>
    public BackgroundTaskNotificationRule Notification => Descriptor.Notification;

    public string? SourceId => Descriptor.SourceId;

    public string? OriginatingThreadExecutionId => Descriptor.OriginatingThreadExecutionId;

    public string? SessionId => Descriptor.SessionId;

    public string? ThreadId => Descriptor.ThreadId;

    public FunctionInvocationSnapshot? Invocation => Descriptor.Invocation;

    public IReadOnlyDictionary<string, string>? Metadata => Descriptor.Metadata;

    /// <summary>
    /// Gets completion details reported by the background task body.
    /// </summary>
    public BackgroundTaskCompletion? Completion
    {
        get
        {
            lock (_completionLock)
                return _completion;
        }
    }

    public IEventCoordinator? EventCoordinator { get; init; }

    internal IThreadEventPublisher? ThreadEvents { get; init; }

    public IEventFlowRegistry? EventFlows => EventCoordinator?.EventFlows;

    public IServiceProvider? Services { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public async ValueTask<AgentEvent> PublishAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var sessionId = evt.SessionId ?? SessionId ?? Invocation?.SessionId;
        var threadId = evt.ThreadId ?? ThreadId ?? Invocation?.ThreadId;
        if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(threadId))
        {
            evt = evt with { SessionId = sessionId, ThreadId = threadId };
            if (ThreadEvents is not null)
            {
                return await ThreadEvents.CommitAndPublishAsync(
                    new ThreadKey(sessionId, threadId),
                    evt,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (EventCoordinator is null)
            throw new InvalidOperationException("Background task context does not have an event publisher.");

        await EventCoordinator.EmitAsync(evt, cancellationToken).ConfigureAwait(false);
        return evt;
    }

    /// <summary>
    /// Reports completion details for the background task.
    /// </summary>
    /// <param name="summary">The model-facing completion summary.</param>
    /// <param name="metadata">Additional metadata to merge into the completed event.</param>
    public void SetCompletion(
        string? summary = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        lock (_completionLock)
        {
            _completion = new BackgroundTaskCompletion
            {
                Summary = summary,
                Metadata = metadata
            };
        }
    }
}
