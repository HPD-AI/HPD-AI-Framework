using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;
using EventChannel = HPD.Events.EventChannel;
using EventDirection = HPD.Events.EventDirection;

namespace HPD.Agent;
/// <summary>
/// Provides hierarchical metadata about which agent emitted an event.
/// Enables event attribution and filtering in multi-agent systems.
/// </summary>
public record AgentMetadata
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




#region Channel and Direction Enums
// EventChannel and EventDirection enums live in HPD.Events.
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
public record InterruptionRequestEvent : AgentInputEvent
{
    public InterruptionRequestEvent(
        string? eventFlowId,
        string Reason,
        InterruptionSource Source)
    {
        EventFlowId = eventFlowId;
        this.Reason = Reason;
        this.Source = Source;
    }

    public string Reason { get; init; }
    public InterruptionSource Source { get; init; }
    public string? EventFlowId { get; init; }
}

/// <summary>
/// Emitted after an interruption request has been applied to active streams or turns.
/// </summary>
public sealed record InterruptionHandledEvent : AgentEvent
{
    public InterruptionHandledEvent(
        string? eventFlowId,
        string reason,
        InterruptionSource source)
    {
        EventFlowId = eventFlowId;
        Reason = reason;
        Source = source;
        CanInterrupt = false;
    }

    public string Reason { get; init; }
    public InterruptionSource Source { get; init; }

    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
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
[JsonConverter(typeof(AgentEventJsonConverter))]
public abstract record AgentEvent : HPD.Events.Event
{
    /// <summary>
    /// Stable ID assigned when the event is persisted into a thread history.
    /// </summary>
    public string? EventId { get; init; }

    /// <summary>
    /// Durable session scope when this event is persisted or replayed from a thread.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Durable thread scope when this event is persisted or replayed from a thread.
    /// </summary>
    public string? ThreadId { get; init; }

    /// <summary>
    /// Live metadata about which agent emitted this event.
    /// This is omitted from durable thread event JSON by default because thread ownership
    /// and durable attribution live on thread metadata.
    /// </summary>
    public AgentMetadata? Metadata { get; init; }

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

    /// <summary>
    /// Whether this event type should be recorded into durable thread history.
    /// This is event type policy, not serialized event payload.
    /// </summary>
    public virtual bool ShouldPersistToThread() => false;

    /// <summary>
    /// Optional content-store persistence policy for this event type.
    /// This is event type policy, not serialized event payload.
    /// </summary>
    public virtual ContentPersistenceRequest? GetContentPersistenceRequest() => null;
}

/// <summary>
/// Base type for first-class user input events.
/// </summary>
public abstract record AgentInputEvent
{
    /// <summary>Session scope for the input event.</summary>
    public string? SessionId { get; init; }

    /// <summary>Thread scope for the input event. Defaults to the agent's thread resolution behavior when null.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Optional target agent identifier for hosted or multi-agent runtimes.</summary>
    public string? AgentId { get; init; }

    /// <summary>Per-run configuration carried with the input event.</summary>
    public AgentRunConfig? RunConfig { get; init; }

    /// <summary>Runtime-owned run identifier assigned by hosting layers that track active work.</summary>
    public string? RuntimeRunId { get; init; }
}

/// <summary>
/// Emitted when hosting accepts input into a runtime-owned thread run.
/// </summary>
public sealed record ThreadRunStartedEvent(
    string RuntimeRunId,
    string AgentId,
    DateTimeOffset StartedAt) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted by the runtime when a submitted input has left the active execution slot.
/// </summary>
public sealed record ThreadRunCompletedEvent(
    string RuntimeRunId,
    string AgentId,
    bool Cancelled,
    string? ErrorType = null,
    string? ErrorMessage = null) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// User text input sent into an agent turn.
/// </summary>
public sealed record UserTextInputEvent(string Text) : AgentInputEvent
{
}

/// <summary>
/// Process-local user message input sent into an agent turn.
/// </summary>
public sealed record UserMessagesInputEvent(
    IReadOnlyList<ChatMessage> Messages) : AgentInputEvent
{
    /// <summary>Process-local session scope for in-memory integrations.</summary>
    [JsonIgnore]
    public Session? Session { get; init; }

    /// <summary>Process-local thread scope for in-memory integrations.</summary>
    [JsonIgnore]
    public Thread? Thread { get; init; }
}

#region Message Turn Events (Entire User Interaction)

/// <summary>
/// Emitted when a message turn starts (user sends message, agent begins processing)
/// This represents the START of the entire multi-step agent execution.
/// </summary>
public record MessageTurnStartedEvent : AgentEvent
{
    [JsonConstructor]
    public MessageTurnStartedEvent(
        string MessageTurnId,
        string ConversationId,
        string AgentId,
        string AgentName)
    {
        this.MessageTurnId = MessageTurnId;
        this.ConversationId = ConversationId;
        this.AgentId = AgentId;
        this.AgentName = AgentName;
    }

    public MessageTurnStartedEvent(
        string MessageTurnId,
        string ConversationId,
        string AgentName)
        : this(MessageTurnId, ConversationId, AgentName, AgentName)
    {
    }

    public string MessageTurnId { get; init; }
    public string ConversationId { get; init; }
    public string AgentId { get; init; }
    public string AgentName { get; init; }

    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;

    public int? InputMessageCount { get; init; }
    public bool? IsResume { get; init; }
}

/// <summary>
/// Emitted when a message turn completes successfully
/// This represents the END of the entire agent execution for this user message.
/// </summary>
public record MessageTurnFinishedEvent : AgentEvent
{
    [JsonConstructor]
    public MessageTurnFinishedEvent(
        string MessageTurnId,
        string ConversationId,
        string AgentId,
        string AgentName,
        TimeSpan Duration,
        UsageDetails? Usage = null)
    {
        this.MessageTurnId = MessageTurnId;
        this.ConversationId = ConversationId;
        this.AgentId = AgentId;
        this.AgentName = AgentName;
        this.Duration = Duration;
        this.Usage = Usage;
    }

    public MessageTurnFinishedEvent(
        string MessageTurnId,
        string ConversationId,
        string AgentName,
        TimeSpan Duration,
        UsageDetails? Usage = null)
        : this(MessageTurnId, ConversationId, AgentName, AgentName, Duration, Usage)
    {
    }

    public string MessageTurnId { get; init; }
    public string ConversationId { get; init; }
    public string AgentId { get; init; }
    public string AgentName { get; init; }
    public TimeSpan Duration { get; init; }
    public UsageDetails? Usage { get; init; }

    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;

    public int? Iteration { get; init; }
    public string? TerminationReason { get; init; }
    public int? TurnMessageCount { get; init; }
}

/// <summary>
/// Emitted when an error occurs during message turn execution.
/// Error category is lazily computed from the exception using GenericErrorHandler.
/// </summary>
public record MessageTurnErrorEvent(
    string Message,
    [property: System.Text.Json.Serialization.JsonIgnore] Exception? Exception = null) : AgentEvent, IErrorEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    public string? MessageTurnId { get; init; }
    public string? ConversationId { get; init; }
    public string? AgentId { get; init; }
    public string? AgentName { get; init; }
    public string? ErrorType { get; init; }

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
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted when an agent turn completes
/// </summary>
public record AgentTurnFinishedEvent(int Iteration) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
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
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

#endregion

#region Content Events (Within an Agent Turn)

/// <summary>
/// Emitted when the agent starts producing text content
/// </summary>
public record TextMessageStartEvent(string MessageId, string Role) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted when the agent produces text content (streaming delta)
/// </summary>
public record TextDeltaEvent(string Text, string MessageId) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted when the agent finishes producing text content
/// </summary>
public record TextMessageEndEvent(string MessageId) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted when a realtime provider produces a user input transcript update.
/// </summary>
public sealed record UserAudioTranscriptDeltaEvent(
    string Text,
    string MessageId,
    string? ProviderItemId = null,
    int? ContentIndex = null) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>
/// Emitted when a realtime provider finalizes a user input transcript.
/// </summary>
public sealed record UserAudioTranscriptCompletedEvent(
    string Text,
    string MessageId,
    string? ProviderItemId = null,
    int? ContentIndex = null) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>
/// Emitted when realtime user input transcription fails.
/// </summary>
public sealed record UserAudioTranscriptFailedEvent(
    string MessageId,
    string Error,
    string? ProviderItemId = null,
    int? ContentIndex = null) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}


#endregion

#region Reasoning Events (For reasoning-capable models like o1, DeepSeek-R1)

/// <summary>
/// Emitted when the agent starts producing reasoning content.
/// Reasoning is extended thinking used by models like o1, DeepSeek-R1.
/// </summary>
public record ReasoningMessageStartEvent(string MessageId, string Role) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted when the agent produces reasoning content (streaming delta).
/// </summary>
public record ReasoningDeltaEvent(string Text, string MessageId, string? ProtectedData = null) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted when the agent finishes producing reasoning content.
/// </summary>
public record ReasoningMessageEndEvent(string MessageId) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override bool ShouldPersistToThread() => true;
}

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
    string? ToolHarnessName = null,
    ToolCallType? CallType = null) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted when a tool call's arguments are fully available
/// </summary>
public record ToolCallArgsEvent(string CallId, string ArgsJson) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted when a tool call completes execution
/// </summary>
public record ToolCallEndEvent(string CallId) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted when a tool call result is available
/// </summary>
public record ToolCallResultEvent(
    string CallId,
    ToolResultPayload Result,
    string? ToolHarnessName = null,
    ToolCallType? CallType = null,
    string? Name = null) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;

    public string? MessageId { get; init; }
}

/// <summary>
/// Serializable event-facing projection of a tool result.
/// </summary>
public sealed record ToolResultPayload(
    string? Text = null,
    JsonElement? Json = null,
    IReadOnlyList<ClientTools.IToolResultContent>? Content = null,
    string? ResultType = null)
{
    internal static ToolResultPayload FromResult(object? result)
    {
        if (result is ToolResultPayload payload)
            return payload;

        if (result is null)
            return new ToolResultPayload(Json: JsonSerializer.SerializeToElement((object?)null, HPDJsonContext.Default.Object));

        var resultType = result.GetType().FullName;

        return result switch
        {
            string text => new ToolResultPayload(
                Text: text,
                Json: JsonSerializer.SerializeToElement(text, HPDJsonContext.Default.String),
                ResultType: resultType),

            JsonElement json => new ToolResultPayload(
                Text: json.GetRawText(),
                Json: json.Clone(),
                ResultType: resultType),

            ValidationErrorResponse validation => new ToolResultPayload(
                Text: JsonSerializer.Serialize(validation, HPDJsonContext.Default.ValidationErrorResponse),
                Json: JsonSerializer.SerializeToElement(validation, HPDJsonContext.Default.ValidationErrorResponse),
                ResultType: resultType),

            ClientTools.TextContent textContent => new ToolResultPayload(
                Text: textContent.Text,
                Json: JsonSerializer.SerializeToElement(textContent.Text, HPDJsonContext.Default.String),
                Content: [textContent],
                ResultType: resultType),

            ClientTools.JsonContent jsonContent => new ToolResultPayload(
                Text: jsonContent.Value.GetRawText(),
                Json: jsonContent.Value.Clone(),
                Content: [jsonContent],
                ResultType: resultType),

            ClientTools.IToolResultContent content => new ToolResultPayload(
                Text: ContentToText([content]),
                Content: [content],
                ResultType: resultType),

            IEnumerable<ClientTools.IToolResultContent> contents => FromContent(contents.ToArray(), resultType),

            _ => new ToolResultPayload(Text: result.ToString(), ResultType: resultType)
        };
    }

    private static ToolResultPayload FromContent(
        IReadOnlyList<ClientTools.IToolResultContent> content,
        string? resultType)
    {
        if (content.Count == 1)
        {
            if (content[0] is ClientTools.TextContent text)
            {
                return new ToolResultPayload(
                    Text: text.Text,
                    Json: JsonSerializer.SerializeToElement(text.Text, HPDJsonContext.Default.String),
                    Content: content,
                    ResultType: resultType);
            }

            if (content[0] is ClientTools.JsonContent json)
            {
                return new ToolResultPayload(
                    Text: json.Value.GetRawText(),
                    Json: json.Value.Clone(),
                    Content: content,
                    ResultType: resultType);
            }
        }

        return new ToolResultPayload(
            Text: ContentToText(content),
            Content: content,
            ResultType: resultType);
    }

    private static string? ContentToText(IReadOnlyList<ClientTools.IToolResultContent> content)
    {
        if (content.Count == 0)
            return null;

        if (content.Count == 1)
        {
            return content[0] switch
            {
                ClientTools.TextContent text => text.Text,
                ClientTools.JsonContent json => json.Value.GetRawText(),
                ClientTools.BinaryContent binary => binary.Filename ?? binary.Id ?? binary.Url ?? binary.MimeType,
                _ => null
            };
        }

        return JsonSerializer.Serialize(content, AgentEventJsonContext.Default.IReadOnlyListIToolResultContent);
    }
}

/// <summary>
/// Base event for runtime-owned background work started by a tool call.
/// </summary>
public abstract record ToolCallBackgroundTaskEvent : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;

    public required string TaskId { get; init; }

    public required string Name { get; init; }

    public required FunctionInvocationSnapshot Invocation { get; init; }
}

/// <summary>
/// Emitted when runtime-owned background work started by a tool call begins.
/// </summary>
public sealed record ToolCallBackgroundTaskStartedEvent : ToolCallBackgroundTaskEvent
{
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Emitted when runtime-owned background work started by a tool call completes.
/// </summary>
public sealed record ToolCallBackgroundTaskCompletedEvent : ToolCallBackgroundTaskEvent
{
    public required DateTimeOffset CompletedAt { get; init; }

    public required long DurationMilliseconds { get; init; }
}

/// <summary>
/// Emitted when runtime-owned background work started by a tool call observes runtime cancellation.
/// </summary>
public sealed record ToolCallBackgroundTaskCancelledEvent : ToolCallBackgroundTaskEvent
{
    public required DateTimeOffset CancelledAt { get; init; }
}

/// <summary>
/// Emitted when runtime-owned background work started by a tool call faults.
/// </summary>
public sealed record ToolCallBackgroundTaskFaultedEvent : ToolCallBackgroundTaskEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;

    public required DateTimeOffset FaultedAt { get; init; }

    public required string ExceptionType { get; init; }

    public required string ErrorMessage { get; init; }
}

#endregion

#region Middleware Events

public interface IAgentRequestEvent : HPD.Events.IRequestEvent;

public interface IAgentResponseEvent : HPD.Events.IResponseEvent;

public sealed record AgentRequestStartedEvent(
    string RequestId,
    string SourceName,
    string RequestEventType,
    string ExpectedResponseEventType,
    HPD.Events.ResponsePolicy ResponsePolicy,
    HPD.Events.ResponderTarget? Target,
    HPD.Events.RequestVisibility Visibility,
    DateTimeOffset StartedAt) : AgentEvent
{
    public override HPD.Events.EventChannel Channel { get; init; } = HPD.Events.EventChannel.Control;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;
}

public sealed record AgentRequestResolvedEvent(
    string RequestId,
    string SourceName,
    string RequestEventType,
    string ResponseEventType,
    string? ResponderId,
    string? ResponderGroup,
    DateTimeOffset ResolvedAt) : AgentEvent
{
    public override HPD.Events.EventChannel Channel { get; init; } = HPD.Events.EventChannel.Control;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;
}

public sealed record AgentRequestExpiredEvent(
    string RequestId,
    string SourceName,
    string RequestEventType,
    TimeSpan Timeout,
    DateTimeOffset ExpiredAt) : AgentEvent
{
    public override HPD.Events.EventChannel Channel { get; init; } = HPD.Events.EventChannel.Control;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;
}

public sealed record AgentRequestCancelledEvent(
    string RequestId,
    string SourceName,
    string RequestEventType,
    string? Reason,
    DateTimeOffset CancelledAt) : AgentEvent
{
    public override HPD.Events.EventChannel Channel { get; init; } = HPD.Events.EventChannel.Control;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;
}

public sealed record AgentResponseRejectedEvent(
    string RequestId,
    string ResponseEventType,
    HPD.Events.RespondStatus Status,
    string? Reason,
    string? ResponderId,
    string? ResponderGroup,
    DateTimeOffset RejectedAt) : AgentEvent
{
    public override HPD.Events.EventChannel Channel { get; init; } = HPD.Events.EventChannel.Control;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
    public override bool ShouldPersistToThread() => true;
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
    IDictionary<string, object?>? Arguments) : AgentEvent, IAgentRequestEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    /// <summary>Explicit interface implementation - maps PermissionId to RequestId</summary>
    string HPD.Events.IRequestCorrelatedEvent.RequestId => PermissionId;
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
    PermissionChoice Choice = PermissionChoice.Ask) : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;

    /// <summary>Explicit interface implementation - maps PermissionId to RequestId</summary>
    string HPD.Events.IRequestCorrelatedEvent.RequestId => PermissionId;
}

/// <summary>
/// Emitted after permission is approved (for observability).
/// </summary>
public record PermissionApprovedEvent(
    string PermissionId,
    string SourceName) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted after permission is denied (for observability).
/// </summary>
public record PermissionDeniedEvent(
    string PermissionId,
    string SourceName,
    string CallId,
    string Reason) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Middleware requests permission to continue beyond max iterations.
/// </summary>
public record ContinuationRequestEvent(
    string ContinuationId,
    string SourceName,
    int CurrentIteration,
    int MaxIterations) : AgentEvent, IAgentRequestEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    /// <summary>Explicit interface implementation - maps ContinuationId to RequestId</summary>
    string HPD.Events.IRequestCorrelatedEvent.RequestId => ContinuationId;
}

/// <summary>
/// Response to continuation request.
/// </summary>
public record ContinuationResponseEvent(
    string ContinuationId,
    string SourceName,
    bool Approved,
    int ExtensionAmount = 0) : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;

    /// <summary>Explicit interface implementation - maps ContinuationId to RequestId</summary>
    string HPD.Events.IRequestCorrelatedEvent.RequestId => ContinuationId;
}

/// <summary>
/// Agent/ToolHarness requests user clarification or additional input.
/// Handler should prompt user and send ClarificationResponseEvent.
/// </summary>
public record ClarificationRequestEvent(
    string RequestId,
    string SourceName,
    string Question,
    string? AgentName = null,
    string[]? Options = null) : AgentEvent, IAgentRequestEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

/// <summary>
/// Response to clarification request.
/// Sent by external handler back to waiting agent/ToolHarness.
/// </summary>
public record ClarificationResponseEvent(
    string RequestId,
    string SourceName,
    string Question,
    string Answer) : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;
}

/// <summary>
/// Middleware reports an error (one-way, no response needed).
/// This is not a request event - it's just informational.
/// </summary>
public record MiddlewareErrorEvent(
    string SourceName,
    string ErrorMessage) : AgentEvent, IErrorEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

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
/// They are observed through HPD.Events subscriptions.
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
    ImmutableHashSet<string> ExpandedToolHarnesses,
    ImmutableHashSet<string> ExpandedSkills,
    int TotalToolCount,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when a ToolHarness or skill container is expanded.
/// </summary>
public record ContainerExpandedEvent(
    string ContainerName,
    ContainerType Type,
    IReadOnlyList<string> UnlockedFunctions,
    int Iteration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

public enum ContainerType { ToolHarness, Skill }


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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when circuit breaker is triggered.
/// </summary>
public record CircuitBreakerTriggeredEvent(
    string AgentName,
    string FunctionName,
    int ConsecutiveCount,
    int Iteration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

/// <summary>
/// Emitted when compaction cache is checked.
/// </summary>
public record CompactionCacheEvent(
    string AgentName,
    bool IsHit,
    DateTime? CompactionCreatedAt,
    int? SummarizedUpToIndex,
    int CurrentMessageCount,
    int? TokenSavings,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
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
public record CheckpointEvent : AgentEvent, IObservabilityEvent
{
    public CheckpointEvent(
        CheckpointOperation Operation,
        string SessionId,
        DateTimeOffset Timestamp,
        TimeSpan? Duration = null,
        int? Iteration = null,
        int? WriteCount = null,
        int? SizeBytes = null,
        int? MessageCount = null,
        bool? Success = null,
        string? ErrorMessage = null)
    {
        this.Operation = Operation;
        this.SessionId = SessionId;
        this.Timestamp = Timestamp;
        this.Duration = Duration;
        this.Iteration = Iteration;
        this.WriteCount = WriteCount;
        this.SizeBytes = SizeBytes;
        this.MessageCount = MessageCount;
        this.Success = Success;
        this.ErrorMessage = ErrorMessage;
    }

    public CheckpointOperation Operation { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? Iteration { get; init; }
    public int? WriteCount { get; init; }
    public int? SizeBytes { get; init; }
    public int? MessageCount { get; init; }
    public bool? Success { get; init; }
    public string? ErrorMessage { get; init; }

    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

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
) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() => true;
}

/// <summary>
/// Emitted during polling with status updates for a background operation.
/// </summary>
public record BackgroundOperationStatusEvent(
    ResponseContinuationToken ContinuationToken,
    OperationStatus Status,
    string? StatusMessage = null
) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override bool ShouldPersistToThread() =>
        Status.IsTerminal ||
        !string.IsNullOrWhiteSpace(StatusMessage) &&
        !StatusMessage.StartsWith("Polling attempt ", StringComparison.OrdinalIgnoreCase);
}

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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

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
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;

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

    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when plan mode is activated.
/// </summary>
public record PlanModeActivatedEvent(
    string AgentName,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

#endregion

/// <summary>
/// Emitted when a nested agent is invoked.
/// </summary>
public record NestedAgentInvokedEvent(
    string OrchestratorName,
    string ChildAgentName,
    int NestingDepth,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when document processing occurs.
/// </summary>
public record DocumentProcessedEvent(
    string AgentName,
    string DocumentPath,
    long SizeBytes,
    TimeSpan Duration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when message preparation completes.
/// </summary>
public record InternalMessagePreparedEvent(
    string AgentName,
    int Iteration,
    int FinalMessageCount,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when a request event is processed.
/// </summary>
public record RequestEventProcessedEvent(
    string AgentName,
    string EventType,
    bool RequiresResponse,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when agent makes a decision.
/// </summary>
public record AgentDecisionEvent(
    string AgentName,
    string DecisionType,
    int Iteration,
    int ConsecutiveFailures,
    int CompletedFunctionsCount
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when agent completes successfully.
/// </summary>
public record AgentCompletionEvent(
    string AgentName,
    int TotalIterations,
    TimeSpan Duration,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Snapshot of a non-history message added to the model-bound context for an iteration.
/// </summary>
public sealed record ContextMessageSnapshot(
    string Role,
    string Text
);

/// <summary>
/// Snapshot of a visible tool included in the model-bound context for an iteration.
/// </summary>
public sealed record ToolContextSnapshot(
    string Name,
    string Description,
    string? ToolHarnessName,
    ToolCallType? CallType,
    bool IsContainer,
    string? InputSchemaJson
);

/// <summary>
/// Emitted immediately before an LLM call with the non-history context being fed to the model.
/// Excludes normal chat history; includes instructions, visible tool context, and middleware-injected context messages.
/// </summary>
public record IterationContextSnapshotEvent(
    string AgentName,
    int Iteration,
    int TotalMessageCount,
    int ContextMessageCount,
    IReadOnlyList<ContextMessageSnapshot> ContextMessages,
    string? Instructions,
    int ToolCount,
    IReadOnlyList<ToolContextSnapshot> Tools,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Snapshot of one middleware state entry at a lifecycle phase.
/// </summary>
public sealed record MiddlewareStateEntrySnapshot(
    string Key,
    string Type,
    string PropertyName,
    StateScope Scope,
    bool Persistent,
    int Version,
    JsonElement? Json,
    string? Error,
    bool Redacted
);

/// <summary>
/// Emitted at stable lifecycle phases with the current internal middleware state.
/// </summary>
public record MiddlewareStateSnapshotEvent(
    string AgentName,
    string? SessionId,
    string? ThreadId,
    int Iteration,
    string Phase,
    string? BatchId,
    string? FunctionCallId,
    int? ToolCallIndex,
    int StateCount,
    IReadOnlyList<MiddlewareStateEntrySnapshot> States,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Snapshot of one middleware state entry change detected across a lifecycle phase.
/// </summary>
public sealed record MiddlewareStateChange(
    string Key,
    string Type,
    string PropertyName,
    StateScope Scope,
    bool Persistent,
    int Version,
    string ChangeType,
    JsonElement? Before,
    JsonElement? After,
    string? Error,
    bool Redacted
);

/// <summary>
/// Emitted when middleware state changes across a stable lifecycle phase.
/// </summary>
public record MiddlewareStateChangedEvent(
    string AgentName,
    string? SessionId,
    string? ThreadId,
    int Iteration,
    string Phase,
    string? BatchId,
    string? FunctionCallId,
    int? ToolCallIndex,
    int ChangeCount,
    IReadOnlyList<MiddlewareStateChange> Changes,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

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
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;

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
/// Tracks how many ToolHarnesses and skills have been expanded.
/// </summary>
public record CollapsingStateEvent(
    string AgentName,
    int Iteration,
    int ExpandedToolHarnessesCount,
    int ExpandedSkillsCount,
    DateTimeOffset Timestamp
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}




#endregion

#region Structured Output Events

/// <summary>
/// A structured output result containing a parsed (partial or complete) value.
/// Emitted by RunStructuredAsync&lt;T&gt;().
/// </summary>
/// <typeparam name="T">The output type</typeparam>
/// <param name="Value">The parsed value (partial or complete)</param>
/// <param name="IsPartial">True if this is an intermediate result, false if final</param>
/// <param name="RawJson">The raw JSON string that was parsed</param>
public sealed record StructuredResultEvent<T>(
    T Value,
    bool IsPartial,
    string RawJson
) : AgentEvent where T : class
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

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
    [property: System.Text.Json.Serialization.JsonIgnore]
    Exception? Exception = null
) : AgentEvent, IErrorEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

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
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Emitted when an event is dropped due to stream interruption.
/// Provides observability into dropped events.
/// </summary>
public record EventDroppedEvent(
    string DroppedEventFlowId,
    string DroppedEventType,
    long DroppedSequenceNumber) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

#endregion

#endregion

/// <summary>
/// Abstraction for request-session event coordination.
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
