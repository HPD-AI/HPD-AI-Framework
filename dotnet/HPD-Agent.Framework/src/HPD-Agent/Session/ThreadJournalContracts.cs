namespace HPD.Agent;

/// <summary>Stable identity of one thread journal.</summary>
public readonly record struct ThreadKey(string SessionId, string ThreadId);

/// <summary>Optimistic condition applied to an atomic journal append.</summary>
public readonly record struct ThreadAppendCondition(long? ExpectedHead = null)
{
    public static ThreadAppendCondition Any => default;
}

/// <summary>Result of atomically committing events to one thread journal.</summary>
public sealed record ThreadEventAppendResult(
    IReadOnlyList<AgentEvent> CommittedEvents,
    long PreviousHead,
    long CurrentHead);

/// <summary>Lightweight metadata for a thread journal. Reading it never projects event history.</summary>
public sealed record ThreadDescriptor(
    ThreadKey Key,
    string? Name,
    string? Description,
    IReadOnlyList<string> Tags,
    ThreadKind Kind,
    ThreadVisibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Head,
    int MessageCount,
    ThreadForkDescriptor? Fork,
    ThreadRuntimeChildDescriptor? RuntimeChild,
    IReadOnlyDictionary<string, object> Metadata);

public sealed record ThreadForkDescriptor(
    string SourceThreadId,
    string? MessageId,
    int? MessageIndex);

public sealed record ThreadRuntimeChildDescriptor(
    string? ParentSessionId,
    string? ParentThreadId,
    string? SubAgentName,
    string? SubAgentRunId,
    string? SubAgentSourceKind,
    string? ParentToolCallId,
    string? SessionPolicy,
    string? ThreadPolicy);

public sealed record ThreadListRequest(
    bool IncludeHidden = true,
    int MaxCount = int.MaxValue);

/// <summary>Metadata-only view of the current journal head.</summary>
public sealed record ThreadEventHead(
    long ThreadSequenceNumber,
    DateTimeOffset UpdatedAt);

/// <summary>A bounded, sequence-native journal read.</summary>
public sealed record ThreadEventReadRequest(
    long After = 0,
    long? Through = null,
    int MaxBatchEventCount = 256);

/// <summary>A contiguous batch of canonical thread events.</summary>
public sealed record ThreadEventBatch(
    IReadOnlyList<AgentEvent> Events,
    long FirstThreadSequenceNumber,
    long LastThreadSequenceNumber);

/// <summary>Controls store-level catch-up and committed observation.</summary>
public sealed record ThreadObservationOptions(
    int MaxBatchEventCount = 256);

public sealed record FileThreadDescriptorState(
    string Schema,
    int Version,
    ThreadDescriptor Descriptor,
    IReadOnlyList<string> MessageIds,
    long CurrentSegmentStart,
    int CurrentSegmentEventCount);

/// <summary>Raised when an optimistic journal append observes a different head.</summary>
public sealed class ThreadAppendConflictException : InvalidOperationException
{
    public ThreadAppendConflictException(ThreadKey thread, long expectedHead, long actualHead)
        : base($"Thread '{thread.ThreadId}' head mismatch. Expected {expectedHead}, actual {actualHead}.")
    {
        Thread = thread;
        ExpectedHead = expectedHead;
        ActualHead = actualHead;
    }

    public ThreadKey Thread { get; }
    public long ExpectedHead { get; }
    public long ActualHead { get; }
}

public sealed class ThreadCursorConflictException : InvalidOperationException
{
    public ThreadCursorConflictException(ThreadKey thread, long cursor, long head)
        : base($"Thread '{thread.ThreadId}' cursor {cursor} is ahead of head {head}.")
    {
        Thread = thread;
        Cursor = cursor;
        Head = head;
    }

    public ThreadKey Thread { get; }
    public long Cursor { get; }
    public long Head { get; }
}

public sealed class ThreadDeletedException : InvalidOperationException
{
    public ThreadDeletedException(ThreadKey thread)
        : base($"Thread '{thread.ThreadId}' in session '{thread.SessionId}' was deleted.")
    {
        Thread = thread;
    }

    public ThreadKey Thread { get; }
}
