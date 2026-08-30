using HPD.Agent;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Events;
using HPD.Agent.Serialization;

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
    /// <summary>
    /// Model-facing tool call that causally originated this semantic fact, when one exists.
    /// Tree-owned background events retain the originating start call.
    /// </summary>
    public string? ToolCallId { get; init; }
}

[EventType("DEBUG_TREE_STARTED", Durability = AgentEventDurability.Durable)]
public sealed record DebugTreeStartedEvent : DebugLifecycleEvent
{
    public required string EnvironmentId { get; init; }
    public required DebugSemanticStartKind SemanticStartKind { get; init; }
    public required DebugAdapterStartMethod AdapterStartMethod { get; init; }
    public required string ExecutionPlannerId { get; init; }
}

/// <summary>Durable evidence that an inert semantic execution plan was selected.</summary>
[EventType("DEBUG_EXECUTION_PLANNED", Durability = AgentEventDurability.Durable)]
public sealed record DebugExecutionPlannedEvent : DebugLifecycleEvent
{
    public required DebugSemanticStartKind SemanticStartKind { get; init; }
    public required DebugAdapterStartMethod AdapterStartMethod { get; init; }
    public required string ExecutionPlannerId { get; init; }
}

/// <summary>Durable evidence that tree-scoped activation has begun.</summary>
[EventType("DEBUG_EXECUTION_ACTIVATING", Durability = AgentEventDurability.Durable)]
public sealed record DebugExecutionActivatingEvent : DebugLifecycleEvent
{
    public required DebugSemanticStartKind SemanticStartKind { get; init; }
    public required DebugAdapterStartMethod AdapterStartMethod { get; init; }
    public required string ExecutionPlannerId { get; init; }
}

/// <summary>Durable evidence that an HPD-owned prerequisite host started.</summary>
[EventType("DEBUG_HOST_PROCESS_STARTED", Durability = AgentEventDurability.Durable)]
public sealed record DebugHostProcessStartedEvent : DebugLifecycleEvent
{
    public required string SafeProcessRole { get; init; }
}

/// <summary>Durable evidence that a hosted debuggee reported trusted readiness.</summary>
[EventType("DEBUG_HOST_READY", Durability = AgentEventDurability.Durable)]
public sealed record DebugHostReadyEvent : DebugLifecycleEvent
{
    public required string SafeProcessRole { get; init; }
}

/// <summary>Durable evidence that an owned host exited before or after readiness.</summary>
[EventType("DEBUG_HOST_PROCESS_EXITED", Durability = AgentEventDurability.Durable)]
public sealed record DebugHostProcessExitedEvent : DebugLifecycleEvent
{
    public required string SafeProcessRole { get; init; }
    public int? ExitCode { get; init; }
}

/// <summary>Durable classified failure from tree-scoped execution activation.</summary>
[EventType("DEBUG_EXECUTION_ACTIVATION_FAILED", Durability = AgentEventDurability.Durable)]
public sealed record DebugExecutionActivationFailedEvent : DebugLifecycleEvent
{
    public DebugExecutionActivationFailedEvent() => CanInterrupt = false;
    public required string ExecutionPlannerId { get; init; }
    public required string SafeReasonCode { get; init; }
}

/// <summary>Durable safe diagnostic for a failed owned-resource cleanup.</summary>
[EventType("DEBUG_OWNED_RESOURCE_CLEANUP_FAILED", Durability = AgentEventDurability.Durable)]
public sealed record DebugOwnedResourceCleanupFailedEvent : DebugLifecycleEvent
{
    public DebugOwnedResourceCleanupFailedEvent() => CanInterrupt = false;
    public required string SafeResourceKind { get; init; }
    public required string SafeResourceIdentity { get; init; }
}

/// <summary>Durable evidence that bounded terminal-tree state was retained.</summary>
[EventType("DEBUG_TERMINAL_RECORD_RETAINED", Durability = AgentEventDurability.Durable)]
public sealed record DebugTerminalRecordRetainedEvent : DebugLifecycleEvent
{
    public DebugTerminalRecordRetainedEvent() => CanInterrupt = false;
    public required string FinalStatus { get; init; }
}

/// <summary>Durable evidence that a bounded terminal record was evicted.</summary>
[EventType("DEBUG_TERMINAL_RECORD_EVICTED", Durability = AgentEventDurability.Durable)]
public sealed record DebugTerminalRecordEvictedEvent : DebugLifecycleEvent
{
    public DebugTerminalRecordEvictedEvent() => CanInterrupt = false;
    public required string SafeReasonCode { get; init; }
}

[EventType("DEBUG_SESSION_STATE_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record DebugSessionStateChangedEvent : DebugLifecycleEvent
{
    public required string Status { get; init; }
    public int? AdapterThreadId { get; init; }
    public string? Reason { get; init; }
    public long? SuspensionEpoch { get; init; }
}

[EventType("DEBUG_SESSION_EXITED", Durability = AgentEventDurability.Durable)]
public sealed record DebugSessionExitedEvent : DebugLifecycleEvent
{
    public DebugSessionExitedEvent() => CanInterrupt = false;
    public required int ExitCode { get; init; }
}

[EventType("DEBUG_SESSION_TERMINATED", Durability = AgentEventDurability.Durable)]
public sealed record DebugSessionTerminatedEvent : DebugLifecycleEvent
{
    public DebugSessionTerminatedEvent() => CanInterrupt = false;
    public bool RestartRequested { get; init; }
}

[EventType("DEBUG_TREE_FAULTED", Durability = AgentEventDurability.Durable)]
public sealed record DebugTreeFaultedEvent : DebugLifecycleEvent
{
    public DebugTreeFaultedEvent() => CanInterrupt = false;
    public required string SafeReasonCode { get; init; }
}

[EventType("DEBUG_SESSION_FAILED", Durability = AgentEventDurability.Durable)]
public sealed record DebugSessionFailedEvent : DebugLifecycleEvent
{
    public DebugSessionFailedEvent() => CanInterrupt = false;
    public required string SafeReasonCode { get; init; }
}

[EventType("DEBUG_TREE_TERMINATED", Durability = AgentEventDurability.Durable)]
public sealed record DebugTreeTerminatedEvent : DebugLifecycleEvent
{
    public DebugTreeTerminatedEvent() => CanInterrupt = false;
    public required string SafeReasonCode { get; init; }
}

[EventType("DEBUG_TREE_COMPLETED", Durability = AgentEventDurability.Durable)]
public sealed record DebugTreeCompletedEvent : DebugLifecycleEvent
{
    public DebugTreeCompletedEvent() => CanInterrupt = false;
    public required string FinalStatus { get; init; }
    public int? ExitCode { get; init; }
    public required long DurationMilliseconds { get; init; }
    public required int SessionCount { get; init; }
    public required int ChildSessionCount { get; init; }
    public required DebugBreakpointCounts Breakpoints { get; init; }
    public required int BreakpointStopCount { get; init; }
    public required long RetainedOutputBytes { get; init; }
    public required long DroppedOutputRecords { get; init; }
    public required long DroppedOutputBytes { get; init; }
    public required long ProjectionFailures { get; init; }
    public string? SafeReasonCode { get; init; }
}

[EventType("DEBUG_RESTART_TRANSITION", Durability = AgentEventDurability.Durable)]
public sealed record DebugRestartTransitionEvent : DebugLifecycleEvent
{
    public required bool InPlace { get; init; }
}

[EventType("DEBUG_CHILD_SESSION_STARTED", Durability = AgentEventDurability.Durable)]
public sealed record DebugChildSessionStartedEvent : DebugLifecycleEvent
{
    public required string ParentDebugSessionId { get; init; }
    public required DebugAdapterStartMethod AdapterStartMethod { get; init; }
    public string? OutputPresentation { get; init; }
}

[EventType("DEBUG_BREAKPOINT_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record DebugBreakpointChangedEvent : DebugLifecycleEvent
{
    public required string ClientBreakpointId { get; init; }
    public required DebugBreakpointKind BreakpointKind { get; init; }
    public required DebugBreakpointChangeKind Change { get; init; }
    public required bool Acknowledged { get; init; }
    public required bool Verified { get; init; }
    public string? SafeMessage { get; init; }
    public string? DisplayPath { get; init; }
    public long? ResolvedLine { get; init; }
    public long? ResolvedColumn { get; init; }
    public string? InstructionReferenceToken { get; init; }
}

/// <summary>
/// Durable semantic evidence that one model-facing operation committed a
/// breakpoint selection.
/// </summary>
[EventType("DEBUG_BREAKPOINT_SELECTION_APPLIED", Durability = AgentEventDurability.Durable)]
public sealed record DebugBreakpointSelectionAppliedEvent : DebugLifecycleEvent
{
    public required string ToolCallId { get; init; }
    public required string Action { get; init; }
    public required DebugBreakpointKind BreakpointKind { get; init; }
    public required IReadOnlyList<DebugBreakpointSelectionEventItem> Before { get; init; }
    public required IReadOnlyList<DebugBreakpointSelectionEventItem> After { get; init; }
    public required IReadOnlyList<DebugBreakpointSelectionDelta> Changes { get; init; }
    public required DebugBreakpointCounts Counts { get; init; }
    public IReadOnlyList<DebugSourcePreview> SourcePreviews { get; init; } = [];
    public required bool DetailsTruncated { get; init; }
    public string? TruncationReason { get; init; }
}

/// <summary>A bounded, adapter-safe breakpoint selection item.</summary>
public sealed record DebugBreakpointSelectionEventItem
{
    public required string ClientBreakpointId { get; init; }
    public required DebugBreakpointKind Kind { get; init; }
    public string? DisplayPath { get; init; }
    public long? RequestedLine { get; init; }
    public long? RequestedColumn { get; init; }
    public long? ResolvedLine { get; init; }
    public long? ResolvedColumn { get; init; }
    public string? SafeDisplayName { get; init; }
    public string? Condition { get; init; }
    public string? HitCondition { get; init; }
    public string? LogMessage { get; init; }
    public required bool Acknowledged { get; init; }
    public required bool Verified { get; init; }
    public string? SafeMessage { get; init; }
}

/// <summary>One stable breakpoint identity transition.</summary>
public sealed record DebugBreakpointSelectionDelta(
    string ClientBreakpointId,
    DebugBreakpointSelectionDeltaKind Kind);

/// <summary>Describes how one stable breakpoint selection changed.</summary>
public enum DebugBreakpointSelectionDeltaKind
{
    /// <summary>The breakpoint was absent before the committed mutation.</summary>
    Added,
    /// <summary>The breakpoint was absent after the committed mutation.</summary>
    Removed,
    /// <summary>The breakpoint identity remained but its semantic presentation changed.</summary>
    Updated,
    /// <summary>The breakpoint identity and semantic presentation were retained.</summary>
    Unchanged
}

/// <summary>Trusted bounded source text captured for presentation and replay.</summary>
public sealed record DebugSourcePreview
{
    public required string DisplayPath { get; init; }
    public string? Language { get; init; }
    public string? ContentHash { get; init; }
    public string? SourceVersion { get; init; }
    public required IReadOnlyList<DebugSourcePreviewHunk> Hunks { get; init; }
    public required bool Truncated { get; init; }
    public string? UnavailableReason { get; init; }
}

/// <summary>A contiguous, one-based range of bounded source-preview lines.</summary>
public sealed record DebugSourcePreviewHunk(
    int StartLine,
    IReadOnlyList<string> Lines);

[EventType("DEBUG_SESSION_STOPPED", Durability = AgentEventDurability.Durable)]
public sealed record DebugSessionStoppedEvent : DebugLifecycleEvent
{
    public int? AdapterThreadId { get; init; }
    public required string Reason { get; init; }
    public string? Description { get; init; }
    public long? SuspensionEpoch { get; init; }
}

/// <summary>Bounded semantic projection of the one primary stop for a suspension.</summary>
[EventType("DEBUG_PRIMARY_STOP_AVAILABLE", Durability = AgentEventDurability.Durable)]
public sealed record DebugPrimaryStopAvailableEvent : DebugLifecycleEvent
{
    public required int AdapterThreadId { get; init; }
    public required long SuspensionEpoch { get; init; }
    public required string Reason { get; init; }
    public string? Description { get; init; }
    public string? FrameName { get; init; }
    public string? DisplayPath { get; init; }
    public long? Line { get; init; }
    public long? Column { get; init; }
    public DebugSourcePreview? SourcePreview { get; init; }
    public required bool InspectionSucceeded { get; init; }
    public string? SafeFailureCode { get; init; }
    public IReadOnlyList<string> HitBreakpointClientIds { get; init; } = [];
    public required bool HitBreakpointIdentityUnknown { get; init; }
}

[EventType("DEBUG_SESSION_CONTINUED", Durability = AgentEventDurability.Durable)]
public sealed record DebugSessionContinuedEvent : DebugLifecycleEvent
{
    public required int AdapterThreadId { get; init; }
    public bool AllThreadsContinued { get; init; }
}

/// <summary>One successfully applied model-facing execution command.</summary>
[EventType("DEBUG_EXECUTION_COMMAND_APPLIED", Durability = AgentEventDurability.Durable)]
public sealed record DebugExecutionCommandAppliedEvent : DebugLifecycleEvent
{
    public required DebugExecutionCommand Command { get; init; }
    public int? AdapterThreadId { get; init; }
}

public enum DebugExecutionCommand
{
    Continue,
    Pause,
    StepOver,
    StepIn,
    StepOut,
    StepBack,
    ReverseContinue,
    RestartFrame,
    Goto,
    TerminateThreads
}

/// <summary>One successfully applied model-facing mutation of debuggee state.</summary>
[EventType("DEBUG_STATE_MUTATION_APPLIED", Durability = AgentEventDurability.Durable)]
public sealed record DebugStateMutationAppliedEvent : DebugLifecycleEvent
{
    public required DebugStateMutationKind MutationKind { get; init; }
    public string? SafeTargetName { get; init; }
    public string? SafeNewValue { get; init; }
    public long? ByteCount { get; init; }
}

public enum DebugStateMutationKind
{
    Variable,
    Expression,
    Memory
}

public abstract record DebugProjectionEvent : DebugLifecycleEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

[EventType("DEBUG_PROCESS_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record DebugProcessChangedEvent : DebugProjectionEvent
{
    public required string Name { get; init; }
    public int? SystemProcessId { get; init; }
    public bool? IsLocalProcess { get; init; }
    public string? StartMethod { get; init; }
}

[EventType("DEBUG_THREAD_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record DebugThreadChangedEvent : DebugProjectionEvent
{
    public required string Reason { get; init; }
    public required int AdapterThreadId { get; init; }
}

[EventType("DEBUG_MODULE_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record DebugModuleChangedEvent : DebugProjectionEvent
{
    public required string Reason { get; init; }
    public required string OpaqueModuleId { get; init; }
    public required string Name { get; init; }
    public string? Path { get; init; }
}

[EventType("DEBUG_LOADED_SOURCE_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record DebugLoadedSourceChangedEvent : DebugProjectionEvent
{
    public required string Reason { get; init; }
    public string? Name { get; init; }
    public string? Path { get; init; }
    public int? SourceReference { get; init; }
}

[EventType("DEBUG_CAPABILITIES_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record DebugCapabilitiesChangedEvent : DebugProjectionEvent
{
    public required IReadOnlyList<string> Enabled { get; init; }
    public required IReadOnlyList<string> Disabled { get; init; }
}

[EventType("DEBUG_STATE_INVALIDATED", Durability = AgentEventDurability.Durable)]
public sealed record DebugStateInvalidatedEvent : DebugProjectionEvent
{
    public required IReadOnlyList<string> Areas { get; init; }
    public int? AdapterThreadId { get; init; }
    public int? StackFrameId { get; init; }
}

[EventType("DEBUG_MEMORY_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record DebugMemoryChangedEvent : DebugProjectionEvent
{
    public required string MemoryReferenceToken { get; init; }
    public required long Offset { get; init; }
    public required long Count { get; init; }
    public int InvalidatedRanges { get; init; }
}

[EventType("DEBUG_OUTPUT_AVAILABLE", Durability = AgentEventDurability.Durable)]
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

[EventType("DEBUG_PROGRESS_STARTED", Durability = AgentEventDurability.Durable)]
public sealed record DebugProgressStartedEvent : DebugProgressEvent
{
    public required string Title { get; init; }
    public bool Cancellable { get; init; }
}

[EventType("DEBUG_PROGRESS_UPDATED", Durability = AgentEventDurability.Durable)]
public sealed record DebugProgressUpdatedEvent : DebugProgressEvent;

[EventType("DEBUG_PROGRESS_COMPLETED", Durability = AgentEventDurability.Durable)]
public sealed record DebugProgressCompletedEvent : DebugProgressEvent;
