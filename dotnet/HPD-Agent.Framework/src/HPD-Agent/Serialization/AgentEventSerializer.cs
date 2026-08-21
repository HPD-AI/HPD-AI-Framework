using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
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
    private static readonly object RegistrationLock = new();

    /// <summary>
    /// Type name to SCREAMING_SNAKE_CASE discriminator mapping.
    /// Framework events are pre-registered; custom events are auto-added by source generator.
    /// </summary>
    private static readonly Dictionary<Type, string> TypeNames = new()
    {
        // Input Events
        [typeof(UserMessagesInputEvent)] = EventTypes.Input.USER_MESSAGES_INPUT,
        [typeof(CompactThreadInputEvent)] = EventTypes.Input.COMPACT_THREAD_INPUT,
        [typeof(AgentOperationNotificationInputEvent)] = EventTypes.Input.AGENT_OPERATION_NOTIFICATION_INPUT,
        [typeof(ClientTools.ClientToolOperationOutcomeEvent)] = EventTypes.ClientTool.CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME,

        // Thread Events
        [typeof(ThreadCreatedEvent)] = ThreadEventTypes.ThreadCreated,
        [typeof(ThreadUpdatedEvent)] = ThreadEventTypes.ThreadUpdated,
        [typeof(ContentAddedEvent)] = ThreadEventTypes.ContentAdded,
        [typeof(ThreadMiddlewareStateCommittedEvent)] = ThreadEventTypes.ThreadMiddlewareStateCommitted,
        [typeof(ThreadHistoryCompactionCheckpointEvent)] = ThreadEventTypes.ThreadHistoryCompactionCheckpoint,

        // Message Turn Events
        [typeof(MessageTurnStartedEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_STARTED,
        [typeof(MessageTurnFinishedEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_FINISHED,
        [typeof(MessageTurnErrorEvent)] = EventTypes.MessageTurn.MESSAGE_TURN_ERROR,

        // Agent Turn Events
        [typeof(AgentTurnStartedEvent)] = EventTypes.AgentTurn.AGENT_TURN_STARTED,
        [typeof(AgentTurnFinishedEvent)] = EventTypes.AgentTurn.AGENT_TURN_FINISHED,
        [typeof(ProviderOperationUsageEvent)] = EventTypes.AgentTurn.PROVIDER_OPERATION_USAGE,
        [typeof(ProviderValuationObservationEvent)] = EventTypes.AgentTurn.PROVIDER_VALUATION_OBSERVATION,
        [typeof(StateSnapshotEvent)] = EventTypes.AgentTurn.STATE_SNAPSHOT,
        [typeof(ThreadExecutionStartedEvent)] = EventTypes.AgentTurn.THREAD_EXECUTION_STARTED,
        [typeof(ThreadExecutionFinishedEvent)] = EventTypes.AgentTurn.THREAD_EXECUTION_FINISHED,
        [typeof(SubAgentInvocationStartedEvent)] = EventTypes.AgentTurn.SUBAGENT_INVOCATION_STARTED,
        [typeof(SubAgentInvocationCompletedEvent)] = EventTypes.AgentTurn.SUBAGENT_INVOCATION_COMPLETED,
        [typeof(SubAgentInvocationFailedEvent)] = EventTypes.AgentTurn.SUBAGENT_INVOCATION_FAILED,
        [typeof(SubAgentInvocationCancelledEvent)] = EventTypes.AgentTurn.SUBAGENT_INVOCATION_CANCELLED,

        // Content Events
        [typeof(TextMessageStartEvent)] = EventTypes.Content.TEXT_MESSAGE_START,
        [typeof(TextDeltaEvent)] = EventTypes.Content.TEXT_DELTA,
        [typeof(TextMessageEndEvent)] = EventTypes.Content.TEXT_MESSAGE_END,
        [typeof(ThreadMessageReplacedEvent)] = EventTypes.Content.THREAD_MESSAGE_REPLACED,
        [typeof(UserMessageEvent)] = EventTypes.Content.USER_MESSAGE,
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

        // Background Task Events
        [typeof(AgentOperationRegisteredEvent)] = EventTypes.Operation.AGENT_OPERATION_REGISTERED,
        [typeof(AgentOperationTransitionedEvent)] = EventTypes.Operation.AGENT_OPERATION_TRANSITIONED,
        [typeof(AgentOperationNotificationQueuedEvent)] = EventTypes.Operation.AGENT_OPERATION_NOTIFICATION_QUEUED,
        [typeof(AgentOperationNotificationDeliveredEvent)] = EventTypes.Operation.AGENT_OPERATION_NOTIFICATION_DELIVERED,
        [typeof(AgentOperationNotificationSuppressedEvent)] = EventTypes.Operation.AGENT_OPERATION_NOTIFICATION_SUPPRESSED,
        [typeof(AgentOperationTombstonedEvent)] = EventTypes.Operation.AGENT_OPERATION_TOMBSTONED,
        [typeof(AgentOperationTombstoneEvictedEvent)] = EventTypes.Operation.AGENT_OPERATION_TOMBSTONE_EVICTED,
        [typeof(AgentCapabilityRefreshStartedEvent)] = EventTypes.Capability.AGENT_CAPABILITY_REFRESH_STARTED,
        [typeof(AgentCapabilityRefreshPublishedEvent)] = EventTypes.Capability.AGENT_CAPABILITY_REFRESH_PUBLISHED,
        [typeof(AgentCapabilityRefreshRejectedEvent)] = EventTypes.Capability.AGENT_CAPABILITY_REFRESH_REJECTED,
        [typeof(AgentTurnCapabilitiesPinnedEvent)] = EventTypes.Capability.AGENT_TURN_CAPABILITIES_PINNED,

        // Permission Events
        [typeof(PermissionRequestEvent)] = EventTypes.Permission.PERMISSION_REQUEST,
        [typeof(PermissionResponseEvent)] = EventTypes.Permission.PERMISSION_RESPONSE,
        [typeof(ContinuationRequestEvent)] = EventTypes.Permission.CONTINUATION_REQUEST,
        [typeof(ContinuationResponseEvent)] = EventTypes.Permission.CONTINUATION_RESPONSE,
        [typeof(Security.AgentCapabilityRequestEvent)] = "AGENT_CAPABILITY_REQUEST",
        [typeof(Security.AgentCapabilityResponseEvent)] = "AGENT_CAPABILITY_RESPONSE",

        // Clarification Events
        [typeof(ClarificationRequestEvent)] = EventTypes.Clarification.CLARIFICATION_REQUEST,
        [typeof(ClarificationResponseEvent)] = EventTypes.Clarification.CLARIFICATION_RESPONSE,

        // Client Tool Events
        [typeof(ClientTools.ClientToolInvokeRequestEvent)] = EventTypes.ClientTool.CLIENT_TOOL_INVOKE_REQUEST,
        [typeof(ClientTools.ClientToolInvokeOutcomeEvent)] = EventTypes.ClientTool.CLIENT_TOOL_INVOKE_OUTCOME,

        // Agent request lifecycle
        [typeof(AgentRequestTerminatedEvent)] = "AGENT_REQUEST_TERMINATED",

        // Middleware Events
        [typeof(MiddlewareErrorEvent)] = EventTypes.Middleware.MIDDLEWARE_ERROR,
        [typeof(CompactionEvent)] = EventTypes.Middleware.COMPACTION,
        [typeof(MaxConsecutiveErrorsExceededEvent)] = EventTypes.Middleware.MAX_CONSECUTIVE_ERRORS_EXCEEDED,
        [typeof(TotalErrorThresholdExceededEvent)] = EventTypes.Middleware.TOTAL_ERROR_THRESHOLD_EXCEEDED,
        [typeof(PIIDetectedEvent)] = EventTypes.Middleware.PII_DETECTED,

        // Thread events removed - threading is now an application-level concern

        // Request Lifecycle Events

        // Observability Events
        [typeof(CollapsedToolsVisibleEvent)] = EventTypes.Observability.COLLAPSED_TOOLS_VISIBLE,
        [typeof(ContainerExpandedEvent)] = EventTypes.Observability.CONTAINER_EXPANDED,
        [typeof(PermissionCheckEvent)] = EventTypes.Observability.PERMISSION_CHECK,
        [typeof(IterationStartEvent)] = EventTypes.Observability.ITERATION_START,
        [typeof(CircuitBreakerTriggeredEvent)] = EventTypes.Observability.CIRCUIT_BREAKER_TRIGGERED,
        [typeof(InternalParallelToolExecutionEvent)] = EventTypes.Observability.INTERNAL_PARALLEL_TOOL_EXECUTION,
        [typeof(FunctionRetryEvent)] = EventTypes.Observability.FUNCTION_RETRY,
        [typeof(ModelCallRetryEvent)] = EventTypes.Observability.MODEL_CALL_RETRY,
        [typeof(DeltaSendingActivatedEvent)] = EventTypes.Observability.DELTA_SENDING_ACTIVATED,
        [typeof(PlanUpdatedEvent)] = EventTypes.Observability.PLAN_UPDATED,
        [typeof(NestedAgentInvokedEvent)] = EventTypes.Observability.NESTED_AGENT_INVOKED,
        [typeof(DocumentProcessedEvent)] = EventTypes.Observability.DOCUMENT_PROCESSED,
        [typeof(InternalMessagePreparedEvent)] = EventTypes.Observability.INTERNAL_MESSAGE_PREPARED,
        [typeof(RequestEventProcessedEvent)] = EventTypes.Observability.REQUEST_EVENT_PROCESSED,
        [typeof(AgentDecisionEvent)] = EventTypes.Observability.AGENT_DECISION,
        [typeof(AgentCompletionEvent)] = EventTypes.Observability.AGENT_COMPLETION,
        [typeof(IterationContextSnapshotEvent)] = EventTypes.Observability.ITERATION_CONTEXT_SNAPSHOT,
        [typeof(MiddlewareStateSnapshotEvent)] = EventTypes.Observability.MIDDLEWARE_STATE_SNAPSHOT,
        [typeof(MiddlewareStateChangedEvent)] = EventTypes.Observability.MIDDLEWARE_STATE_CHANGED,
        [typeof(CollapsingStateEvent)] = EventTypes.Observability.COLLAPSING_STATE,
        [typeof(EventDroppedEvent)] = EventTypes.Observability.EVENT_DROPPED,
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
        var options = HpdAgentJsonUtilities.CreateDefaultOptions();
        options.TypeInfoResolverChain.Insert(0, AgentEventJsonContext.Default);

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

    /// <summary>Serializes an input event and its typed provider run configuration.</summary>
    public static string ToJson(AgentInputEvent input, ProviderComposition providerComposition)
    {
        ArgumentNullException.ThrowIfNull(providerComposition);
        return ToJson(input, providerComposition, "1.0");
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

    /// <summary>Serializes an input event and its typed provider run configuration with a version.</summary>
    public static string ToJson(
        AgentInputEvent input,
        ProviderComposition providerComposition,
        string version)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providerComposition);
        var root = JsonNode.Parse(ToJson(input, version)) as JsonObject
            ?? throw new JsonException("Agent input event did not serialize to a JSON object.");
        if (input.RunConfig is not null)
        {
            root["runConfig"] = JsonNode.Parse(
                HpdAgentConfigSerializer.Serialize(input.RunConfig, providerComposition));
        }
        return root.ToJsonString(AgentEventJsonContext.Default.Options);
    }

    /// <summary>
    /// Gets the type discriminator for an event type.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>The SCREAMING_SNAKE_CASE type discriminator.</returns>
    public static string GetEventTypeName(Type eventType)
    {
        lock (RegistrationLock)
        {
            return TypeNames.TryGetValue(eventType, out var typeName)
                ? typeName
                : ToScreamingSnakeCase(eventType.Name);
        }
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

        if (typeInfo is not null)
        {
            if (typeInfo.Type != eventType)
            {
                throw new ArgumentException(
                    $"JSON metadata for '{typeInfo.Type.FullName}' cannot be registered for event type '{eventType.FullName}'.",
                    nameof(typeInfo));
            }

            var reservedProperty = typeInfo.Properties.FirstOrDefault(static property =>
                property.Name is "version" or "type");
            if (reservedProperty is not null)
            {
                throw new ArgumentException(
                    $"Event type '{eventType.FullName}' declares JSON property '{reservedProperty.Name}', " +
                    "which is reserved by the agent event envelope.",
                    nameof(eventType));
            }
        }

        lock (RegistrationLock)
        {
            if (TypeNames.TryGetValue(eventType, out var existingDiscriminator) &&
                !existingDiscriminator.Equals(discriminator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Event type '{eventType.FullName}' is already registered as '{existingDiscriminator}' and cannot be registered as '{discriminator}'.");
            }

            if (DiscriminatorToType.TryGetValue(discriminator, out var existingType) && existingType != eventType)
            {
                throw new InvalidOperationException(
                    $"Event discriminator '{discriminator}' is already registered for '{existingType.FullName}' and cannot be registered for '{eventType.FullName}'.");
            }

            // Update all maps under one lock so readers never observe a partial registration.
            var canonicalDiscriminator = existingDiscriminator ?? discriminator;
            TypeNames[eventType] = canonicalDiscriminator;
            DiscriminatorToType[canonicalDiscriminator] = eventType;
            if (typeInfo is not null)
                TypeInfos[eventType] = typeInfo;
        }
    }

    /// <summary>
    /// Returns an immutable snapshot of one event registration for diagnostics and tests.
    /// </summary>
    public static bool TryGetEventTypeRegistration(Type eventType, out EventTypeRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        lock (RegistrationLock)
        {
            if (!TypeNames.TryGetValue(eventType, out var discriminator))
            {
                registration = default;
                return false;
            }

            TypeInfos.TryGetValue(eventType, out var typeInfo);
            registration = new EventTypeRegistration(eventType, discriminator, typeInfo);
            return true;
        }
    }

    // Reverse lookup: SCREAMING_SNAKE_CASE discriminator → concrete event type
    private static readonly Dictionary<string, Type> DiscriminatorToType =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Type, JsonTypeInfo> TypeInfos = new();

    // Initialise reverse lookup from TypeNames at startup
    static AgentEventSerializer()
    {
        lock (RegistrationLock)
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

    /// <summary>Deserializes an input event and binds its typed provider run configuration.</summary>
    public static AgentInputEvent? FromInputJson(
        string json,
        ProviderComposition providerComposition)
    {
        ArgumentNullException.ThrowIfNull(providerComposition);
        var input = FromInputJson(json);
        if (input is null)
            return null;

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("runConfig", out var runConfig) ||
            runConfig.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return input;
        }

        return input with
        {
            RunConfig = HpdAgentConfigSerializer.DeserializeRunConfig(
                runConfig.GetRawText(), providerComposition)
        };
    }

    private static string ToJsonEnvelope(object value, Type concreteType, string version)
    {
        var eventType = GetEventTypeName(concreteType);

        var eventJson = JsonSerializer.Serialize(value, GetTypeInfo(concreteType));
        var prefix = $"\"version\":\"{version}\",\"type\":\"{eventType}\"";
        if (value is IErrorEvent errorEvent)
        {
            prefix += ",\"isError\":true";
            if (!eventJson.Contains("\"errorMessage\"", StringComparison.Ordinal))
            {
                prefix += $",\"errorMessage\":{JsonSerializer.Serialize(errorEvent.ErrorMessage, AgentEventJsonContext.Default.String)}";
            }
        }

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
        Type? concreteType;
        lock (RegistrationLock)
        {
            if (discriminator == null || !DiscriminatorToType.TryGetValue(discriminator, out concreteType))
                return null;
        }
        if (concreteType is null)
            return null;

        var typeInfo = GetTypeInfo(concreteType);
        using var payload = StripEnvelopeFields(doc.RootElement, typeInfo);
        return payload.RootElement.Deserialize(typeInfo);
    }

    private static JsonDocument StripEnvelopeFields(JsonElement root, JsonTypeInfo typeInfo)
    {
        var knownProperties = typeInfo.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("version") || property.NameEquals("type"))
                    continue;

                if (!knownProperties.Contains(property.Name))
                    continue;

                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray());
    }

    private static JsonTypeInfo GetTypeInfo(Type concreteType)
    {
        lock (RegistrationLock)
        {
            if (TypeInfos.TryGetValue(concreteType, out var registeredTypeInfo))
                return registeredTypeInfo;
        }

        var typeInfo = StandardJsonOptions.TypeInfoResolver?.GetTypeInfo(concreteType, StandardJsonOptions)
            ?? throw new JsonException($"No JSON metadata registered for event type '{concreteType.FullName}'.");
        lock (RegistrationLock)
        {
            if (TypeInfos.TryGetValue(concreteType, out var registeredTypeInfo))
                return registeredTypeInfo;
            TypeInfos[concreteType] = typeInfo;
            return typeInfo;
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
