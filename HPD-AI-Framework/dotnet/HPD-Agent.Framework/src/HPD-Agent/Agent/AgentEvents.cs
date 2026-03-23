using System.Collections.Immutable;
using Microsoft.Extensions.AI;
// EventPriority and EventDirection are now in HPD.Events namespace
using EventPriority = HPD.Events.EventPriority;
using EventDirection = HPD.Events.EventDirection;

namespace HPD.Agent;
/// <summary>
/// Provides hierarchical context about which agent emitted an event.
/// Enables event attribution and filtering in multi-agent systems.
/// </summary>
public record AgentExecutionContext
{
    /// <summary>
    /// The immediate agent that emitted this event (e.g., "WeatherExpert")
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Hierarchical agent ID showing full execution path.
    /// Format: "parent-abc12345-weatherExpert-def67890"
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Parent agent ID (null if this is root orchestrator)
    /// </summary>
    public string? ParentAgentId { get; init; }

    /// <summary>
    /// Full agent chain from root to current.
    /// Example: ["Orchestrator", "DomainExpert", "WeatherExpert"]
    /// </summary>
    public IReadOnlyList<string> AgentChain { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Depth in the agent hierarchy (0 = root, 1 = direct SubAgent, etc.)
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Is this event from a SubAgent (vs root orchestrator)?
    /// </summary>
    public bool IsSubAgent => Depth > 0;
}




#region Priority and Direction Enums
// EventPriority and EventDirection enums moved to HPD.Events (imported via using aliases at top of file)
#endregion

#region Interruption Types

/// <summary>
/// Source of an interruption request.
/// </summary>
public enum InterruptionSource
{
    /// <summary>User-initiated (clicked stop, pressed Ctrl+C, etc.)</summary>
    User,

    /// <summary>System-initiated (timeout, circuit breaker, error threshold)</summary>
    System,

    /// <summary>Parent agent aborting child agent</summary>
    Parent,

    /// <summary>Middleware-initiated (permission denied, validation failed)</summary>
    Middleware
}

/// <summary>
/// Requests interruption of active streams or operations.
/// </summary>
public record InterruptionRequestEvent(
    string? StreamId,
    string Reason,
    InterruptionSource Source) : AgentEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

#endregion

#region Background Operation Types

/// <summary>
/// Status of a long-running background operation.
/// Used with AllowBackgroundResponses feature for tracking LLM operations
/// that run asynchronously on provider infrastructure.
/// </summary>
public readonly struct OperationStatus : IEquatable<OperationStatus>
{
    /// <summary>Operation has been accepted but not yet started.</summary>
    public static OperationStatus Queued { get; } = new("Queued");

    /// <summary>Operation is actively running.</summary>
    public static OperationStatus InProgress { get; } = new("InProgress");

    /// <summary>Operation completed successfully.</summary>
    public static OperationStatus Completed { get; } = new("Completed");

    /// <summary>Operation failed with an error.</summary>
    public static OperationStatus Failed { get; } = new("Failed");

    /// <summary>Operation was cancelled.</summary>
    public static OperationStatus Cancelled { get; } = new("Cancelled");

    /// <summary>The status value as a string.</summary>
    public string Value { get; }

    /// <summary>Creates a new OperationStatus with the specified value.</summary>
    public OperationStatus(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>Whether this status represents a terminal state (no further updates expected).</summary>
    public bool IsTerminal => this == Completed || this == Failed || this == Cancelled;

    /// <inheritdoc />
    public bool Equals(OperationStatus other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is OperationStatus other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(OperationStatus left, OperationStatus right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(OperationStatus left, OperationStatus right) => !left.Equals(right);
}

#endregion

/// <summary>
/// Protocol-agnostic internal events emitted by the agent core.
/// These events represent what actually happened during agent execution,
/// independent of any specific protocol.
///
/// KEY CONCEPTS:
/// - MESSAGE TURN: The entire user interaction (user sends message → agent responds)
///   May contain multiple agent turns if tools are called
/// - AGENT TURN: A single call to the LLM (one iteration in the agentic loop)
///   Multiple agent turns happen within one message turn when using tools
///
/// Inherits from HPD.Events.Event to participate in unified cross-domain event streaming.
/// Adapters convert these to protocol-specific formats as needed.
/// </summary>
public abstract record AgentEvent : HPD.Events.Event
{
    /// <summary>
    /// Context about which agent emitted this event (optional for backwards compatibility).
    /// Automatically attached by EventCoordinator.Emit() if not already set.
    /// </summary>
    public AgentExecutionContext? ExecutionContext { get; init; }

    /// <summary>
    /// OpenTelemetry-compatible trace ID (128-bit, 32 hex chars).
    /// Shared across all events in a single message turn execution.
    /// Set by the agent core at turn start and propagated to all subsequent events.
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// OpenTelemetry-compatible span ID (64-bit, 16 hex chars) for this event.
    /// Allows observers to build a parent-child span tree from the event stream.
    /// </summary>
    public string? SpanId { get; init; }

    /// <summary>
    /// Span ID of the parent span, linking this event into the trace hierarchy.
    /// Null for root-level events (MessageTurnStartedEvent).
    /// </summary>
    public string? ParentSpanId { get; init; }
}

#region Message Turn Events (Entire User Interaction)

/// <summary>
/// Emitted when a message turn starts (user sends message, agent begins processing)
/// This represents the START of the entire multi-step agent execution.
/// </summary>
public record MessageTurnStartedEvent(
    string MessageTurnId,
    string ConversationId,
    string AgentName) : AgentEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a message turn completes successfully
/// This represents the END of the entire agent execution for this user message.
/// </summary>
public record MessageTurnFinishedEvent(
    string MessageTurnId,
    string ConversationId,
    string AgentName,
    TimeSpan Duration,
    UsageDetails? Usage = null) : AgentEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted when an error occurs during message turn execution.
/// Error category is lazily computed from the exception using GenericErrorHandler.
/// </summary>
public record MessageTurnErrorEvent(
    string Message,
    [property: System.Text.Json.Serialization.JsonIgnore] Exception? Exception = null) : AgentEvent, IErrorEvent
{
    /// <inheritdoc />
    string IErrorEvent.ErrorMessage => Message;

    // Lazy-computed error details from the exception
    private ErrorHandling.ProviderErrorDetails? _errorDetails;
    private bool _errorDetailsParsed;

    private ErrorHandling.ProviderErrorDetails? GetErrorDetails()
    {
        if (!_errorDetailsParsed)
        {
            _errorDetailsParsed = true;
            if (Exception != null)
            {
                var handler = new ErrorHandling.GenericErrorHandler();
                _errorDetails = handler.ParseError(Exception);
            }
        }
        return _errorDetails;
    }

    /// <summary>
    /// Error category lazily computed from the exception.
    /// Uses GenericErrorHandler to classify the error.
    /// </summary>
    public ErrorHandling.ErrorCategory? Category => GetErrorDetails()?.Category;

    /// <summary>
    /// Error code from the provider, if available.
    /// </summary>
    public string? ErrorCode => GetErrorDetails()?.ErrorCode;

    /// <summary>
    /// Whether this is a model not found error.
    /// </summary>
    public bool IsModelNotFound => Category == ErrorHandling.ErrorCategory.ModelNotFound;

    /// <summary>
    /// Whether this error is retryable.
    /// </summary>
    public bool IsRetryable => Category is
        ErrorHandling.ErrorCategory.RateLimitRetryable or
        ErrorHandling.ErrorCategory.ServerError or
        ErrorHandling.ErrorCategory.Transient;
}

#endregion

#region Agent Turn Events (Single LLM Call Within Message Turn)

/// <summary>
/// Emitted when an agent turn starts (single LLM call within the agentic loop)
/// An agent turn represents one iteration where the LLM processes messages and responds.
/// Multiple agent turns may occur in one message turn when tools are called.
/// </summary>
public record AgentTurnStartedEvent(int Iteration) : AgentEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted when an agent turn completes
/// </summary>
public record AgentTurnFinishedEvent(int Iteration) : AgentEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted during agent execution to expose internal state for testing/debugging.
/// NOT intended for production use - only for characterization tests and debugging.
/// </summary>
public record StateSnapshotEvent(
    int CurrentIteration,
    int MaxIterations,
    bool IsTerminated,
    string? TerminationReason,
    int ConsecutiveErrorCount,
    List<string> CompletedFunctions,
    string AgentName) : AgentEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

#endregion

#region Content Events (Within an Agent Turn)

/// <summary>
/// Emitted when the agent starts producing text content
/// </summary>
public record TextMessageStartEvent(string MessageId, string Role) : AgentEvent;

/// <summary>
/// Emitted when the agent produces text content (streaming delta)
/// </summary>
public record TextDeltaEvent(string Text, string MessageId) : AgentEvent;

/// <summary>
/// Emitted when the agent finishes producing text content
/// </summary>
public record TextMessageEndEvent(string MessageId) : AgentEvent;


#endregion

#region Reasoning Events (For reasoning-capable models like o1, DeepSeek-R1)

/// <summary>
/// Emitted when the agent starts producing reasoning content.
/// Reasoning is extended thinking used by models like o1, DeepSeek-R1.
/// </summary>
public record ReasoningMessageStartEvent(string MessageId, string Role) : AgentEvent;

/// <summary>
/// Emitted when the agent produces reasoning content (streaming delta).
/// </summary>
public record ReasoningDeltaEvent(string Text, string MessageId) : AgentEvent;

/// <summary>
/// Emitted when the agent finishes producing reasoning content.
/// </summary>
public record ReasoningMessageEndEvent(string MessageId) : AgentEvent;

#endregion

#region Tool Events

/// <summary>
/// Indicates the kind of capability behind a tool call.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ToolCallType>))]
public enum ToolCallType
{
    /// <summary>A standard [AIFunction] method.</summary>
    Function,
    /// <summary>A [Skill] container that expands to constituent functions.</summary>
    Skill,
    /// <summary>A [SubAgent] that delegates to another agent.</summary>
    SubAgent,
    /// <summary>A [MultiAgent] workflow that orchestrates multiple agents.</summary>
    MultiAgent,
    /// <summary>A tool exposed by an [MCPServer].</summary>
    MCPServer,
    /// <summary>A function generated from an [OpenApi] spec.</summary>
    OpenApi,
}

/// <summary>
/// Emitted when the agent requests a tool call
/// </summary>
public record ToolCallStartEvent(
    string CallId,
    string Name,
    string MessageId,
    string? ToolkitName = null,
    ToolCallType? CallType = null) : AgentEvent;

/// <summary>
/// Emitted when a tool call's arguments are fully available
/// </summary>
public record ToolCallArgsEvent(string CallId, string ArgsJson) : AgentEvent;

/// <summary>
/// Emitted when a tool call completes execution
/// </summary>
public record ToolCallEndEvent(string CallId) : AgentEvent;

/// <summary>
/// Emitted when a tool call result is available
/// </summary>
public record ToolCallResultEvent(
    string CallId,
    string Result,
    string? ToolkitName = null,
    ToolCallType? CallType = null) : AgentEvent;

#endregion

#region Middleware Events

/// <summary>
/// Marker interface for agent-specific bidirectional events.
/// Inherits from HPD.Events.IBidirectionalEvent for cross-domain consistency.
/// Events implementing this interface can:
/// - Be emitted during execution
/// - Bubble to parent agents via AsyncLocal
/// - Wait for responses using WaitForResponseAsync
/// </summary>
public interface IBidirectionalAgentEvent : HPD.Events.IBidirectionalEvent
{
    // Inherits RequestId and SourceName from HPD.Events.IBidirectionalEvent
}

/// <summary>
/// Marker interface for permission-related Middleware events.
/// Permission events are a specialized subset of Middleware events
/// that require user interaction and approval workflows.
/// </summary>
/// <remarks>
/// Implements IBidirectionalAgentEvent.RequestId via PermissionId.
/// Each implementing record must provide: string IBidirectionalAgentEvent.RequestId => PermissionId;
/// </remarks>
public interface IPermissionEvent : IBidirectionalAgentEvent
{
    /// <summary>
    /// Unique identifier for this permission interaction.
    /// Used to correlate requests and responses.
    /// Maps to IBidirectionalAgentEvent.RequestId for consistency.
    /// </summary>
    string PermissionId { get; }
}

/// <summary>
/// Middleware requests permission to execute a function.
/// Handler should prompt user and send PermissionResponseEvent.
/// </summary>
public record PermissionRequestEvent(
    string PermissionId,
    string SourceName,
    string FunctionName,
    string? Description,
    string CallId,
    IDictionary<string, object?>? Arguments) : AgentEvent, IPermissionEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    /// <summary>Explicit interface implementation - maps PermissionId to RequestId</summary>
    string HPD.Events.IBidirectionalEvent.RequestId => PermissionId;
}

/// <summary>
/// Response to permission request.
/// Sent by external handler back to waiting Middleware.
/// </summary>
public record PermissionResponseEvent(
    string PermissionId,
    string SourceName,
    bool Approved,
    string? Reason = null,
    PermissionChoice Choice = PermissionChoice.Ask) : AgentEvent, IPermissionEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    /// <summary>Explicit interface implementation - maps PermissionId to RequestId</summary>
    string HPD.Events.IBidirectionalEvent.RequestId => PermissionId;
}

/// <summary>
/// Emitted after permission is approved (for observability).
/// </summary>
public record PermissionApprovedEvent(
    string PermissionId,
    string SourceName) : AgentEvent, IPermissionEvent
{
    /// <summary>Explicit interface implementation - maps PermissionId to RequestId</summary>
    string HPD.Events.IBidirectionalEvent.RequestId => PermissionId;
}

/// <summary>
/// Emitted after permission is denied (for observability).
/// </summary>
public record PermissionDeniedEvent(
    string PermissionId,
    string SourceName,
    string CallId,
    string Reason) : AgentEvent, IPermissionEvent
{
    /// <summary>Explicit interface implementation - maps PermissionId to RequestId</summary>
    string HPD.Events.IBidirectionalEvent.RequestId => PermissionId;
}

/// <summary>
/// Middleware requests permission to continue beyond max iterations.
/// </summary>
public record ContinuationRequestEvent(
    string ContinuationId,
    string SourceName,
    int CurrentIteration,
    int MaxIterations) : AgentEvent, IPermissionEvent
{
    /// <summary>
    /// Explicit interface implementation for IPermissionEvent.PermissionId
    /// Maps ContinuationId to PermissionId for consistency.
    /// </summary>
    string IPermissionEvent.PermissionId => ContinuationId;

    /// <summary>Explicit interface implementation - maps ContinuationId to RequestId</summary>
    string HPD.Events.IBidirectionalEvent.RequestId => ContinuationId;
}

/// <summary>
/// Response to continuation request.
/// </summary>
public record ContinuationResponseEvent(
    string ContinuationId,
    string SourceName,
    bool Approved,
    int ExtensionAmount = 0) : AgentEvent, IPermissionEvent
{
    /// <summary>
    /// Explicit interface implementation for IPermissionEvent.PermissionId
    /// Maps ContinuationId to PermissionId for consistency.
    /// </summary>
    string IPermissionEvent.PermissionId => ContinuationId;

    /// <summary>Explicit interface implementation - maps ContinuationId to RequestId</summary>
    string HPD.Events.IBidirectionalEvent.RequestId => ContinuationId;
}

/// <summary>
/// Marker interface for clarification-related events.
/// Clarification events enable agents/Toolkits to ask the user for additional information
/// during execution, supporting human-in-the-loop workflows beyond just permissions.
/// </summary>
public interface IClarificationEvent : IBidirectionalAgentEvent
{
    /// <summary>
    /// Unique identifier for this clarification interaction.
    /// Used to correlate requests and responses.
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// The question being asked to the user.
    /// </summary>
    string Question { get; }
}

/// <summary>
/// Agent/Toolkit requests user clarification or additional input.
/// Handler should prompt user and send ClarificationResponseEvent.
/// </summary>
public record ClarificationRequestEvent(
    string RequestId,
    string SourceName,
    string Question,
    string? AgentName = null,
    string[]? Options = null) : AgentEvent, IClarificationEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

/// <summary>
/// Response to clarification request.
/// Sent by external handler back to waiting agent/Toolkit.
/// </summary>
public record ClarificationResponseEvent(
    string RequestId,
    string SourceName,
    string Question,
    string Answer) : AgentEvent, IClarificationEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

/// <summary>
/// Middleware reports an error (one-way, no response needed).
/// This is NOT a bidirectional event - it's just informational.
/// </summary>
public record MiddlewareErrorEvent(
    string SourceName,
    string ErrorMessage) : AgentEvent, IErrorEvent
{
    /// <summary>
    /// The underlying exception. Not serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Exception? Exception { get; init; }
}

#endregion

#region Observability Events (Internal diagnostics)

/// <summary>
/// Marker interface to distinguish observability events from protocol events.
/// Observability events are designed for logging, metrics, and monitoring.
/// They are processed by IAgentEventObserver implementations.
/// </summary>
public interface IObservabilityEvent { }

/// <summary>
/// Interface for events that represent errors or error conditions.
/// Provides a unified way to identify and handle error events across the system.
/// </summary>
/// <remarks>
/// Consumers can subscribe to all error events by filtering on this interface:
/// <code>
/// if (evt is IErrorEvent error)
/// {
///     logger.LogError(error.Exception, "{Message}", error.ErrorMessage);
/// }
/// </code>
/// </remarks>
public interface IErrorEvent
{
    /// <summary>
    /// Human-readable error message describing what went wrong.
    /// </summary>
    string ErrorMessage { get; }

    /// <summary>
    /// The underlying exception, if available. Not serialized — Exception is not STJ source-gen compatible.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    Exception? Exception { get; }
}

/// <summary>
/// Emitted when Collapsed tools visibility is determined for an iteration.
/// Contains full snapshot of what tools the LLM can see.
/// </summary>
public record CollapsedToolsVisibleEvent(
    string AgentName,
    int Iteration,
    IReadOnlyList<string> VisibleToolNames,
    ImmutableHashSet<string> ExpandedToolkits,
    ImmutableHashSet<string> ExpandedSkills,
    int TotalToolCount,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when a Toolkit or skill container is expanded.
/// </summary>
public record ContainerExpandedEvent(
    string ContainerName,
    ContainerType Type,
    IReadOnlyList<string> UnlockedFunctions,
    int Iteration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

public enum ContainerType { Toolkit, Skill }


/// <summary>
/// Emitted when a permission check occurs.
/// </summary>
public record PermissionCheckEvent(
    string FunctionName,
    bool IsApproved,
    string? DenialReason,
    string AgentName,
    int Iteration,
    TimeSpan Duration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when an iteration starts with full state snapshot.
/// </summary>
public record IterationStartEvent(
    string AgentName,
    int Iteration,
    int MaxIterations,
    int CurrentMessageCount,
    int HistoryMessageCount,
    int TurnHistoryMessageCount,
    int CompletedFunctionsCount
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when circuit breaker is triggered.
/// </summary>
public record CircuitBreakerTriggeredEvent(
    string AgentName,
    string FunctionName,
    int ConsecutiveCount,
    int Iteration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when history reduction cache is checked.
/// </summary>
public record HistoryReductionCacheEvent(
    string AgentName,
    bool IsHit,
    DateTime? ReductionCreatedAt,
    int? SummarizedUpToIndex,
    int CurrentMessageCount,
    int? TokenSavings,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when an asset is successfully uploaded to AssetStore.
/// Provides observability for binary asset storage operations.
/// </summary>
public record AssetUploadedEvent(
    string AssetId,
    string MediaType,
    int SizeBytes
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when an asset upload fails.
/// Provides observability for asset storage failures.
/// </summary>
public record AssetUploadFailedEvent(
    string MediaType,
    string Error
) : AgentEvent, IObservabilityEvent, IErrorEvent
{
    /// <summary>
    /// Human-readable error message describing what went wrong.
    /// </summary>
    string IErrorEvent.ErrorMessage => Error;

    /// <summary>
    /// The underlying exception, if available.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    Exception? IErrorEvent.Exception => null;
}

/// <summary>
/// Checkpoint operation type.
/// </summary>
public enum CheckpointOperation
{
    Saved,
    Restored,
    Cleared
}

/// <summary>
/// Emitted for all checkpoint-related operations (save, restore, pending writes).
/// </summary>
public record CheckpointEvent(
    CheckpointOperation Operation,
    string SessionId,
    DateTimeOffset Timestamp,
    TimeSpan? Duration = null,
    int? Iteration = null,
    int? WriteCount = null,
    int? SizeBytes = null,
    int? MessageCount = null,
    bool? Success = null,
    string? ErrorMessage = null
) : AgentEvent, IObservabilityEvent;

#region Background Operation Events

/// <summary>
/// Emitted when an LLM operation has been backgrounded by the provider.
/// Contains the continuation token needed for polling for completion.
/// </summary>
/// <remarks>
/// This event is emitted when AllowBackgroundResponses is true and the provider
/// supports background mode. The client should use the ContinuationToken to poll
/// for the operation's completion.
/// </remarks>
public record BackgroundOperationStartedEvent(
    ResponseContinuationToken ContinuationToken,
    OperationStatus Status,
    string? OperationId = null
) : AgentEvent;

/// <summary>
/// Emitted during polling with status updates for a background operation.
/// </summary>
public record BackgroundOperationStatusEvent(
    ResponseContinuationToken ContinuationToken,
    OperationStatus Status,
    string? StatusMessage = null
) : AgentEvent;

#endregion

/// <summary>
/// Emitted when parallel tool execution starts.
/// </summary>
public record InternalParallelToolExecutionEvent(
    string AgentName,
    int Iteration,
    int ToolCount,
    int ParallelBatchSize,
    int ApprovedCount,
    int DeniedCount,
    TimeSpan Duration,
    TimeSpan? SemaphoreWaitDuration,
    bool IsParallel,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Retry status for function execution.
/// </summary>
public enum RetryStatus
{
    /// <summary>Retry attempt in progress</summary>
    Attempting,
    /// <summary>All retry attempts exhausted</summary>
    Exhausted
}

/// <summary>
/// Emitted for all retry-related events during function execution.
/// </summary>
public record InternalRetryEvent(
    RetryStatus Status,
    string AgentName,
    string FunctionName,
    int AttemptNumber,
    int MaxRetries,
    DateTimeOffset Timestamp,
    string? ErrorMessage = null,
    TimeSpan? RetryDelay = null
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when a function execution is being retried due to an error.
/// Emitted by FunctionRetryMiddleware for observability.
/// Error category is lazily computed from the exception.
/// </summary>
/// <param name="FunctionName">The name of the function being retried</param>
/// <param name="Attempt">The current retry attempt number (1-based)</param>
/// <param name="MaxRetries">Maximum number of retries allowed</param>
/// <param name="Delay">Time to wait before retrying</param>
/// <param name="Exception">The exception that caused the retry</param>
/// <param name="ExceptionType">The type name of the exception</param>
/// <param name="ErrorMessage">The error message from the exception</param>
public record FunctionRetryEvent(
    string FunctionName,
    int Attempt,
    int MaxRetries,
    TimeSpan Delay,
    string ExceptionType,
    string ErrorMessage
) : AgentEvent, IObservabilityEvent, IErrorEvent
{
    /// <summary>
    /// The exception that caused the retry. Not serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Exception? Exception { get; init; }

    /// <inheritdoc />
    Exception? IErrorEvent.Exception => Exception;

    // Lazy-computed error details
    private ErrorHandling.ProviderErrorDetails? _errorDetails;
    private bool _errorDetailsParsed;

    private ErrorHandling.ProviderErrorDetails? GetErrorDetails()
    {
        if (!_errorDetailsParsed)
        {
            _errorDetailsParsed = true;
            var handler = new ErrorHandling.GenericErrorHandler();
            _errorDetails = handler.ParseError(Exception);
        }
        return _errorDetails;
    }

    /// <summary>
    /// Error category lazily computed from the exception.
    /// </summary>
    public ErrorHandling.ErrorCategory? Category => GetErrorDetails()?.Category;

    /// <summary>
    /// Whether this is a model not found error.
    /// </summary>
    public bool IsModelNotFound => Category == ErrorHandling.ErrorCategory.ModelNotFound;

    /// <summary>
    /// Whether this error is retryable.
    /// </summary>
    public bool IsRetryable => Category is
        ErrorHandling.ErrorCategory.RateLimitRetryable or
        ErrorHandling.ErrorCategory.ServerError or
        ErrorHandling.ErrorCategory.Transient;
}

/// <summary>
/// Emitted when a model call (LLM streaming) is being retried due to an error.
/// Signals to consumers (like UI) that partial content should be discarded.
/// Emitted by RetryMiddleware for observability and progressive streaming support.
/// Error category is lazily computed from the exception.
/// </summary>
/// <remarks>
/// <para><b>Progressive Streaming Pattern:</b></para>
/// <para>
/// This event follows the Gemini CLI pattern for handling streaming retries.
/// When consumers receive this event, they should:
/// </para>
/// <list type="bullet">
/// <item>Clear any partial response text displayed to the user</item>
/// <item>Show a retry indicator (optional)</item>
/// <item>Prepare to receive fresh content from the retry attempt</item>
/// </list>
/// <para>
/// Unlike buffered retry where users see nothing until success, this pattern
/// allows users to see partial responses immediately, then a brief retry indicator,
/// followed by the successful response. This provides better UX than a frozen screen.
/// </para>
/// <para><b>Example (UI Handler):</b></para>
/// <code>
/// case ModelCallRetryEvent retry:
///     // Clear partial response buffer
///     responseBuffer.Clear();
///
///     // Optional: Show retry indicator
///     Console.WriteLine($"⟳ Retrying (attempt {retry.Attempt}/{retry.MaxRetries})...");
///     break;
/// </code>
/// </remarks>
/// <param name="Attempt">The current retry attempt number (1-based)</param>
/// <param name="MaxRetries">Maximum number of retries allowed</param>
/// <param name="Delay">Time to wait before retrying</param>
/// <param name="Exception">The exception that caused the retry</param>
/// <param name="ExceptionType">The type name of the exception</param>
/// <param name="ErrorMessage">The error message from the exception</param>
public record ModelCallRetryEvent(
    int Attempt,
    int MaxRetries,
    TimeSpan Delay,
    string ExceptionType,
    string ErrorMessage
) : AgentEvent, IObservabilityEvent, IErrorEvent
{
    /// <summary>
    /// The exception that caused the retry. Not serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Exception? Exception { get; init; }

    /// <inheritdoc />
    Exception? IErrorEvent.Exception => Exception;

    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    // Lazy-computed error details
    private ErrorHandling.ProviderErrorDetails? _errorDetails;
    private bool _errorDetailsParsed;

    private ErrorHandling.ProviderErrorDetails? GetErrorDetails()
    {
        if (!_errorDetailsParsed)
        {
            _errorDetailsParsed = true;
            var handler = new ErrorHandling.GenericErrorHandler();
            _errorDetails = handler.ParseError(Exception);
        }
        return _errorDetails;
    }

    /// <summary>
    /// Error category lazily computed from the exception.
    /// </summary>
    public ErrorHandling.ErrorCategory? Category => GetErrorDetails()?.Category;

    /// <summary>
    /// Whether this is a model not found error.
    /// </summary>
    public bool IsModelNotFound => Category == ErrorHandling.ErrorCategory.ModelNotFound;

    /// <summary>
    /// Whether this error is retryable according to the error category.
    /// </summary>
    public bool IsRetryable => Category is
        ErrorHandling.ErrorCategory.RateLimitRetryable or
        ErrorHandling.ErrorCategory.ServerError or
        ErrorHandling.ErrorCategory.Transient;
}

/// <summary>
/// Emitted when delta sending is activated.
/// </summary>
public record DeltaSendingActivatedEvent(
    string AgentName,
    int MessageCountSent,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when plan mode is activated.
/// </summary>
public record PlanModeActivatedEvent(
    string AgentName,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

#region Plan Lifecycle Events

/// <summary>
/// Type of plan update operation.
/// </summary>
public enum PlanUpdateType
{
    /// <summary>Plan was created with initial goal and steps</summary>
    Created,

    /// <summary>A step's status was updated</summary>
    StepUpdated,

    /// <summary>A new step was added to the plan</summary>
    StepAdded,

    /// <summary>A context note was added</summary>
    NoteAdded,

    /// <summary>The entire plan was marked as complete</summary>
    Completed
}

/// <summary>
/// Consolidated plan update event following the Codex pattern.
/// Emitted whenever a plan is created or modified, containing the full plan state.
/// </summary>
/// <remarks>
/// <para><b>Design Rationale:</b></para>
/// <para>
/// This follows the Codex pattern of emitting a single event type with full plan state,
/// rather than multiple granular events. Benefits:
/// - Simpler for consumers (one event handler)
/// - Always includes complete context (no partial state)
/// - Matches industry patterns (Codex, )
/// - Reduces serialization registrations
/// </para>
/// <para>
/// The UpdateType discriminator allows consumers to react to specific changes while
/// always having access to the complete plan state for UI synchronization.
/// </para>
/// <para><b>Plan Property:</b></para>
/// <para>
/// The Plan property is of type object to avoid circular dependencies between HPD-Agent
/// and HPD-Agent.Memory assemblies. At runtime, this will be an AgentPlanData instance.
/// Consumers can cast it to the appropriate type.
/// </para>
/// </remarks>
public record PlanUpdatedEvent(
    string PlanId,
    string ConversationId,
    PlanUpdateType UpdateType,
    object Plan,
    string? Explanation,
    DateTimeOffset UpdatedAt
) : AgentEvent, IObservabilityEvent;

#endregion

/// <summary>
/// Emitted when a nested agent is invoked.
/// </summary>
public record NestedAgentInvokedEvent(
    string OrchestratorName,
    string ChildAgentName,
    int NestingDepth,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when document processing occurs.
/// </summary>
public record DocumentProcessedEvent(
    string AgentName,
    string DocumentPath,
    long SizeBytes,
    TimeSpan Duration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when message preparation completes.
/// </summary>
public record InternalMessagePreparedEvent(
    string AgentName,
    int Iteration,
    int FinalMessageCount,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when a bidirectional event is processed.
/// </summary>
public record BidirectionalEventProcessedEvent(
    string AgentName,
    string EventType,
    bool RequiresResponse,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when agent makes a decision.
/// </summary>
public record AgentDecisionEvent(
    string AgentName,
    string DecisionType,
    int Iteration,
    int ConsecutiveFailures,
    int CompletedFunctionsCount
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when agent completes successfully.
/// </summary>
public record AgentCompletionEvent(
    string AgentName,
    int TotalIterations,
    TimeSpan Duration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when iteration messages are logged.
/// </summary>
public record IterationMessagesEvent(
    string AgentName,
    int Iteration,
    int MessageCount,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when middleware schema changes are detected during checkpoint restoration.
/// Used for monitoring, alerting, and audit trails.
/// </summary>
public record SchemaChangedEvent(
    string? OldSignature,
    string NewSignature,
    IReadOnlyList<string> RemovedTypes,
    IReadOnlyList<string> AddedTypes,
    bool IsUpgrade
) : AgentEvent, IObservabilityEvent
{
    public SchemaChangedEvent(
        string? oldSignature,
        string newSignature)
        : this(
            OldSignature: oldSignature,
            NewSignature: newSignature,
            RemovedTypes: Array.Empty<string>(),
            AddedTypes: Array.Empty<string>(),
            IsUpgrade: oldSignature == null)
    {
    }
}

/// <summary>
/// Emitted by ToolCollapsingMiddleware at iteration start to report Collapsing state.
/// Tracks how many Toolkits and skills have been expanded.
/// </summary>
public record CollapsingStateEvent(
    string AgentName,
    int Iteration,
    int ExpandedToolkitsCount,
    int ExpandedSkillsCount,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent;




#endregion

#region Structured Output Events

/// <summary>
/// A structured output result containing a parsed (partial or complete) value.
/// Emitted during RunStructuredAsync&lt;T&gt;() streaming.
/// </summary>
/// <typeparam name="T">The output type</typeparam>
/// <param name="Value">The parsed value (partial or complete)</param>
/// <param name="IsPartial">True if this is an intermediate result, false if final</param>
/// <param name="RawJson">The raw JSON string that was parsed</param>
public sealed record StructuredResultEvent<T>(
    T Value,
    bool IsPartial,
    string RawJson
) : AgentEvent where T : class;

/// <summary>
/// Emitted when structured output parsing fails on final validation.
/// Partial parse failures are silently skipped; this is only for final failures.
/// </summary>
/// <param name="RawJson">The JSON that failed to parse</param>
/// <param name="ErrorMessage">Description of the error</param>
/// <param name="ExpectedTypeName">The type we attempted to deserialize to</param>
/// <param name="Exception">The underlying exception (if any)</param>
public sealed record StructuredOutputErrorEvent(
    string RawJson,
    string ErrorMessage,
    string ExpectedTypeName,
    Exception? Exception = null
) : AgentEvent, IErrorEvent;

#region Structured Output Observability Events

/// <summary>
/// Emitted when structured output processing starts.
/// Provides observability into structured output operations.
/// </summary>
/// <param name="MessageId">Unique identifier for this structured output operation</param>
/// <param name="OutputTypeName">The name of the output type (e.g., "WeatherReport")</param>
/// <param name="OutputMode">The output mode: "native" or "tool"</param>
public sealed record StructuredOutputStartEvent(
    string MessageId,
    string OutputTypeName,
    string OutputMode
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when a partial structured output is successfully parsed.
/// Used for monitoring streaming partial parse performance.
/// </summary>
/// <param name="MessageId">Unique identifier for this structured output operation</param>
/// <param name="OutputTypeName">The name of the output type</param>
/// <param name="ParseAttempt">The number of parse attempts so far</param>
/// <param name="AccumulatedJsonLength">Current length of accumulated JSON</param>
public sealed record StructuredOutputPartialEvent(
    string MessageId,
    string OutputTypeName,
    int ParseAttempt,
    int AccumulatedJsonLength
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when structured output processing completes successfully.
/// Provides performance metrics for monitoring.
/// </summary>
/// <param name="MessageId">Unique identifier for this structured output operation</param>
/// <param name="OutputTypeName">The name of the output type</param>
/// <param name="TotalParseAttempts">Total number of partial parse attempts</param>
/// <param name="FinalJsonLength">Length of the final JSON</param>
/// <param name="Duration">Total duration of structured output processing</param>
public sealed record StructuredOutputCompleteEvent(
    string MessageId,
    string OutputTypeName,
    int TotalParseAttempts,
    int FinalJsonLength,
    TimeSpan Duration
) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Emitted when an event is dropped due to stream interruption.
/// Provides observability into dropped events.
/// </summary>
public record EventDroppedEvent(
    string DroppedStreamId,
    string DroppedEventType,
    long DroppedSequenceNumber) : AgentEvent, IObservabilityEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

#endregion

#endregion

/// <summary>
/// Abstraction for bidirectional event coordination.
/// Enables middlewares to emit events and wait for responses
/// without knowing about Agent internals.
/// </summary>
/// <remarks>
/// <para>
/// This interface decouples middleware from Agent, enabling:
/// - Clean middleware architecture (no agent reference needed)
/// - Easy unit testing (mock the interface)
/// - Future implementations (e.g., distributed event coordination)
/// </para>
/// <para>
/// <b>Threading:</b> All methods must be thread-safe. Multiple middlewares
/// can emit events concurrently.
/// </para>
/// <para>

