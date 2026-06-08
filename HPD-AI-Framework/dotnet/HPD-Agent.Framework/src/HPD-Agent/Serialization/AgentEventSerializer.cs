using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

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
        // Input Events
        [typeof(UserTextInputEvent)] = EventTypes.Input.USER_TEXT_INPUT,
        [typeof(UserMessagesInputEvent)] = EventTypes.Input.USER_MESSAGES_INPUT,

        // Branch Events
        [typeof(BranchCreatedEvent)] = BranchEventTypes.BranchCreated,
        [typeof(BranchForkedEvent)] = BranchEventTypes.BranchForked,
        [typeof(BranchMetadataUpdatedEvent)] = BranchEventTypes.BranchMetadataUpdated,
        [typeof(BranchTreeUpdatedEvent)] = BranchEventTypes.BranchTreeUpdated,
        [typeof(MessageStartedEvent)] = BranchEventTypes.MessageStarted,
        [typeof(MessageCompletedEvent)] = BranchEventTypes.MessageCompleted,
        [typeof(ContentAddedEvent)] = BranchEventTypes.ContentAdded,
        [typeof(BranchMiddlewareStateCommittedEvent)] = BranchEventTypes.BranchMiddlewareStateCommitted,
        [typeof(BranchHistoryCompactedEvent)] = BranchEventTypes.BranchHistoryCompacted,

        // Message Turn Events
        [typeof(MessageTurnStartedEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_STARTED,
        [typeof(MessageTurnFinishedEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_FINISHED,
        [typeof(MessageTurnErrorEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_ERROR,

        // Agent Turn Events
        [typeof(AgentTurnStartedEvent)] = EventTypes.AgentTurn.AGENT_TURN_STARTED,
        [typeof(AgentTurnFinishedEvent)] = EventTypes.AgentTurn.AGENT_TURN_FINISHED,
        [typeof(StateSnapshotEvent)] = EventTypes.AgentTurn.STATE_SNAPSHOT,
        [typeof(BranchRunStartedEvent)] = EventTypes.AgentTurn.BRANCH_RUN_STARTED,
        [typeof(BranchRunCompletedEvent)] = EventTypes.AgentTurn.BRANCH_RUN_COMPLETED,

        // Content Events
        [typeof(TextMessageStartEvent)] = EventTypes.Content.TEXT_MESSAGE_START,
        [typeof(TextDeltaEvent)] = EventTypes.Content.TEXT_DELTA,
        [typeof(TextMessageEndEvent)] = EventTypes.Content.TEXT_MESSAGE_END,
        [typeof(UserAudioTranscriptDeltaEvent)] = EventTypes.Content.USER_AUDIO_TRANSCRIPT_DELTA,
        [typeof(UserAudioTranscriptCompletedEvent)] = EventTypes.Content.USER_AUDIO_TRANSCRIPT_COMPLETED,
        [typeof(UserAudioTranscriptFailedEvent)] = EventTypes.Content.USER_AUDIO_TRANSCRIPT_FAILED,

        // Reasoning Events
        [typeof(ReasoningMessageStartEvent)] = EventTypes.Reasoning.REASONING_MESSAGE_START,
        [typeof(ReasoningDeltaEvent)] = EventTypes.Reasoning.REASONING_DELTA,
        [typeof(ReasoningMessageEndEvent)] = EventTypes.Reasoning.REASONING_MESSAGE_END,

        // Tool Events
        [typeof(ToolCallStartEvent)] = EventTypes.Tool.TOOL_CALL_START,
        [typeof(ToolCallArgsEvent)] = EventTypes.Tool.TOOL_CALL_ARGS,
        [typeof(ToolCallEndEvent)] = EventTypes.Tool.TOOL_CALL_END,
        [typeof(ToolCallResultEvent)] = EventTypes.Tool.TOOL_CALL_RESULT,
        [typeof(ToolCallBackgroundTaskStartedEvent)] = EventTypes.Tool.TOOL_CALL_BACKGROUND_TASK_STARTED,
        [typeof(ToolCallBackgroundTaskCompletedEvent)] = EventTypes.Tool.TOOL_CALL_BACKGROUND_TASK_COMPLETED,
        [typeof(ToolCallBackgroundTaskCancelledEvent)] = EventTypes.Tool.TOOL_CALL_BACKGROUND_TASK_CANCELLED,
        [typeof(ToolCallBackgroundTaskFaultedEvent)] = EventTypes.Tool.TOOL_CALL_BACKGROUND_TASK_FAULTED,

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

        // Client Tool Events
        [typeof(ClientTools.ClientToolInvokeRequestEvent)] = EventTypes.ClientTool.CLIENT_TOOL_INVOKE_REQUEST,
        [typeof(ClientTools.ClientToolInvokeResponseEvent)] = EventTypes.ClientTool.CLIENT_TOOL_INVOKE_RESPONSE,
        [typeof(ClientTools.clientToolHarnessesRegisteredEvent)] = EventTypes.ClientTool.CLIENT_TOOL_GROUPS_REGISTERED,

        // Middleware Events
        [typeof(MiddlewareErrorEvent)] = EventTypes.Middleware.MIDDLEWARE_ERROR,
        [typeof(CompactionEvent)] = EventTypes.Middleware.COMPACTION,
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
        [typeof(CompactionCacheEvent)] = EventTypes.Observability.COMPACTION_CACHE,
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
        [typeof(IterationContextSnapshotEvent)] = EventTypes.Observability.ITERATION_CONTEXT_SNAPSHOT,
        [typeof(MiddlewareStateSnapshotEvent)] = EventTypes.Observability.MIDDLEWARE_STATE_SNAPSHOT,
        [typeof(MiddlewareStateChangedEvent)] = EventTypes.Observability.MIDDLEWARE_STATE_CHANGED,
        [typeof(SchemaChangedEvent)] = EventTypes.Observability.SCHEMA_CHANGED,
        [typeof(CollapsingStateEvent)] = EventTypes.Observability.COLLAPSING_STATE,
        [typeof(EventDroppedEvent)] = EventTypes.Observability.EVENT_DROPPED,
        [typeof(BackgroundOperationStartedEvent)] = EventTypes.Observability.BACKGROUND_OPERATION_STARTED,
        [typeof(BackgroundOperationStatusEvent)] = EventTypes.Observability.BACKGROUND_OPERATION_STATUS,
        [typeof(StructuredOutputErrorEvent)] = EventTypes.Observability.STRUCTURED_OUTPUT_ERROR,
        [typeof(StructuredOutputStartEvent)] = EventTypes.Observability.STRUCTURED_OUTPUT_START,
        [typeof(StructuredOutputPartialEvent)] = EventTypes.Observability.STRUCTURED_OUTPUT_PARTIAL,
        [typeof(StructuredOutputCompleteEvent)] = EventTypes.Observability.STRUCTURED_OUTPUT_COMPLETE,
        [typeof(ContentUploadedEvent)] = EventTypes.Observability.CONTENT_UPLOADED,
        [typeof(ContentUploadFailedEvent)] = EventTypes.Observability.CONTENT_UPLOAD_FAILED,
        [typeof(HostedFileUploadedEvent)] = EventTypes.Observability.HOSTED_FILE_UPLOADED,
        [typeof(HostedFileUploadFailedEvent)] = EventTypes.Observability.HOSTED_FILE_UPLOAD_FAILED,
        [typeof(ContentReferenceResolvedEvent)] = EventTypes.Observability.CONTENT_REFERENCE_RESOLVED,
        [typeof(ContentReferenceResolutionFailedEvent)] = EventTypes.Observability.CONTENT_REFERENCE_RESOLUTION_FAILED,

        // Priority Streaming Events
        [typeof(InterruptionRequestEvent)] = EventTypes.Streaming.INTERRUPTION_REQUEST,
        [typeof(InterruptionHandledEvent)] = EventTypes.Streaming.INTERRUPTION_HANDLED,
    };

    /// <summary>
    /// Standard JSON options with source generator for Native AOT.
    /// </summary>
    public static JsonSerializerOptions StandardJsonOptions { get; } = CreateStandardJsonOptions();

    private static JsonSerializerOptions CreateStandardJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        options.TypeInfoResolverChain.Add(AgentEventJsonContext.Default);
        options.TypeInfoResolverChain.Add(HPDJsonContext.Default);

        foreach (var resolver in Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)
        {
            if (resolver is not null)
            {
                options.TypeInfoResolverChain.Add(resolver);
            }
        }

        if (RuntimeFeature.IsDynamicCodeSupported)
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        options.AddAIContentType<ImageContent>("hpd:image");
        options.AddAIContentType<AudioContent>("hpd:audio");
        options.AddAIContentType<VideoContent>("hpd:video");
        options.AddAIContentType<DocumentContent>("hpd:document");

        options.MakeReadOnly();
        return options;
    }

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
    /// Serializes an agent input event to JSON with version and type fields.
    /// </summary>
    public static string ToJson(AgentInputEvent input)
    {
        return ToJson(input, "1.0");
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

        return ToJsonEnvelope(evt, evt.GetType(), version);
    }

    /// <summary>
    /// Serializes an agent input event to JSON with specified version.
    /// </summary>
    public static string ToJson(AgentInputEvent input, string version)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(version);

        return ToJsonEnvelope(input, input.GetType(), version);
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
    /// Gets the type discriminator for an input event instance.
    /// </summary>
    public static string GetEventTypeName(AgentInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return GetEventTypeName(input.GetType());
    }

    /// <summary>
    /// Registers a custom event type with a specific discriminator.
    /// Called by source generator for auto-discovered custom events.
    /// </summary>
    /// <param name="eventType">The event type to register.</param>
    /// <param name="discriminator">The SCREAMING_SNAKE_CASE discriminator.</param>
    public static void RegisterEventType(Type eventType, string discriminator, JsonTypeInfo? typeInfo = null)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);

        if (!typeof(AgentEvent).IsAssignableFrom(eventType) && !typeof(AgentInputEvent).IsAssignableFrom(eventType))
            throw new ArgumentException($"Type '{eventType.FullName}' is not an agent event type.", nameof(eventType));

        TypeNames[eventType] = discriminator;
        DiscriminatorToType[discriminator] = eventType;
        if (typeInfo is not null)
            TypeInfos[eventType] = typeInfo;
    }

    // Reverse lookup: SCREAMING_SNAKE_CASE discriminator → concrete event type
    private static readonly Dictionary<string, Type> DiscriminatorToType =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Type, JsonTypeInfo> TypeInfos = new();

    // Initialise reverse lookup from TypeNames at startup
    static AgentEventSerializer()
    {
        foreach (var (type, discriminator) in TypeNames)
        {
            DiscriminatorToType[discriminator] = type;
            // Warm up the STJ source-gen context so every registered type has metadata
            // available before the first Serialize call.
            if (StandardJsonOptions.TypeInfoResolver?.GetTypeInfo(type, StandardJsonOptions) is { } typeInfo)
                TypeInfos[type] = typeInfo;
        }
    }

    /// <summary>
    /// Deserializes an agent wire envelope from JSON.
    /// </summary>
    public static object? FromJson(string json)
    {
        try
        {
            return DeserializeEnvelope(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deserializes an output/observation agent event from JSON.
    /// </summary>
    public static AgentEvent? FromEventJson(string json) => FromJson(json) as AgentEvent;

    /// <summary>
    /// Deserializes an output/observation agent event from JSON and throws when
    /// the payload is not a known agent event.
    /// </summary>
    public static AgentEvent DeserializeEventJson(string json) =>
        DeserializeEnvelope(json) as AgentEvent
        ?? throw new JsonException("JSON payload is not a known agent event.");

    /// <summary>
    /// Deserializes an agent input event from JSON.
    /// </summary>
    public static AgentInputEvent? FromInputJson(string json) => FromJson(json) as AgentInputEvent;

    private static string ToJsonEnvelope(object value, Type concreteType, string version)
    {
        var eventType = TypeNames.TryGetValue(concreteType, out var typeName)
            ? typeName
            : ToScreamingSnakeCase(concreteType.Name);

        var eventJson = JsonSerializer.Serialize(value, GetTypeInfo(concreteType));
        var prefix = $"\"version\":\"{version}\",\"type\":\"{eventType}\"";

        return eventJson == "{}"
            ? $"{{{prefix}}}"
            : eventJson.Insert(1, prefix + ",");
    }

    private static object? DeserializeEnvelope(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            return null;

        var discriminator = typeProp.GetString();
        if (discriminator == null || !DiscriminatorToType.TryGetValue(discriminator, out var concreteType))
            return null;

        return doc.RootElement.Deserialize(GetTypeInfo(concreteType));
    }

    private static JsonTypeInfo GetTypeInfo(Type concreteType)
    {
        if (TypeInfos.TryGetValue(concreteType, out var typeInfo))
            return typeInfo;

        typeInfo = StandardJsonOptions.TypeInfoResolver?.GetTypeInfo(concreteType, StandardJsonOptions)
            ?? throw new JsonException($"No JSON metadata registered for event type '{concreteType.FullName}'.");
        TypeInfos[concreteType] = typeInfo;
        return typeInfo;
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
