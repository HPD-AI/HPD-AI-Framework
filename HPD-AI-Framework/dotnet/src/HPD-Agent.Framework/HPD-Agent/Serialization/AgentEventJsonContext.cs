using System.Text.Json;
using System.Text.Json.Serialization;
using EventPriority = HPD.Events.EventPriority;
using EventDirection = HPD.Events.EventDirection;

namespace HPD.Agent.Serialization;

/// <summary>
/// Source generator context for Native AOT compatible event serialization.
/// All framework event types must be registered here for proper serialization.
/// </summary>
/// <remarks>
/// <para>
/// This context uses System.Text.Json source generation for:
/// - Zero reflection overhead
/// - Faster startup time
/// - Smaller binary size
/// - Native AOT compatibility
/// </para>
/// <para>
/// Custom events defined by users are auto-registered via the CustomEventSourceGenerator.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
// Base types
[JsonSerializable(typeof(AgentEvent))]
[JsonSerializable(typeof(AgentExecutionContext))]

// Message Turn Events
[JsonSerializable(typeof(MessageTurnStartedEvent))]
[JsonSerializable(typeof(MessageTurnFinishedEvent))]
[JsonSerializable(typeof(MessageTurnErrorEvent))]

// Agent Turn Events
[JsonSerializable(typeof(AgentTurnStartedEvent))]
[JsonSerializable(typeof(AgentTurnFinishedEvent))]
[JsonSerializable(typeof(StateSnapshotEvent))]

// Content Events
[JsonSerializable(typeof(TextMessageStartEvent))]
[JsonSerializable(typeof(TextDeltaEvent))]
[JsonSerializable(typeof(TextMessageEndEvent))]

// Reasoning Events
[JsonSerializable(typeof(ReasoningMessageStartEvent))]
[JsonSerializable(typeof(ReasoningDeltaEvent))]
[JsonSerializable(typeof(ReasoningMessageEndEvent))]

// Tool Events
[JsonSerializable(typeof(ToolCallStartEvent))]
[JsonSerializable(typeof(ToolCallArgsEvent))]
[JsonSerializable(typeof(ToolCallEndEvent))]
[JsonSerializable(typeof(ToolCallResultEvent))]
[JsonSerializable(typeof(ToolCallType))]

// Permission Events
[JsonSerializable(typeof(PermissionRequestEvent))]
[JsonSerializable(typeof(PermissionResponseEvent))]
[JsonSerializable(typeof(PermissionApprovedEvent))]
[JsonSerializable(typeof(PermissionDeniedEvent))]
[JsonSerializable(typeof(ContinuationRequestEvent))]
[JsonSerializable(typeof(ContinuationResponseEvent))]

// Clarification Events
[JsonSerializable(typeof(ClarificationRequestEvent))]
[JsonSerializable(typeof(ClarificationResponseEvent))]

// Middleware Events
[JsonSerializable(typeof(MiddlewareErrorEvent))]
[JsonSerializable(typeof(HistoryReductionEvent))]
[JsonSerializable(typeof(HistoryReductionStatus))]
[JsonSerializable(typeof(HistoryReductionStrategy))]
[JsonSerializable(typeof(MaxConsecutiveErrorsExceededEvent))]
[JsonSerializable(typeof(TotalErrorThresholdExceededEvent))]
[JsonSerializable(typeof(PIIDetectedEvent))]
[JsonSerializable(typeof(PIIStrategy))]

// Client Tool Events
[JsonSerializable(typeof(ClientTools.ClientToolInvokeRequestEvent))]
[JsonSerializable(typeof(ClientTools.ClientToolInvokeResponseEvent))]
[JsonSerializable(typeof(ClientTools.clientToolKitsRegisteredEvent))]
[JsonSerializable(typeof(ClientTools.IToolResultContent))]
[JsonSerializable(typeof(ClientTools.TextContent))]
[JsonSerializable(typeof(ClientTools.BinaryContent))]
[JsonSerializable(typeof(ClientTools.JsonContent))]
[JsonSerializable(typeof(ClientTools.ClientToolAugmentation))]

// Branch events removed - branching is now an application-level concern
// Applications should define their own branch event types if needed

// Asset Events
[JsonSerializable(typeof(AssetUploadedEvent))]
[JsonSerializable(typeof(AssetUploadFailedEvent))]

// Observability Events
[JsonSerializable(typeof(CollapsedToolsVisibleEvent))]
[JsonSerializable(typeof(ContainerExpandedEvent))]
[JsonSerializable(typeof(ContainerType))]
[JsonSerializable(typeof(PermissionCheckEvent))]
[JsonSerializable(typeof(IterationStartEvent))]
[JsonSerializable(typeof(CircuitBreakerTriggeredEvent))]
[JsonSerializable(typeof(HistoryReductionCacheEvent))]
[JsonSerializable(typeof(CheckpointEvent))]
[JsonSerializable(typeof(CheckpointOperation))]
[JsonSerializable(typeof(InternalParallelToolExecutionEvent))]
[JsonSerializable(typeof(InternalRetryEvent))]
[JsonSerializable(typeof(RetryStatus))]
[JsonSerializable(typeof(FunctionRetryEvent))]
[JsonSerializable(typeof(ModelCallRetryEvent))]
[JsonSerializable(typeof(DeltaSendingActivatedEvent))]
[JsonSerializable(typeof(PlanModeActivatedEvent))]
[JsonSerializable(typeof(PlanUpdatedEvent))]
[JsonSerializable(typeof(PlanUpdateType))]
[JsonSerializable(typeof(NestedAgentInvokedEvent))]
[JsonSerializable(typeof(DocumentProcessedEvent))]
[JsonSerializable(typeof(InternalMessagePreparedEvent))]
[JsonSerializable(typeof(BidirectionalEventProcessedEvent))]
[JsonSerializable(typeof(AgentDecisionEvent))]
[JsonSerializable(typeof(AgentCompletionEvent))]
[JsonSerializable(typeof(IterationMessagesEvent))]
[JsonSerializable(typeof(SchemaChangedEvent))]
[JsonSerializable(typeof(CollapsingStateEvent))]
[JsonSerializable(typeof(EventDroppedEvent))]
[JsonSerializable(typeof(BackgroundOperationStartedEvent))]
[JsonSerializable(typeof(BackgroundOperationStatusEvent))]
[JsonSerializable(typeof(StructuredOutputErrorEvent))]
[JsonSerializable(typeof(StructuredOutputStartEvent))]
[JsonSerializable(typeof(StructuredOutputPartialEvent))]
[JsonSerializable(typeof(StructuredOutputCompleteEvent))]

// Priority Streaming Enums
[JsonSerializable(typeof(EventPriority))]
[JsonSerializable(typeof(EventDirection))]
[JsonSerializable(typeof(InterruptionSource))]

// Priority Streaming Events
[JsonSerializable(typeof(InterruptionRequestEvent))]

// Common types for IDictionary<string, object?> serialization (e.g., PermissionRequestEvent.Arguments)
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(IDictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(object))]

internal partial class AgentEventJsonContext : JsonSerializerContext { }
