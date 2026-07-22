using HPD.Agent;
using HPD.Events;

namespace HPDOS.ToolHarnesses.Middleware;

public interface IDebugLifecycleEventPublisher
{
    ValueTask PublishAsync(AgentEvent @event, bool durable, CancellationToken cancellationToken = default);
}

public abstract record DebugLifecycleEvent : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;
    public required string DebugTreeId { get; init; }
    public required string DebugSessionId { get; init; }
    public required string AdapterId { get; init; }
}

public sealed record DebugTreeStartedEvent : DebugLifecycleEvent
{
    public required string EnvironmentId { get; init; }
    public required bool IsAttach { get; init; }
}

public sealed record DebugSessionStateChangedEvent : DebugLifecycleEvent
{
    public required string Status { get; init; }
    public int? AdapterThreadId { get; init; }
    public string? Reason { get; init; }
    public long? SuspensionEpoch { get; init; }
}

public sealed record DebugSessionExitedEvent : DebugLifecycleEvent
{
    public DebugSessionExitedEvent() => CanInterrupt = false;
    public required int ExitCode { get; init; }
}

public sealed record DebugSessionTerminatedEvent : DebugLifecycleEvent
{
    public DebugSessionTerminatedEvent() => CanInterrupt = false;
    public bool RestartRequested { get; init; }
}

public sealed record DebugTreeFaultedEvent : DebugLifecycleEvent
{
    public DebugTreeFaultedEvent() => CanInterrupt = false;
    public required string SafeReasonCode { get; init; }
}

public sealed record DebugSessionFailedEvent : DebugLifecycleEvent
{
    public DebugSessionFailedEvent() => CanInterrupt = false;
    public required string SafeReasonCode { get; init; }
}

public sealed record DebugTreeTerminatedEvent : DebugLifecycleEvent
{
    public DebugTreeTerminatedEvent() => CanInterrupt = false;
    public required string SafeReasonCode { get; init; }
}

public sealed record DebugSessionSummaryEvent : DebugLifecycleEvent
{
    public DebugSessionSummaryEvent() => CanInterrupt = false;
    public required string FinalStatus { get; init; }
    public int? ExitCode { get; init; }
    public required long DurationMilliseconds { get; init; }
    public required int ChildSessionCount { get; init; }
    public required long RetainedOutputBytes { get; init; }
    public required long DroppedOutputRecords { get; init; }
    public required long DroppedOutputBytes { get; init; }
    public required long ProjectionFailures { get; init; }
}

public sealed record DebugRestartTransitionEvent : DebugLifecycleEvent
{
    public required bool InPlace { get; init; }
}

public sealed record DebugChildSessionStartedEvent : DebugLifecycleEvent
{
    public required string ParentDebugSessionId { get; init; }
    public required bool IsAttach { get; init; }
    public string? OutputPresentation { get; init; }
}

public sealed record DebugBreakpointChangedEvent : DebugLifecycleEvent
{
    public required string Reason { get; init; }
    public int? BreakpointId { get; init; }
    public required bool Verified { get; init; }
    public string? Message { get; init; }
    public string? SourcePath { get; init; }
    public long? Line { get; init; }
    public long? Column { get; init; }
    public string? InstructionReference { get; init; }
}

public sealed record DebugSessionStoppedEvent : DebugLifecycleEvent
{
    public int? AdapterThreadId { get; init; }
    public required string Reason { get; init; }
    public string? Description { get; init; }
    public long? SuspensionEpoch { get; init; }
}

public sealed record DebugSessionContinuedEvent : DebugLifecycleEvent
{
    public required int AdapterThreadId { get; init; }
    public bool AllThreadsContinued { get; init; }
}

public abstract record DebugProjectionEvent : DebugLifecycleEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

public sealed record DebugProcessChangedEvent : DebugProjectionEvent
{
    public required string Name { get; init; }
    public int? SystemProcessId { get; init; }
    public bool? IsLocalProcess { get; init; }
    public string? StartMethod { get; init; }
}

public sealed record DebugThreadChangedEvent : DebugProjectionEvent
{
    public required string Reason { get; init; }
    public required int AdapterThreadId { get; init; }
}

public sealed record DebugModuleChangedEvent : DebugProjectionEvent
{
    public required string Reason { get; init; }
    public required string OpaqueModuleId { get; init; }
    public required string Name { get; init; }
    public string? Path { get; init; }
}

public sealed record DebugLoadedSourceChangedEvent : DebugProjectionEvent
{
    public required string Reason { get; init; }
    public string? Name { get; init; }
    public string? Path { get; init; }
    public int? SourceReference { get; init; }
}

public sealed record DebugCapabilitiesChangedEvent : DebugProjectionEvent
{
    public required IReadOnlyList<string> Enabled { get; init; }
    public required IReadOnlyList<string> Disabled { get; init; }
}

public sealed record DebugStateInvalidatedEvent : DebugProjectionEvent
{
    public required IReadOnlyList<string> Areas { get; init; }
    public int? AdapterThreadId { get; init; }
    public int? StackFrameId { get; init; }
}

public sealed record DebugMemoryChangedEvent : DebugProjectionEvent
{
    public required string MemoryReferenceToken { get; init; }
    public required long Offset { get; init; }
    public required long Count { get; init; }
    public int InvalidatedRanges { get; init; }
}

public sealed record DebugOutputAvailableEvent : DebugProjectionEvent
{
    public override EventKind Kind { get; init; } = EventKind.Content;
    public required long FirstSequence { get; init; }
    public required long LastSequence { get; init; }
    public required string Category { get; init; }
    public string? InlineText { get; init; }
    public string? ContentScope { get; init; }
    public string? ContentId { get; init; }
    public string? ContentVersion { get; init; }
    public long DroppedRecords { get; init; }
    public long DroppedBytes { get; init; }
}

public abstract record DebugProgressEvent : DebugProjectionEvent
{
    public required string ProgressId { get; init; }
    public string? Message { get; init; }
    public double? Percentage { get; init; }
}

public sealed record DebugProgressStartedEvent : DebugProgressEvent
{
    public required string Title { get; init; }
    public bool Cancellable { get; init; }
}

public sealed record DebugProgressUpdatedEvent : DebugProgressEvent;

public sealed record DebugProgressCompletedEvent : DebugProgressEvent;
