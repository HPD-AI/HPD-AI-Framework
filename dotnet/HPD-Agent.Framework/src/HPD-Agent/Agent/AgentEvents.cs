using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using HPD.Agent.Planning;
using HPD.Agent.Permissions;
using HPD.Agent.Providers;
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
/// Emitted after an interruption request has been applied to active streams or turns.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("INTERRUPTION_HANDLED")]
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
    /// Stable identity assigned when the event is created. Canonical commit preserves it.
    /// </summary>
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Authoritative position in a committed thread journal. Zero means the event is staged or
    /// has not yet been committed. Runs without a configured session store use the agent-owned
    /// ephemeral journal, so accounted terminal events still receive a canonical position.
    /// </summary>
    public long ThreadSequenceNumber { get; init; }

    /// <summary>
    /// Durable session scope when this event is persisted or replayed from a thread.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Durable thread scope when this event is persisted or replayed from a thread.
    /// </summary>
    public string? ThreadId { get; init; }

    /// <summary>
    /// Durable identity of the accepted thread execution that produced or owns this event.
    /// Null for structural, administrative, or otherwise execution-independent events.
    /// </summary>
    public virtual string? ThreadExecutionId { get; init; }

    /// <summary>
    /// Canonical attribution for the agent that emitted this event.
    /// When present, this metadata is persisted so live observation and replay remain identical.
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

}

/// <summary>
/// Base type for first-class user input events.
/// </summary>
public abstract record AgentInputEvent
{
    /// <summary>Client-owned correlation identifier for reconciling submitted input with admitted transcript messages.</summary>
    public string? ClientInputId { get; init; }

    /// <summary>Session scope for the input event.</summary>
    public string? SessionId { get; init; }

    /// <summary>Thread scope for the input event. Defaults to the agent's thread resolution behavior when null.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Optional target agent identifier for hosted or multi-agent runtimes.</summary>
    public string? AgentId { get; init; }

    /// <summary>Per-run configuration carried with the input event.</summary>
    public AgentRunConfig? RunConfig { get; init; }

    /// <summary>Identifier of the accepted input execution assigned by the coordinating runtime.</summary>
    public string? ThreadExecutionId { get; init; }
}

/// <summary>
/// Selects how one conversational input is delivered. This policy belongs only
/// to <see cref="UserMessagesInputEvent"/>; fixed-routing semantic commands do
/// not acquire conversation delivery semantics.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentInputDelivery>))]
public enum AgentInputDelivery
{
    /// <summary>Admit one distinct conversation turn.</summary>
    Queue = 0,

    /// <summary>Guide the matching active execution at its next safe model boundary.</summary>
    Steer = 1
}

/// <summary>
/// Emitted after a coordinating runtime durably accepts an input execution for a thread.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("THREAD_EXECUTION_STARTED")]
public sealed record ThreadExecutionStartedEvent : AgentEvent
{
    /// <summary>Creates a validated execution-start fact.</summary>
    public ThreadExecutionStartedEvent(
        string threadExecutionId,
        string agentId,
        DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ThreadExecutionId = threadExecutionId;
        AgentId = agentId;
        StartedAt = startedAt;
    }

    /// <summary>Gets the durable identity of the accepted thread execution.</summary>
    [AllowNull]
    public override string ThreadExecutionId { get; init; }

    /// <summary>Gets the agent that owns the accepted execution.</summary>
    public string AgentId { get; init; }

    /// <summary>Gets when the runtime accepted the execution.</summary>
    public DateTimeOffset StartedAt { get; init; }

    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override EventChannel Channel { get; init; } = EventChannel.Control;
}

/// <summary>
/// Describes the terminal outcome of a thread input execution.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThreadExecutionOutcome>))]
public enum ThreadExecutionOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>Serializable failure information for a failed thread execution.</summary>
/// <param name="Type">The stable exception or error type name.</param>
/// <param name="Message">The human-readable failure message.</param>
public sealed record ThreadExecutionError
{
    /// <summary>Creates validated failure information for a thread execution.</summary>
    public ThreadExecutionError(string type, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Type = type;
        Message = message;
    }

    /// <summary>Gets the stable exception or error type name.</summary>
    public string Type { get; init; }

    /// <summary>Gets the human-readable failure message.</summary>
    public string Message { get; init; }
}

/// <summary>
/// Emitted after a submitted input has reached a terminal outcome and leaves its execution slot.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("THREAD_EXECUTION_FINISHED")]
public sealed record ThreadExecutionFinishedEvent : AgentEvent
{
    /// <summary>Creates a validated terminal execution fact.</summary>
    public ThreadExecutionFinishedEvent(
        string threadExecutionId,
        string agentId,
        ThreadExecutionOutcome outcome,
        DateTimeOffset finishedAt,
        ThreadExecutionError? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if ((outcome == ThreadExecutionOutcome.Failed) != (error is not null))
        {
            throw new ArgumentException(
                "Failed executions require error details, and non-failed executions cannot carry them.",
                nameof(error));
        }

        ThreadExecutionId = threadExecutionId;
        AgentId = agentId;
        Outcome = outcome;
        FinishedAt = finishedAt;
        Error = error;
    }

    /// <summary>Gets the execution identifier correlated with the corresponding start fact.</summary>
    [AllowNull]
    public override string ThreadExecutionId { get; init; }

    /// <summary>Gets the agent that executed the accepted input.</summary>
    public string AgentId { get; init; }

    /// <summary>Gets the terminal outcome reported by the execution owner.</summary>
    public ThreadExecutionOutcome Outcome { get; init; }

    /// <summary>Gets when the input left active execution.</summary>
    public DateTimeOffset FinishedAt { get; init; }

    /// <summary>Gets failure details when <see cref="Outcome"/> is <see cref="ThreadExecutionOutcome.Failed"/>.</summary>
    public ThreadExecutionError? Error { get; init; }

    /// <summary>
    /// Gets the exact terminal result produced for the submitted input. This is the
    /// durable, transport-neutral receipt correlated by <see cref="ThreadExecutionId"/>.
    /// </summary>
    public AgentInputResult? InputResult { get; init; }

    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override EventChannel Channel { get; init; } = EventChannel.Control;
}

/// <summary>
/// Records a parent delegation to a durable child thread, including its resolved context
/// and invocation-mode decisions.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_INVOCATION_STARTED")]
public sealed record SubAgentInvocationStartedEvent(
    string InvocationId,
    string ParentToolCallId,
    string ChildAgentId,
    string ChildSessionId,
    string ChildThreadId,
    string RoleName,
    SubAgentContextPolicy ContextPolicy,
    AgentInvocationMode Mode) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override EventChannel Channel { get; init; } = EventChannel.Control;
}

/// <summary>Records successful completion of one parent-to-child delegation.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_INVOCATION_COMPLETED")]
public sealed record SubAgentInvocationCompletedEvent(string InvocationId, string? Summary) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override EventChannel Channel { get; init; } = EventChannel.Control;
}

/// <summary>Records failure of one parent-to-child delegation.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_INVOCATION_FAILED")]
public sealed record SubAgentInvocationFailedEvent(string InvocationId, string ErrorType, string Message) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override EventChannel Channel { get; init; } = EventChannel.Control;
}

/// <summary>Records cancellation of one parent-to-child delegation.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_INVOCATION_CANCELLED")]
public sealed record SubAgentInvocationCancelledEvent(string InvocationId, string? Reason) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override EventChannel Channel { get; init; } = EventChannel.Control;
}

/// <summary>
/// User message input sent into an agent turn. When <see cref="Messages"/> is empty,
/// the agent resumes the scoped thread using existing history and the supplied run config.
/// </summary>
public sealed record UserMessagesInputEvent : AgentInputEvent
{
    /// <summary>Messages to add for this turn. Empty means resume the scoped thread.</summary>
    public IReadOnlyList<ChatMessage> Messages { get; init; } = Array.Empty<ChatMessage>();

    /// <summary>
    /// Gets the explicit conversation-delivery policy. Queue is the safe wire and
    /// CLR default so older payloads never steer active work implicitly.
    /// </summary>
    public AgentInputDelivery Delivery { get; init; } = AgentInputDelivery.Queue;

    /// <summary>Process-local session scope for in-memory integrations.</summary>
    [JsonIgnore]
    public Session? Session { get; init; }

    /// <summary>Process-local thread scope for in-memory integrations.</summary>
    [JsonIgnore]
    public Thread? Thread { get; init; }

    [JsonIgnore]
    internal AgentChatClientHandle? InheritedChatClient { get; init; }

    [JsonIgnore]
    internal ClientFamilyInheritanceMode InheritedChatMode { get; init; } = ClientFamilyInheritanceMode.UseOwn;
}

/// <summary>Explicitly compacts the scoped thread without creating a user message or model turn.</summary>
public sealed record CompactThreadInputEvent : AgentInputEvent
{
    public required ThreadCompactionRequest Request { get; init; }

    [JsonIgnore]
    public Session? Session { get; init; }

    [JsonIgnore]
    public Thread? Thread { get; init; }
}

#region Message Turn Events (Entire User Interaction)

/// <summary>
/// Emitted when a message turn starts (user sends message, agent begins processing)
/// This represents the START of the entire multi-step agent execution.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("MESSAGE_TURN_STARTED")]
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

    public int? InputMessageCount { get; init; }
    public bool? IsResume { get; init; }
}

/// <summary>
/// Emitted when a message turn completes successfully
/// This represents the END of the entire agent execution for this user message.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("MESSAGE_TURN_FINISHED")]
public record MessageTurnFinishedEvent : AgentEvent
{
    [JsonConstructor]
    public MessageTurnFinishedEvent(
        string MessageTurnId,
        string ConversationId,
        string AgentId,
        string AgentName,
        TimeSpan Duration,
        MessageTurnUsageSummary Usage)
    {
        this.MessageTurnId = MessageTurnId;
        this.ConversationId = ConversationId;
        this.AgentId = AgentId;
        this.AgentName = AgentName;
        this.Duration = Duration;
        this.Usage = Usage;
    }

    public string MessageTurnId { get; init; }
    public string ConversationId { get; init; }
    public string AgentId { get; init; }
    public string AgentName { get; init; }
    public TimeSpan Duration { get; init; }
    public MessageTurnUsageSummary Usage { get; init; }

    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;

    public int? Iteration { get; init; }
    public string? TerminationReason { get; init; }
    public int? TurnMessageCount { get; init; }
}

/// <summary>
/// Emitted when an error occurs during message turn execution.
/// Error category is lazily computed from the exception using GenericErrorHandler.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("MESSAGE_TURN_ERROR")]
public record MessageTurnErrorEvent(
    string MessageTurnId,
    string ErrorMessage,
    MessageTurnUsageSummary Usage,
    [property: System.Text.Json.Serialization.JsonIgnore] Exception? Exception = null) : AgentEvent, IErrorEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    public string? ConversationId { get; init; }
    public string? AgentId { get; init; }
    public string? AgentName { get; init; }
    public string? ErrorType { get; init; }

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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_TURN_STARTED")]
public record AgentTurnStartedEvent(int Iteration) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted when an agent turn completes.
/// An agent turn represents one completed model call within the enclosing message turn.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_TURN_FINISHED")]
public record AgentTurnFinishedEvent(
    string MessageTurnId,
    int Iteration,
    string OperationId,
    string? LogicalOperationId,
    int Attempt,
    ProviderClientFamily Family,
    ProviderOperationOutcome Outcome,
    UsageDetails? Usage,
    string? ProviderKey,
    string? ModelId,
    string? ResponseId) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("PROVIDER_OPERATION_USAGE")]
public sealed record ProviderOperationUsageEvent(
    string MessageTurnId,
    string OperationId,
    string? LogicalOperationId,
    int Attempt,
    ProviderOperationKind OperationKind,
    ProviderClientFamily Family,
    ProviderOperationOutcome Outcome,
    UsageDetails? Usage,
    string? ProviderKey,
    string? ModelId,
    string? ResponseId) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("PROVIDER_VALUATION_OBSERVATION")]
public sealed record ProviderValuationObservationEvent(
    string MessageTurnId,
    string SourceEventId,
    ProviderValuationObservation Observation) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>Emitted before all dynamic capability sources are reconciled.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_CAPABILITY_REFRESH_STARTED")]
public sealed record AgentCapabilityRefreshStartedEvent(long CurrentEpoch, string Reason) : AgentEvent;

/// <summary>Emitted after a complete validated capability epoch is published.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_CAPABILITY_REFRESH_PUBLISHED")]
public sealed record AgentCapabilityRefreshPublishedEvent(
    long PreviousEpoch,
    long NewEpoch,
    string Reason) : AgentEvent;

/// <summary>Emitted when a complete capability candidate is rejected and the prior epoch remains active.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_CAPABILITY_REFRESH_REJECTED")]
public sealed record AgentCapabilityRefreshRejectedEvent(
    long RetainedEpoch,
    string Error,
    string Reason) : AgentEvent, IErrorEvent
{
    /// <inheritdoc />
    public string ErrorMessage => Error;

    /// <inheritdoc />
    [JsonIgnore]
    public Exception? Exception => null;
}

/// <summary>Emitted when one immutable effective capability surface is pinned for a turn.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_TURN_CAPABILITIES_PINNED")]
public sealed record AgentTurnCapabilitiesPinnedEvent : AgentEvent
{
    /// <summary>Gets the complete stable identity of the effective surface.</summary>
    public required AgentTurnCapabilityIdentity Identity { get; init; }
}

/// <summary>Emitted before a skill resolves its authoritative instructions.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_ACTIVATION_STARTED")]
public sealed record SkillActivationStartedEvent(CapabilityId CapabilityId, string Name) : AgentEvent;

/// <summary>Emitted after skill instructions resolve successfully.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_ACTIVATED")]
public sealed record SkillActivatedEvent(
    CapabilityId CapabilityId,
    string Name,
    int RevealedCapabilityCount,
    SkillActivationLifetime Lifetime) : AgentEvent;

/// <summary>Emitted when skill instruction resolution fails.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_ACTIVATION_FAILED")]
public sealed record SkillActivationFailedEvent(
    CapabilityId CapabilityId,
    string Name,
    string ErrorType) : AgentEvent, IErrorEvent
{
    /// <inheritdoc />
    public string ErrorMessage => "Skill activation failed.";

    /// <inheritdoc />
    [JsonIgnore]
    public Exception? Exception => null;
}

/// <summary>Emitted before a model-visible skill resource is read.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_RESOURCE_READ_STARTED")]
public sealed record SkillResourceReadStartedEvent(CapabilityId CapabilityId, string Name) : AgentEvent;

/// <summary>Emitted after a model-visible skill resource is read.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_RESOURCE_READ_COMPLETED")]
public sealed record SkillResourceReadCompletedEvent(CapabilityId CapabilityId, string Name) : AgentEvent;

/// <summary>Emitted when a skill resource read fails.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_RESOURCE_READ_FAILED")]
public sealed record SkillResourceReadFailedEvent(
    CapabilityId CapabilityId,
    string Name,
    string ErrorType) : AgentEvent, IErrorEvent
{
    /// <inheritdoc />
    public string ErrorMessage => "Skill resource read failed.";

    /// <inheritdoc />
    [JsonIgnore]
    public Exception? Exception => null;
}

/// <summary>Emitted before an external skill script runner starts.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_SCRIPT_STARTED")]
public sealed record SkillScriptStartedEvent(
    CapabilityId CapabilityId,
    string Name,
    string Runner) : AgentEvent;

/// <summary>Emitted after an external skill script runner completes.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_SCRIPT_COMPLETED")]
public sealed record SkillScriptCompletedEvent(CapabilityId CapabilityId, string Name) : AgentEvent;

/// <summary>Emitted when external skill script execution fails.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_SCRIPT_FAILED")]
public sealed record SkillScriptFailedEvent(
    CapabilityId CapabilityId,
    string Name,
    SkillScriptErrorCategory Category) : AgentEvent, IErrorEvent
{
    /// <inheritdoc />
    public string ErrorMessage => "Skill script execution failed.";

    /// <inheritdoc />
    [JsonIgnore]
    public Exception? Exception => null;
}

/// <summary>Emitted when an external skill script exceeds its configured timeout.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SKILL_SCRIPT_TIMED_OUT")]
public sealed record SkillScriptTimedOutEvent(CapabilityId CapabilityId, string Name) : AgentEvent, IErrorEvent
{
    /// <inheritdoc />
    public string ErrorMessage => "Skill script execution timed out.";

    /// <inheritdoc />
    [JsonIgnore]
    public Exception? Exception => null;
}

/// <summary>
/// Emitted during agent execution to expose internal state for testing/debugging.
/// NOT intended for production use - only for characterization tests and debugging.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("STATE_SNAPSHOT")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("TEXT_MESSAGE_START")]
public record TextMessageStartEvent(
    string MessageId,
    string Role,
    AgentMessageSource Source = AgentMessageSource.Unspecified,
    AgentMessageVisibility Visibility = AgentMessageVisibility.Transcript,
    AgentMessagePersistence Persistence = AgentMessagePersistence.ThreadHistory,
    string? AuthorName = null,
    DateTimeOffset? CreatedAt = null,
    string? ClientInputId = null,
    AdditionalPropertiesDictionary? AdditionalProperties = null) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>
/// Emitted when the agent produces text content (streaming delta)
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("TEXT_DELTA")]
public record TextDeltaEvent(string Text, string MessageId) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>
/// Emitted when the agent finishes producing text content
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("TEXT_MESSAGE_END")]
public record TextMessageEndEvent(string MessageId) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>Durably replaces the complete snapshot of an existing thread message.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("THREAD_MESSAGE_REPLACED")]
public sealed record ThreadMessageReplacedEvent(
    string MessageId,
    ChatMessage Replacement,
    string Reason) : AgentEvent;

/// <summary>
/// Outbound, lean projection of a user input into the transcript stream.
/// Emitted once per user message at commit time. Carries only the transcript
/// facts needed to render the user bubble; never the AgentInputEvent (RunConfig,
/// ClientInputId, etc.). Consumers that render a user bubble MUST handle this
/// type; consumers that only render agent output may ignore it.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("USER_MESSAGE")]
public sealed record UserMessageEvent(string Text, string MessageId) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>
/// Emitted when a realtime provider produces a user input transcript update.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("USER_AUDIO_TRANSCRIPT_DELTA")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("USER_AUDIO_TRANSCRIPT_COMPLETED")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("USER_AUDIO_TRANSCRIPT_FAILED")]
public sealed record UserAudioTranscriptFailedEvent(
    string MessageId,
    string ErrorMessage,
    string? ProviderItemId = null,
    int? ContentIndex = null) : AgentEvent, IErrorEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;

    [JsonIgnore]
    public Exception? Exception => null;
}


#endregion

#region Reasoning Events (For reasoning-capable models like o1, DeepSeek-R1)

/// <summary>
/// Emitted when the agent starts producing reasoning content.
/// Reasoning is extended thinking used by models like o1, DeepSeek-R1.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("REASONING_MESSAGE_START")]
public record ReasoningMessageStartEvent(string MessageId, string Role) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>
/// Emitted when the agent produces reasoning content (streaming delta).
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("REASONING_DELTA")]
public record ReasoningDeltaEvent(string Text, string MessageId, string? ProtectedData = null) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>
/// Emitted when the agent finishes producing reasoning content.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("REASONING_MESSAGE_END")]
public record ReasoningMessageEndEvent(string MessageId) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
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
    /// <summary>A tool exposed by an [McpServer].</summary>
    McpServer,
    /// <summary>A function generated from an [OpenApi] spec.</summary>
    OpenApi,
}

/// <summary>
/// Emitted when the agent requests a tool call
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("TOOL_CALL_START")]
public record ToolCallStartEvent(
    string CallId,
    string Name,
    string MessageId,
    string? ToolHarnessName = null,
    ToolCallType? CallType = null) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a tool call's arguments are fully available
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("TOOL_CALL_ARGS")]
public record ToolCallArgsEvent(string CallId, string ArgsJson) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
}

/// <summary>
/// Emitted when a tool call completes execution and the assistant function call is complete.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("TOOL_CALL_END")]
public record ToolCallEndEvent(
    string CallId,
    string MessageId,
    string Name,
    string ArgsJson) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a tool call result is available
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("TOOL_CALL_RESULT")]
public record ToolCallResultEvent(
    string CallId,
    ToolResultPayload Result,
    string? ToolHarnessName = null,
    ToolCallType? CallType = null,
    string? Name = null) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;

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

 /// <summary>Emitted exactly once when an operation becomes authoritative in its owning thread.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_OPERATION_REGISTERED")]
public sealed record AgentOperationRegisteredEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;

    /// <summary>Gets the complete initial operation snapshot.</summary>
    public required AgentOperationSnapshot Operation { get; init; }
}

/// <summary>Emitted once for each committed, version-checked operation transition.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_OPERATION_TRANSITIONED")]
public sealed record AgentOperationTransitionedEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;

    /// <summary>Gets the operation identifier.</summary>
    public required string OperationId { get; init; }

    /// <summary>Gets the version replaced by this transition.</summary>
    public required long PreviousVersion { get; init; }

    /// <summary>Gets the complete snapshot after the transition.</summary>
    public required AgentOperationSnapshot Operation { get; init; }

    /// <summary>Gets the provider deduplication key when supplied.</summary>
    public string? ProviderDeduplicationKey { get; init; }
}

/// <summary>Durably records the bounded semantic facts used to execute a function call.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("FUNCTION_INVOCATION_AUDITED")]
public sealed record FunctionInvocationAuditedEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;

    /// <summary>Gets the immutable bounded invocation projection.</summary>
    public required FunctionInvocationAuditProjection Invocation { get; init; }
}

/// <summary>Records a ToolBody failure that occurred after an operation registration committed.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("TOOL_BODY_OPERATION_COMMITTED_FAILURE")]
public sealed record ToolBodyOperationCommittedFailureEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;

    /// <summary>Gets the bounded invocation projection.</summary>
    public required FunctionInvocationAuditProjection Invocation { get; init; }

    /// <summary>Gets the single committed operation receipt and call identity.</summary>
    public required CommittedToolBodyOperation CommittedOperation { get; init; }

    /// <summary>Gets the safe failure description.</summary>
    public required string ErrorMessage { get; init; }
}

/// <summary>Records a non-throwing failure while releasing an operation execution owner.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("OPERATION_EXECUTION_OWNER_CLEANUP_FAILED")]
public sealed record OperationExecutionOwnerCleanupFailedEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;

    /// <summary>Gets the operation whose execution owner was being released.</summary>
    public required string OperationId { get; init; }

    /// <summary>Gets the bounded operation name.</summary>
    public required string OperationName { get; init; }

    /// <summary>Gets the bounded safe cleanup failure description.</summary>
    public required string ErrorMessage { get; init; }
}

 #endregion

#region Middleware Events

/// <summary>
/// Middleware requests permission to execute a function.
/// Handler should prompt user and send PermissionResponseEvent.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("PERMISSION_REQUEST")]
public record PermissionRequestEvent(
    string PermissionId,
    string SourceName,
    string FunctionName,
    string? Action,
    string CallId,
    PermissionEvaluationEnvelope Evaluation) : AgentEvent, IAgentRequestEvent<PermissionResponseEvent>
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    /// <summary>Explicit interface implementation - maps PermissionId to RequestId</summary>
    public string RequestId => PermissionId;
}

/// <summary>
/// Response to permission request.
/// Sent by external handler back to waiting Middleware.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("PERMISSION_RESPONSE")]
public record PermissionResponseEvent(
    string PermissionId,
    string SourceName,
    string ChoiceId,
    string? Feedback = null) : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;

    /// <summary>Explicit interface implementation - maps PermissionId to RequestId</summary>
    public string RequestId => PermissionId;
}

/// <summary>Records an atomically committed session permission-preference change.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("PERMISSION_PREFERENCE_CHANGED")]
public sealed record PermissionPreferenceChangedEvent(
    string PreferenceId,
    PermissionKey Key,
    PermissionDecisionKind Decision,
    PermissionPersistenceKind Persistence) : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
}

/// <summary>
/// Middleware requests permission to continue beyond max iterations.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CONTINUATION_REQUEST")]
public record ContinuationRequestEvent(
    string ContinuationId,
    string SourceName,
    int CurrentIteration,
    int MaxIterations) : AgentEvent, IAgentRequestEvent<ContinuationResponseEvent>
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;

    /// <summary>Explicit interface implementation - maps ContinuationId to RequestId</summary>
    public string RequestId => ContinuationId;
}

/// <summary>
/// Response to continuation request.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CONTINUATION_RESPONSE")]
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
    public string RequestId => ContinuationId;
}

/// <summary>
/// Agent/ToolHarness requests user clarification or additional input.
/// Handler should prompt user and send ClarificationResponseEvent.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CLARIFICATION_REQUEST")]
public record ClarificationRequestEvent(
    string RequestId,
    string SourceName,
    string Question,
    string? AgentName = null,
    string[]? Options = null) : AgentEvent, IAgentRequestEvent<ClarificationResponseEvent>
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

/// <summary>
/// Response to clarification request.
/// Sent by external handler back to waiting agent/ToolHarness.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CLARIFICATION_RESPONSE")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("MIDDLEWARE_ERROR")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("COLLAPSED_TOOLS_VISIBLE")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CONTAINER_EXPANDED")]
public record ContainerExpandedEvent(
    string ContainerName,
    ContainerType ContainerType,
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("PERMISSION_CHECK")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("ITERATION_START")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CIRCUIT_BREAKER_TRIGGERED")]
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
/// Emitted when parallel tool execution starts.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("INTERNAL_PARALLEL_TOOL_EXECUTION")]
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
/// Emitted when a function execution is being retried due to an error.
/// Emitted by FunctionRetryMiddleware for observability.
/// Error category is lazily computed from the live exception, or from the persisted
/// error message after the event has been rehydrated.
/// </summary>
/// <param name="FunctionName">The name of the function being retried</param>
/// <param name="Attempt">The current retry attempt number (1-based)</param>
/// <param name="MaxRetries">Maximum number of retries allowed</param>
/// <param name="Delay">Time to wait before retrying</param>
/// <param name="ExceptionType">The type name of the exception</param>
/// <param name="ErrorMessage">The error message from the exception</param>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("FUNCTION_RETRY")]
public record FunctionRetryEvent(
    string FunctionName,
    int Attempt,
    int MaxRetries,
    TimeSpan Delay,
    string ExceptionType,
    string ErrorMessage
) : AgentEvent, IObservabilityEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;

    /// <summary>
    /// The live exception that caused the retry. This value is not serialized and is
    /// therefore <see langword="null"/> after the event is rehydrated.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Exception? Exception { get; init; }

    // Lazy-computed error details
    private ErrorHandling.ProviderErrorDetails? _errorDetails;
    private bool _errorDetailsParsed;

    private ErrorHandling.ProviderErrorDetails? GetErrorDetails()
    {
        if (!_errorDetailsParsed)
        {
            _errorDetailsParsed = true;
            var handler = new ErrorHandling.GenericErrorHandler();
            _errorDetails = handler.ParseError(Exception ?? new Exception(ErrorMessage));
        }
        return _errorDetails;
    }

    /// <summary>
    /// Error category lazily computed from the live exception or persisted error message.
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
/// Error category is lazily computed from the live exception, or from the persisted
/// error message after the event has been rehydrated.
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
/// <param name="ExceptionType">The type name of the exception</param>
/// <param name="ErrorMessage">The error message from the exception</param>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("MODEL_CALL_RETRY")]
public record ModelCallRetryEvent(
    int Attempt,
    int MaxRetries,
    TimeSpan Delay,
    string ExceptionType,
    string ErrorMessage
) : AgentEvent, IObservabilityEvent
{
    /// <summary>
    /// The live exception that caused the retry. This value is not serialized and is
    /// therefore <see langword="null"/> after the event is rehydrated.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Exception? Exception { get; init; }

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
            _errorDetails = handler.ParseError(Exception ?? new Exception(ErrorMessage));
        }
        return _errorDetails;
    }

    /// <summary>
    /// Error category lazily computed from the live exception or persisted error message.
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("DELTA_SENDING_ACTIVATED")]
public record DeltaSendingActivatedEvent(
    string AgentName,
    int MessageCountSent,
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
/// Consolidated plan update event.
/// Emitted whenever a plan is created or modified, containing the full plan state.
/// </summary>
/// <remarks>
/// <para><b>Design Rationale:</b></para>
/// <para>
/// This emits a single event type with full plan state rather than multiple granular events. Benefits:
/// - Simpler for consumers (one event handler)
/// - Always includes complete context (no partial state)
/// - Reduces serialization registrations
/// </para>
/// <para>
/// The UpdateType discriminator allows consumers to react to specific changes while
/// always having access to the complete plan state for UI synchronization.
/// </para>
/// </remarks>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("PLAN_UPDATED")]
public record PlanUpdatedEvent(
    string PlanId,
    string ConversationId,
    PlanUpdateType UpdateType,
    AgentPlanData Plan,
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("NESTED_AGENT_INVOKED")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("DOCUMENT_PROCESSED")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("INTERNAL_MESSAGE_PREPARED")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("REQUEST_EVENT_PROCESSED")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_DECISION")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_COMPLETION")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("ITERATION_CONTEXT_SNAPSHOT")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("MIDDLEWARE_STATE_SNAPSHOT")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("MIDDLEWARE_STATE_CHANGED")]
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
/// Emitted by ToolCollapsingMiddleware at iteration start to report Collapsing state.
/// Tracks how many ToolHarnesses and skills have been expanded.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("COLLAPSING_STATE")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("STRUCTURED_OUTPUT_ERROR")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("STRUCTURED_OUTPUT_START")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("STRUCTURED_OUTPUT_PARTIAL")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("STRUCTURED_OUTPUT_COMPLETE")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("EVENT_DROPPED")]
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
