using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;
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
[JsonSerializable(typeof(AgentMetadata))]
[JsonSerializable(typeof(AgentInputEvent))]
[JsonSerializable(typeof(AgentMessageSource))]
[JsonSerializable(typeof(AgentMessageVisibility))]
[JsonSerializable(typeof(AgentMessagePersistence))]
[JsonSerializable(typeof(UserMessagesInputEvent))]
[JsonSerializable(typeof(CompactThreadInputEvent))]
[JsonSerializable(typeof(ThreadCompactionRequest))]
[JsonSerializable(typeof(BackgroundTaskNotificationInputEvent))]
[JsonSerializable(typeof(ClientTools.ClientToolBackgroundOperationOutcomeState))]
[JsonSerializable(typeof(ClientTools.ClientToolBackgroundOperationOutcomeEvent))]
[JsonSerializable(typeof(BackgroundTaskNotification))]
[JsonSerializable(typeof(ThreadCreatedEvent))]
[JsonSerializable(typeof(ThreadUpdatedEvent))]
[JsonSerializable(typeof(ContentAddedEvent))]
[JsonSerializable(typeof(ThreadMiddlewareStateCommittedEvent))]
[JsonSerializable(typeof(ThreadHistoryCompactionCheckpointEvent))]
[JsonSerializable(typeof(CompactionPointDescriptor))]
[JsonSerializable(typeof(CompactionPreservationDescriptor))]
[JsonSerializable(typeof(CompactionStrategyDescriptor))]
[JsonSerializable(typeof(AgentRunConfig))]
[JsonSerializable(typeof(AgentPermissionMode))]
[JsonSerializable(typeof(CompactionRunPolicy))]
[JsonSerializable(typeof(CompactionSpecification))]
[JsonSerializable(typeof(AutomaticCompactionPolicy))]
[JsonSerializable(typeof(ThreadContextUsage))]
[JsonSerializable(typeof(AudioRunConfig))]
[JsonSerializable(typeof(AudioConfig))]
[JsonSerializable(typeof(AudioInputMode))]
[JsonSerializable(typeof(AudioOutputMode))]
[JsonSerializable(typeof(AudioPolicySet))]
[JsonSerializable(typeof(InputMediaPolicy))]
[JsonSerializable(typeof(InputMediaHandlingMode))]
[JsonSerializable(typeof(InputMediaDisposition))]
[JsonSerializable(typeof(TraceCapturePolicy))]
[JsonSerializable(typeof(PrivacyPolicy))]
[JsonSerializable(typeof(ThreadProjectionPolicy))]
[JsonSerializable(typeof(AssistantOutputSynthesisMode))]
[JsonSerializable(typeof(AssistantAudioArtifactCapturePolicy))]
[JsonSerializable(typeof(TextToSpeechPacingOptions))]
[JsonSerializable(typeof(TextToSpeechFirstSegmentOptions))]
[JsonSerializable(typeof(TextToSpeechContinuationOptions))]
[JsonSerializable(typeof(TextToSpeechBoundaryOptions))]
[JsonSerializable(typeof(TextToSpeechFilteringOptions))]
[JsonSerializable(typeof(TextToSpeechPacingMode))]
[JsonSerializable(typeof(TextToSpeechEmojiPolicy))]
[JsonSerializable(typeof(ProgressiveTextToSpeechRouteMode))]
[JsonSerializable(typeof(PushTextInputAggregationMode))]
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
[JsonSerializable(typeof(ThreadRunStartedEvent))]
[JsonSerializable(typeof(ThreadRunCompletedEvent))]

// Content Events
[JsonSerializable(typeof(TextMessageStartEvent))]
[JsonSerializable(typeof(TextDeltaEvent))]
[JsonSerializable(typeof(TextMessageEndEvent))]
[JsonSerializable(typeof(UserAudioTranscriptDeltaEvent))]
[JsonSerializable(typeof(UserAudioTranscriptCompletedEvent))]
[JsonSerializable(typeof(UserAudioTranscriptFailedEvent))]

// Reasoning Events
[JsonSerializable(typeof(ReasoningMessageStartEvent))]
[JsonSerializable(typeof(ReasoningDeltaEvent))]
[JsonSerializable(typeof(ReasoningMessageEndEvent))]

// Tool Events
[JsonSerializable(typeof(ToolCallStartEvent))]
[JsonSerializable(typeof(ToolCallArgsEvent))]
[JsonSerializable(typeof(ToolCallEndEvent))]
[JsonSerializable(typeof(ToolCallResultEvent))]
[JsonSerializable(typeof(FunctionInvocationSnapshot))]
[JsonSerializable(typeof(ToolInvocationInfo))]
[JsonSerializable(typeof(ToolResultPayload))]
[JsonSerializable(typeof(AgentBackgroundInvocationReceipt))]
[JsonSerializable(typeof(IReadOnlyList<ClientTools.IToolResultContent>), TypeInfoPropertyName = "IReadOnlyListIToolResultContent")]
[JsonSerializable(typeof(List<ClientTools.IToolResultContent>))]
[JsonSerializable(typeof(ToolCallType))]

// Background Task Events
[JsonSerializable(typeof(BackgroundTaskStartedEvent))]
[JsonSerializable(typeof(BackgroundTaskCompletedEvent))]
[JsonSerializable(typeof(BackgroundTaskCancelledEvent))]
[JsonSerializable(typeof(BackgroundTaskFaultedEvent))]
[JsonSerializable(typeof(BackgroundHandleRegisteredEvent))]
[JsonSerializable(typeof(BackgroundHandleStatusChangedEvent))]
[JsonSerializable(typeof(BackgroundHandleKind))]
[JsonSerializable(typeof(BackgroundHandleOperation))]
[JsonSerializable(typeof(BackgroundHandleArtifact))]
[JsonSerializable(typeof(BackgroundHandleSnapshot))]
[JsonSerializable(typeof(BackgroundTaskNotificationQueuedEvent))]
[JsonSerializable(typeof(BackgroundTaskNotificationDeliveredEvent))]
[JsonSerializable(typeof(BackgroundTaskNotificationSuppressedEvent))]
[JsonSerializable(typeof(BackgroundTaskSourceKind))]
[JsonSerializable(typeof(BackgroundTaskNotificationRule))]
[JsonSerializable(typeof(BackgroundTaskNotificationRule.NoneRule))]
[JsonSerializable(typeof(BackgroundTaskNotificationRule.OnFinalStateRule))]
[JsonSerializable(typeof(BackgroundTaskNotificationRule.StrategyRule))]

// Permission Events
[JsonSerializable(typeof(PermissionRequestEvent))]
[JsonSerializable(typeof(PermissionResponseEvent))]
[JsonSerializable(typeof(ContinuationRequestEvent))]
[JsonSerializable(typeof(ContinuationResponseEvent))]

// Clarification Events
[JsonSerializable(typeof(ClarificationRequestEvent))]
[JsonSerializable(typeof(ClarificationResponseEvent))]

// Middleware Events
[JsonSerializable(typeof(MiddlewareErrorEvent))]
[JsonSerializable(typeof(CompactionEvent))]
[JsonSerializable(typeof(CompactionStatus))]
[JsonSerializable(typeof(CompactionStrategy))]
[JsonSerializable(typeof(MaxConsecutiveErrorsExceededEvent))]
[JsonSerializable(typeof(TotalErrorThresholdExceededEvent))]
[JsonSerializable(typeof(PIIDetectedEvent))]
[JsonSerializable(typeof(PIIStrategy))]

// Client Tool Events
[JsonSerializable(typeof(ClientTools.ClientToolInvokeRequestEvent))]
[JsonSerializable(typeof(ClientTools.ClientToolInvokeOutcomeEvent))]
[JsonSerializable(typeof(ClientTools.ClientToolInvokeOutcomeKind))]
[JsonSerializable(typeof(ClientTools.IToolResultContent))]
[JsonSerializable(typeof(ClientTools.TextContent))]
[JsonSerializable(typeof(ClientTools.BinaryContent))]
[JsonSerializable(typeof(ClientTools.JsonContent))]
[JsonSerializable(typeof(ClientTools.ClientToolAugmentation))]

// Thread events removed - threading is now an application-level concern
// Applications should define their own thread event types if needed

// Content Events
[JsonSerializable(typeof(ContentUploadedEvent))]
[JsonSerializable(typeof(ContentUploadFailedEvent))]
[JsonSerializable(typeof(HostedFileUploadedEvent))]
[JsonSerializable(typeof(HostedFileUploadFailedEvent))]
[JsonSerializable(typeof(ContentReferenceResolvedEvent))]
[JsonSerializable(typeof(ContentReferenceResolutionFailedEvent))]
[JsonSerializable(typeof(ContentReferenceResolutionKind))]

// Observability Events
[JsonSerializable(typeof(CollapsedToolsVisibleEvent))]
[JsonSerializable(typeof(ContainerExpandedEvent))]
[JsonSerializable(typeof(ContainerType))]
[JsonSerializable(typeof(PermissionCheckEvent))]
[JsonSerializable(typeof(IterationStartEvent))]
[JsonSerializable(typeof(CircuitBreakerTriggeredEvent))]
[JsonSerializable(typeof(InternalParallelToolExecutionEvent))]
[JsonSerializable(typeof(FunctionRetryEvent))]
[JsonSerializable(typeof(ModelCallRetryEvent))]
[JsonSerializable(typeof(DeltaSendingActivatedEvent))]
[JsonSerializable(typeof(PlanModeActivatedEvent))]
[JsonSerializable(typeof(PlanUpdatedEvent))]
[JsonSerializable(typeof(PlanUpdateType))]
[JsonSerializable(typeof(NestedAgentInvokedEvent))]
[JsonSerializable(typeof(HPD.Events.ResponsePolicy))]
[JsonSerializable(typeof(HPD.Events.RequestVisibility))]
[JsonSerializable(typeof(HPD.Events.ResponderTarget))]
[JsonSerializable(typeof(HPD.Events.RespondStatus))]
[JsonSerializable(typeof(DocumentProcessedEvent))]
[JsonSerializable(typeof(InternalMessagePreparedEvent))]
[JsonSerializable(typeof(RequestEventProcessedEvent))]
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
[JsonSerializable(typeof(CollapsingStateEvent))]
[JsonSerializable(typeof(EventDroppedEvent))]
[JsonSerializable(typeof(ModelBackgroundOperationStartedEvent))]
[JsonSerializable(typeof(ModelBackgroundOperationStatusEvent))]
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
