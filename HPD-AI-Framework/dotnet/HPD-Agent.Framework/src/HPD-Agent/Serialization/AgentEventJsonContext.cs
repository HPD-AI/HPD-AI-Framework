using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using EventChannel = HPD.Events.EventChannel;
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
[JsonSerializable(typeof(AgentInputEvent))]
[JsonSerializable(typeof(UserTextInputEvent))]
[JsonSerializable(typeof(UserMessagesInputEvent))]
[JsonSerializable(typeof(BranchCreatedEvent))]
[JsonSerializable(typeof(BranchForkedEvent))]
[JsonSerializable(typeof(BranchMetadataUpdatedEvent))]
[JsonSerializable(typeof(BranchTreeUpdatedEvent))]
[JsonSerializable(typeof(MessageStartedEvent))]
[JsonSerializable(typeof(MessageCompletedEvent))]
[JsonSerializable(typeof(ContentAddedEvent))]
[JsonSerializable(typeof(BranchMiddlewareStateCommittedEvent))]
[JsonSerializable(typeof(AgentRunConfig))]
[JsonSerializable(typeof(ChatRunConfig))]
[JsonSerializable(typeof(ReasoningOptions))]
[JsonSerializable(typeof(ChatMessage), TypeInfoPropertyName = "MicrosoftExtensionsAiChatMessage")]
[JsonSerializable(typeof(AIContent), TypeInfoPropertyName = "MicrosoftExtensionsAiAIContent")]
[JsonSerializable(typeof(Microsoft.Extensions.AI.TextContent), TypeInfoPropertyName = "MicrosoftExtensionsAiTextContent")]
[JsonSerializable(typeof(DataContent), TypeInfoPropertyName = "MicrosoftExtensionsAiDataContent")]
[JsonSerializable(typeof(UriContent), TypeInfoPropertyName = "MicrosoftExtensionsAiUriContent")]
[JsonSerializable(typeof(ErrorContent), TypeInfoPropertyName = "MicrosoftExtensionsAiErrorContent")]
[JsonSerializable(typeof(ToolCallContent), TypeInfoPropertyName = "MicrosoftExtensionsAiToolCallContent")]
[JsonSerializable(typeof(ToolResultContent), TypeInfoPropertyName = "MicrosoftExtensionsAiToolResultContent")]
[JsonSerializable(typeof(UsageContent), TypeInfoPropertyName = "MicrosoftExtensionsAiUsageContent")]
[JsonSerializable(typeof(TextReasoningContent), TypeInfoPropertyName = "MicrosoftExtensionsAiTextReasoningContent")]
[JsonSerializable(typeof(ToolApprovalRequestContent), TypeInfoPropertyName = "MicrosoftExtensionsAiToolApprovalRequestContent")]
[JsonSerializable(typeof(ToolApprovalResponseContent), TypeInfoPropertyName = "MicrosoftExtensionsAiToolApprovalResponseContent")]
[JsonSerializable(typeof(ImageGenerationToolCallContent), TypeInfoPropertyName = "MicrosoftExtensionsAiImageGenerationToolCallContent")]
[JsonSerializable(typeof(ImageGenerationToolResultContent), TypeInfoPropertyName = "MicrosoftExtensionsAiImageGenerationToolResultContent")]
[JsonSerializable(typeof(CodeInterpreterToolCallContent), TypeInfoPropertyName = "MicrosoftExtensionsAiCodeInterpreterToolCallContent")]
[JsonSerializable(typeof(CodeInterpreterToolResultContent), TypeInfoPropertyName = "MicrosoftExtensionsAiCodeInterpreterToolResultContent")]
[JsonSerializable(typeof(McpServerToolCallContent), TypeInfoPropertyName = "MicrosoftExtensionsAiMcpServerToolCallContent")]
[JsonSerializable(typeof(McpServerToolResultContent), TypeInfoPropertyName = "MicrosoftExtensionsAiMcpServerToolResultContent")]
[JsonSerializable(typeof(WebSearchToolCallContent), TypeInfoPropertyName = "MicrosoftExtensionsAiWebSearchToolCallContent")]
[JsonSerializable(typeof(HostedFileContent), TypeInfoPropertyName = "MicrosoftExtensionsAiHostedFileContent")]
[JsonSerializable(typeof(HostedVectorStoreContent), TypeInfoPropertyName = "MicrosoftExtensionsAiHostedVectorStoreContent")]
[JsonSerializable(typeof(InputRequestContent), TypeInfoPropertyName = "MicrosoftExtensionsAiInputRequestContent")]
[JsonSerializable(typeof(InputResponseContent), TypeInfoPropertyName = "MicrosoftExtensionsAiInputResponseContent")]
[JsonSerializable(typeof(IReadOnlyList<ChatMessage>), TypeInfoPropertyName = "MicrosoftExtensionsAiChatMessageReadOnlyList")]
[JsonSerializable(typeof(List<ChatMessage>), TypeInfoPropertyName = "MicrosoftExtensionsAiChatMessageList")]

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
[JsonSerializable(typeof(ToolCallBackgroundTaskStartedEvent))]
[JsonSerializable(typeof(ToolCallBackgroundTaskCompletedEvent))]
[JsonSerializable(typeof(ToolCallBackgroundTaskCancelledEvent))]
[JsonSerializable(typeof(ToolCallBackgroundTaskFaultedEvent))]
[JsonSerializable(typeof(FunctionInvocationSnapshot))]
[JsonSerializable(typeof(ToolInvocationInfo))]
[JsonSerializable(typeof(ToolResultPayload))]
[JsonSerializable(typeof(IReadOnlyList<ClientTools.IToolResultContent>), TypeInfoPropertyName = "IReadOnlyListIToolResultContent")]
[JsonSerializable(typeof(List<ClientTools.IToolResultContent>))]
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
[JsonSerializable(typeof(ClientTools.clientHarnessesRegisteredEvent))]
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
[JsonSerializable(typeof(IterationContextSnapshotEvent))]
[JsonSerializable(typeof(ContextMessageSnapshot))]
[JsonSerializable(typeof(ToolContextSnapshot))]
[JsonSerializable(typeof(IReadOnlyList<ContextMessageSnapshot>))]
[JsonSerializable(typeof(List<ContextMessageSnapshot>))]
[JsonSerializable(typeof(IReadOnlyList<ToolContextSnapshot>))]
[JsonSerializable(typeof(List<ToolContextSnapshot>))]
[JsonSerializable(typeof(MiddlewareStateEntrySnapshot))]
[JsonSerializable(typeof(MiddlewareStateSnapshotEvent))]
[JsonSerializable(typeof(MiddlewareStateChange))]
[JsonSerializable(typeof(MiddlewareStateChangedEvent))]
[JsonSerializable(typeof(IReadOnlyList<MiddlewareStateEntrySnapshot>))]
[JsonSerializable(typeof(List<MiddlewareStateEntrySnapshot>))]
[JsonSerializable(typeof(IReadOnlyList<MiddlewareStateChange>))]
[JsonSerializable(typeof(List<MiddlewareStateChange>))]
[JsonSerializable(typeof(StateScope))]
[JsonSerializable(typeof(SchemaChangedEvent))]
[JsonSerializable(typeof(CollapsingStateEvent))]
[JsonSerializable(typeof(EventDroppedEvent))]
[JsonSerializable(typeof(BackgroundOperationStartedEvent))]
[JsonSerializable(typeof(BackgroundOperationStatusEvent))]
[JsonSerializable(typeof(StructuredOutputErrorEvent))]
[JsonSerializable(typeof(StructuredOutputStartEvent))]
[JsonSerializable(typeof(StructuredOutputPartialEvent))]
[JsonSerializable(typeof(StructuredOutputCompleteEvent))]

// Channel Routing Enums
[JsonSerializable(typeof(EventChannel))]
[JsonSerializable(typeof(EventDirection))]
[JsonSerializable(typeof(InterruptionSource))]

// Channel Routing Events
[JsonSerializable(typeof(InterruptionRequestEvent))]
[JsonSerializable(typeof(InterruptionHandledEvent))]

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

public partial class AgentEventJsonContext : JsonSerializerContext { }
