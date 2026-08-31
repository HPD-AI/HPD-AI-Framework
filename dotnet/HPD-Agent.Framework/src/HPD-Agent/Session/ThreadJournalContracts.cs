namespace HPD.Agent;

/// <summary>Stable identity of one thread journal.</summary>
public readonly record struct ThreadKey(string SessionId, string ThreadId);

/// <summary>Position in one immutable generation of a thread journal.</summary>
public readonly record struct ThreadJournalCursor(long Generation, long SequenceNumber)
{
    public static ThreadJournalCursor Start(long generation) => new(generation, 0);
}

/// <summary>Optimistic condition applied to an atomic journal append.</summary>
public readonly record struct ThreadAppendCondition(ThreadJournalCursor? ExpectedCursor = null)
{
    public static ThreadAppendCondition Any => default;
}

/// <summary>Result of atomically committing events to one thread journal.</summary>
public sealed record ThreadEventAppendResult(
    IReadOnlyList<AgentEvent> CommittedEvents,
    ThreadJournalCursor PreviousCursor,
    ThreadJournalCursor CurrentCursor);

/// <summary>Result of atomically replacing a thread journal generation.</summary>
public sealed record ThreadJournalReplaceResult(
    IReadOnlyList<AgentEvent> CommittedEvents,
    ThreadJournalCursor PreviousCursor,
    ThreadJournalCursor CurrentCursor);

/// <summary>Lightweight metadata for a thread journal. Reading it never projects event history.</summary>
public sealed record ThreadDescriptor(
    ThreadKey Key,
    ThreadDefaultAgentBinding DefaultAgent,
    string? Name,
    string? Description,
    IReadOnlyList<string> Tags,
    ThreadKind Kind,
    ThreadVisibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Generation,
    long Head,
    int MessageCount,
    ThreadForkDescriptor? Fork,
    ThreadRuntimeChildDescriptor? RuntimeChild,
    IReadOnlyDictionary<string, object> Metadata);

public sealed record ThreadForkDescriptor(
    string SourceThreadId,
    string? MessageId,
    int? MessageIndex);

/// <summary>Agent definition used when a caller resumes a thread without selecting another agent.</summary>
public sealed record ThreadDefaultAgentBinding(string AgentId, long? AgentRevision = null);

public sealed record ThreadRuntimeChildDescriptor(
    string? ParentSessionId,
    string? ParentThreadId,
    string? SubAgentName,
    string? InvocationId,
    string? SubAgentSourceKind,
    string? ParentToolCallId,
    string? ContextPolicy,
    string? Status);

public sealed record ThreadListRequest(
    bool IncludeHidden = true,
    int MaxCount = int.MaxValue);

/// <summary>Metadata-only view of the current journal head.</summary>
public sealed record ThreadEventHead(
    long Generation,
    long ThreadSequenceNumber,
    DateTimeOffset UpdatedAt)
{
    public ThreadJournalCursor Cursor => new(Generation, ThreadSequenceNumber);
}

/// <summary>A bounded, sequence-native journal read.</summary>
public sealed record ThreadEventReadRequest(
    ThreadJournalCursor After,
    long? Through = null,
    int MaxBatchEventCount = 256);

/// <summary>A contiguous batch of canonical thread events.</summary>
public sealed record ThreadEventBatch(
    IReadOnlyList<AgentEvent> Events,
    long Generation,
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
    public ThreadAppendConflictException(ThreadKey thread, ThreadJournalCursor expected, ThreadJournalCursor actual)
        : base($"Thread '{thread.ThreadId}' cursor mismatch. Expected {expected.Generation}:{expected.SequenceNumber}, actual {actual.Generation}:{actual.SequenceNumber}.")
    {
        Thread = thread;
        ExpectedCursor = expected;
        ActualCursor = actual;
    }

    public ThreadKey Thread { get; }
    public ThreadJournalCursor ExpectedCursor { get; }
    public ThreadJournalCursor ActualCursor { get; }
}

public sealed class ThreadCursorConflictException : InvalidOperationException
{
    public ThreadCursorConflictException(ThreadKey thread, ThreadJournalCursor cursor, ThreadJournalCursor head)
        : base($"Thread '{thread.ThreadId}' cursor {cursor.Generation}:{cursor.SequenceNumber} is incompatible with head {head.Generation}:{head.SequenceNumber}.")
    {
        Thread = thread;
        Cursor = cursor;
        Head = head;
    }

    public ThreadKey Thread { get; }
    public ThreadJournalCursor Cursor { get; }
    public ThreadJournalCursor Head { get; }
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

public sealed class ThreadJournalReplacedException : InvalidOperationException
{
    public ThreadJournalReplacedException(ThreadKey thread, ThreadJournalCursor previous, ThreadJournalCursor current)
        : base($"Thread '{thread.ThreadId}' journal was replaced from generation {previous.Generation} to {current.Generation}. Rehydrate the current generation.")
    {
        Thread = thread;
        PreviousCursor = previous;
        CurrentCursor = current;
    }

    public ThreadKey Thread { get; }
    public ThreadJournalCursor PreviousCursor { get; }
    public ThreadJournalCursor CurrentCursor { get; }
}
