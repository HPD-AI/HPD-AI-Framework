using System.Text.Json;
using System.Text.RegularExpressions;

namespace HPD.Agent.Serialization;

/// <summary>
/// Provides Native AOT compatible JSON serialization for agent events.
/// Uses source-generated serialization for optimal performance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design Principles:</b>
/// - Events remain pure domain objects (no serialization code)
/// - Version and type fields injected via simple string manipulation
/// - SCREAMING_SNAKE_CASE type discriminators for JSON API convention
/// - Native AOT compatible (zero reflection)
/// </para>
/// <para>
/// <b>Usage:</b>
/// <code>
/// var evt = new TextDeltaEvent("hello", "msg-123");
/// var json = AgentEventSerializer.ToJson(evt);
/// // {"version":"1.0","type":"TEXT_DELTA","text":"hello","messageId":"msg-123"}
/// </code>
/// </para>
/// </remarks>
public static partial class AgentEventSerializer
{
    /// <summary>
    /// Type name to SCREAMING_SNAKE_CASE discriminator mapping.
    /// Framework events are pre-registered; custom events are auto-added by source generator.
    /// </summary>
    private static readonly Dictionary<Type, string> TypeNames = new()
    {
        // Message Turn Events
        [typeof(MessageTurnStartedEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_STARTED,
        [typeof(MessageTurnFinishedEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_FINISHED,
        [typeof(MessageTurnErrorEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_ERROR,

        // Agent Turn Events
        [typeof(AgentTurnStartedEvent)] = EventTypes.AgentTurn.AGENT_TURN_STARTED,
        [typeof(AgentTurnFinishedEvent)] = EventTypes.AgentTurn.AGENT_TURN_FINISHED,
        [typeof(StateSnapshotEvent)] = EventTypes.AgentTurn.STATE_SNAPSHOT,

        // Content Events
        [typeof(TextMessageStartEvent)] = EventTypes.Content.TEXT_MESSAGE_START,
        [typeof(TextDeltaEvent)] = EventTypes.Content.TEXT_DELTA,
        [typeof(TextMessageEndEvent)] = EventTypes.Content.TEXT_MESSAGE_END,

        // Reasoning Events
        [typeof(ReasoningMessageStartEvent)] = EventTypes.Reasoning.REASONING_MESSAGE_START,
        [typeof(ReasoningDeltaEvent)] = EventTypes.Reasoning.REASONING_DELTA,
        [typeof(ReasoningMessageEndEvent)] = EventTypes.Reasoning.REASONING_MESSAGE_END,

        // Tool Events
        [typeof(ToolCallStartEvent)] = EventTypes.Tool.TOOL_CALL_START,
        [typeof(ToolCallArgsEvent)] = EventTypes.Tool.TOOL_CALL_ARGS,
        [typeof(ToolCallEndEvent)] = EventTypes.Tool.TOOL_CALL_END,
        [typeof(ToolCallResultEvent)] = EventTypes.Tool.TOOL_CALL_RESULT,

        // Permission Events
        [typeof(PermissionRequestEvent)] = EventTypes.Permission.PERMISSION_REQUEST,
        [typeof(PermissionResponseEvent)] = EventTypes.Permission.PERMISSION_RESPONSE,
        [typeof(PermissionApprovedEvent)] = EventTypes.Permission.PERMISSION_APPROVED,
        [typeof(PermissionDeniedEvent)] = EventTypes.Permission.PERMISSION_DENIED,
        [typeof(ContinuationRequestEvent)] = EventTypes.Permission.CONTINUATION_REQUEST,
        [typeof(ContinuationResponseEvent)] = EventTypes.Permission.CONTINUATION_RESPONSE,

        // Clarification Events
        [typeof(ClarificationRequestEvent)] = EventTypes.Clarification.CLARIFICATION_REQUEST,
        [typeof(ClarificationResponseEvent)] = EventTypes.Clarification.CLARIFICATION_RESPONSE,

        // Middleware Events
        [typeof(MiddlewareErrorEvent)] = EventTypes.Middleware.MIDDLEWARE_ERROR,
        [typeof(HistoryReductionEvent)] = EventTypes.Middleware.HISTORY_REDUCTION,
        [typeof(MaxConsecutiveErrorsExceededEvent)] = EventTypes.Middleware.MAX_CONSECUTIVE_ERRORS_EXCEEDED,
        [typeof(TotalErrorThresholdExceededEvent)] = EventTypes.Middleware.TOTAL_ERROR_THRESHOLD_EXCEEDED,
        [typeof(PIIDetectedEvent)] = EventTypes.Middleware.PII_DETECTED,

        // Branch events removed - branching is now an application-level concern

        // Observability Events
        [typeof(CollapsedToolsVisibleEvent)] = EventTypes.Observability.COLLAPSED_TOOLS_VISIBLE,
        [typeof(ContainerExpandedEvent)] = EventTypes.Observability.CONTAINER_EXPANDED,
        [typeof(PermissionCheckEvent)] = EventTypes.Observability.PERMISSION_CHECK,
        [typeof(IterationStartEvent)] = EventTypes.Observability.ITERATION_START,
        [typeof(CircuitBreakerTriggeredEvent)] = EventTypes.Observability.CIRCUIT_BREAKER_TRIGGERED,
        [typeof(HistoryReductionCacheEvent)] = EventTypes.Observability.HISTORY_REDUCTION_CACHE,
        [typeof(CheckpointEvent)] = EventTypes.Observability.CHECKPOINT,
        [typeof(InternalParallelToolExecutionEvent)] = EventTypes.Observability.INTERNAL_PARALLEL_TOOL_EXECUTION,
        [typeof(InternalRetryEvent)] = EventTypes.Observability.INTERNAL_RETRY,
        [typeof(FunctionRetryEvent)] = EventTypes.Observability.FUNCTION_RETRY,
        [typeof(ModelCallRetryEvent)] = EventTypes.Observability.MODEL_CALL_RETRY,
        [typeof(DeltaSendingActivatedEvent)] = EventTypes.Observability.DELTA_SENDING_ACTIVATED,
        [typeof(PlanModeActivatedEvent)] = EventTypes.Observability.PLAN_MODE_ACTIVATED,
        [typeof(PlanUpdatedEvent)] = EventTypes.Observability.PLAN_UPDATED,
        [typeof(NestedAgentInvokedEvent)] = EventTypes.Observability.NESTED_AGENT_INVOKED,
        [typeof(DocumentProcessedEvent)] = EventTypes.Observability.DOCUMENT_PROCESSED,
        [typeof(InternalMessagePreparedEvent)] = EventTypes.Observability.INTERNAL_MESSAGE_PREPARED,
        [typeof(BidirectionalEventProcessedEvent)] = EventTypes.Observability.BIDIRECTIONAL_EVENT_PROCESSED,
        [typeof(AgentDecisionEvent)] = EventTypes.Observability.AGENT_DECISION,
        [typeof(AgentCompletionEvent)] = EventTypes.Observability.AGENT_COMPLETION,
        [typeof(IterationMessagesEvent)] = EventTypes.Observability.ITERATION_MESSAGES,
        [typeof(SchemaChangedEvent)] = EventTypes.Observability.SCHEMA_CHANGED,
        [typeof(CollapsingStateEvent)] = EventTypes.Observability.COLLAPSING_STATE,
        [typeof(EventDroppedEvent)] = EventTypes.Observability.EVENT_DROPPED,
        [typeof(BackgroundOperationStartedEvent)] = EventTypes.Observability.BACKGROUND_OPERATION_STARTED,
        [typeof(BackgroundOperationStatusEvent)] = EventTypes.Observability.BACKGROUND_OPERATION_STATUS,
        [typeof(StructuredOutputErrorEvent)] = EventTypes.Observability.STRUCTURED_OUTPUT_ERROR,
        [typeof(StructuredOutputStartEvent)] = EventTypes.Observability.STRUCTURED_OUTPUT_START,
        [typeof(StructuredOutputPartialEvent)] = EventTypes.Observability.STRUCTURED_OUTPUT_PARTIAL,
        [typeof(StructuredOutputCompleteEvent)] = EventTypes.Observability.STRUCTURED_OUTPUT_COMPLETE,
        [typeof(AssetUploadedEvent)] = EventTypes.Observability.ASSET_UPLOADED,
        [typeof(AssetUploadFailedEvent)] = EventTypes.Observability.ASSET_UPLOAD_FAILED,

        // Priority Streaming Events
        [typeof(InterruptionRequestEvent)] = EventTypes.Streaming.INTERRUPTION_REQUEST,
    };

    /// <summary>
    /// Standard JSON options with source generator for Native AOT.
    /// </summary>
    public static JsonSerializerOptions StandardJsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        TypeInfoResolver = AgentEventJsonContext.Default
    };

    /// <summary>
    /// Serializes an agent event to JSON with version and type fields.
    /// </summary>
    /// <param name="evt">The event to serialize.</param>
    /// <returns>JSON string with standard event format.</returns>
    /// <remarks>
    /// <para>
    /// Output format:
    /// <code>
    /// {
    ///   "version": "1.0",
    ///   "type": "TEXT_DELTA",
    ///   "text": "hello",
    ///   "messageId": "msg-123"
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// The type discriminator uses SCREAMING_SNAKE_CASE convention.
    /// Custom events without explicit mapping use auto-generated names.
    /// </para>
    /// </remarks>
    public static string ToJson(AgentEvent evt)
    {
        return ToJson(evt, "1.0");
    }

    /// <summary>
    /// Serializes an agent event to JSON with specified version.
    /// </summary>
    /// <param name="evt">The event to serialize.</param>
    /// <param name="version">The version string to include.</param>
    /// <returns>JSON string with standard event format.</returns>
    public static string ToJson(AgentEvent evt, string version)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(version);

        // Get type discriminator
        var eventType = TypeNames.TryGetValue(evt.GetType(), out var typeName)
            ? typeName
            : ToScreamingSnakeCase(evt.GetType().Name);


        // Serialize event to JSON
        var eventJson = JsonSerializer.Serialize(evt, evt.GetType(), StandardJsonOptions);

        // Inject version and type fields at the beginning
        // JSON always starts with { so we insert after it
        var prefix = $"\"version\":\"{version}\",\"type\":\"{eventType}\"";

        if (eventJson == "{}")
        {
            // Empty object - just add the fields
            return $"{{{prefix}}}";
        }
        else
        {
            // Insert prefix after opening brace
            return eventJson.Insert(1, prefix + ",");
        }
    }

    /// <summary>
    /// Gets the type discriminator for an event type.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>The SCREAMING_SNAKE_CASE type discriminator.</returns>
    public static string GetEventTypeName(Type eventType)
    {
        return TypeNames.TryGetValue(eventType, out var typeName)
            ? typeName
            : ToScreamingSnakeCase(eventType.Name);
    }

    /// <summary>
    /// Gets the type discriminator for an event instance.
    /// </summary>
    /// <param name="evt">The event instance.</param>
    /// <returns>The SCREAMING_SNAKE_CASE type discriminator.</returns>
    public static string GetEventTypeName(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return GetEventTypeName(evt.GetType());
    }

    /// <summary>
    /// Registers a custom event type with a specific discriminator.
    /// Called by source generator for auto-discovered custom events.
    /// </summary>
    /// <param name="eventType">The event type to register.</param>
    /// <param name="discriminator">The SCREAMING_SNAKE_CASE discriminator.</param>
    internal static void RegisterEventType(Type eventType, string discriminator)
    {
        TypeNames[eventType] = discriminator;
        // Keep reverse lookup in sync
        DiscriminatorToType[discriminator] = eventType;
    }

    // Reverse lookup: SCREAMING_SNAKE_CASE discriminator → concrete event type
    private static readonly Dictionary<string, Type> DiscriminatorToType =
        new(StringComparer.OrdinalIgnoreCase);

    // Initialise reverse lookup from TypeNames at startup
    static AgentEventSerializer()
    {
        foreach (var (type, discriminator) in TypeNames)
        {
            DiscriminatorToType[discriminator] = type;
            // Warm up the STJ source-gen context so every registered type has metadata
            // available before the first Serialize call.
            StandardJsonOptions.TypeInfoResolver?.GetTypeInfo(type, StandardJsonOptions);
        }
    }

    /// <summary>
    /// Deserializes an agent event from a JSON string produced by <see cref="ToJson"/>.
    /// Reads the "type" discriminator field to determine the concrete event type.
    /// Returns null if the discriminator is unknown or the JSON is invalid.
    /// </summary>
    public static AgentEvent? FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                return null;

            var discriminator = typeProp.GetString();
            if (discriminator == null || !DiscriminatorToType.TryGetValue(discriminator, out var concreteType))
                return null;

            // Deserialize directly from the already-parsed JsonElement to avoid a second parse.
            return (AgentEvent?)doc.RootElement.Deserialize(concreteType, StandardJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts PascalCase event name to SCREAMING_SNAKE_CASE.
    /// Used as fallback for custom events without explicit mapping.
    /// </summary>
    /// <param name="pascalCase">The PascalCase name (e.g., "TextDeltaEvent").</param>
    /// <returns>The SCREAMING_SNAKE_CASE name (e.g., "TEXT_DELTA").</returns>
    private static string ToScreamingSnakeCase(string pascalCase)
    {
        // Remove "Event" suffix if present
        if (pascalCase.EndsWith("Event", StringComparison.Ordinal))
            pascalCase = pascalCase[..^5];

        // Insert underscores before capitals and uppercase
        return PascalCaseToSnakeCaseRegex().Replace(pascalCase, "$1_$2").ToUpperInvariant();
    }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex PascalCaseToSnakeCaseRegex();
}
