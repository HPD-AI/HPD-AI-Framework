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
[JsonSerializable(typeof(AgentMetadata))]
[JsonSerializable(typeof(AgentEventCatalog))]
[JsonSerializable(typeof(AgentEventCatalogEntry))]
[JsonSerializable(typeof(AgentMessageSource))]
[JsonSerializable(typeof(AgentMessageVisibility))]
[JsonSerializable(typeof(AgentMessagePersistence))]
[JsonSerializable(typeof(AudioSessionCommand))]
[JsonSerializable(typeof(AudioSessionCommand.Start))]
[JsonSerializable(typeof(AudioSessionCommand.Update))]
[JsonSerializable(typeof(AudioSessionCommand.CommitInputTurn))]
[JsonSerializable(typeof(AudioSessionCommand.SetInputEnabled))]
[JsonSerializable(typeof(AudioSessionCommand.SetOutputEnabled))]
[JsonSerializable(typeof(AudioSessionCommand.InterruptOutput))]
[JsonSerializable(typeof(AudioSessionCommand.Stop))]
[JsonSerializable(typeof(AudioSessionInputResult))]
[JsonSerializable(typeof(AudioSessionStartBindings))]
[JsonSerializable(typeof(AudioSessionStartBinding))]
[JsonSerializable(typeof(ThreadCompactionRequest))]
[JsonSerializable(typeof(ClientTools.ClientToolOperationOutcomeState))]
[JsonSerializable(typeof(CompactionPointDescriptor))]
[JsonSerializable(typeof(CompactionPreservationDescriptor))]
[JsonSerializable(typeof(CompactionStrategyDescriptor))]
[JsonSerializable(typeof(AgentRunConfig))]
[JsonSerializable(typeof(AgentSecurityRunConfig))]
[JsonSerializable(typeof(AgentSandboxRunConfig))]
[JsonSerializable(typeof(AgentApprovalPolicy))]
[JsonSerializable(typeof(AgentSandboxPolicy))]
[JsonSerializable(typeof(AgentSandboxEscapePolicy))]
[JsonSerializable(typeof(Security.AgentCapabilityKind))]
[JsonSerializable(typeof(Security.AgentCapabilityResource))]
[JsonSerializable(typeof(CompactionRunPolicy))]
[JsonSerializable(typeof(CollapsingRunPolicy))]
[JsonSerializable(typeof(ContainerRecoveryHistoryMode))]
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
[JsonSerializable(typeof(ChatClientConfig))]
[JsonSerializable(typeof(RealtimeClientConfig))]
[JsonSerializable(typeof(ImageGenerationClientConfig))]
[JsonSerializable(typeof(EmbeddingsClientConfig))]
[JsonSerializable(typeof(TextToSpeechClientConfig))]
[JsonSerializable(typeof(SpeechToTextClientConfig))]
[JsonSerializable(typeof(HostedFilesClientConfig))]
[JsonSerializable(typeof(VoiceActivityClientConfig))]
[JsonSerializable(typeof(EndOfTurnClientConfig))]
[JsonSerializable(typeof(RealtimeAudioFormatRunConfig))]
[JsonSerializable(typeof(RealtimeTranscriptionRunConfig))]
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

// Agent Turn Events
[JsonSerializable(typeof(ProviderUsageValuation))]
[JsonSerializable(typeof(ProviderUsageValuationInput))]
[JsonSerializable(typeof(ProviderUsageValuationComponent))]
[JsonSerializable(typeof(ProviderUsageUnpricedQuantity))]
[JsonSerializable(typeof(ProviderUsageValuationDiagnostic))]
[JsonSerializable(typeof(ProviderReportedValuationProvenance))]
[JsonSerializable(typeof(ContractValuationProvenance))]
[JsonSerializable(typeof(InvoiceValuationProvenance))]
[JsonSerializable(typeof(AuthorityAttemptValuationProvenance))]
[JsonSerializable(typeof(ProviderUsageMeasurement))]
[JsonSerializable(typeof(MessageTurnUsageSummary))]
[JsonSerializable(typeof(ProviderReportedMonetaryObservation))]
[JsonSerializable(typeof(ThreadExecutionError))]
[JsonSerializable(typeof(SubAgentCreationRecord))]
[JsonSerializable(typeof(SubAgentCreationKey))]
[JsonSerializable(typeof(SubAgentCreationRequest))]
[JsonSerializable(typeof(ThreadForkOperationRecord))]
[JsonSerializable(typeof(ThreadForkResult))]
[JsonSerializable(typeof(SubAgentForkChildOutcome))]
[JsonSerializable(typeof(SubAgentOperationResult))]
[JsonSerializable(typeof(SubAgentActionResult))]
[JsonSerializable(typeof(SubAgentListResult))]
[JsonSerializable(typeof(SubAgentWaitResult))]
[JsonSerializable(typeof(AgentInputResult))]
[JsonSerializable(typeof(AgentInputResult.Completed))]
[JsonSerializable(typeof(AgentInputResult.Steered))]
[JsonSerializable(typeof(AgentInputResult.Control))]
[JsonSerializable(typeof(AgentInputResult.AudioSession))]
[JsonSerializable(typeof(AudioSessionInputResult))]

// Content Events

// Reasoning Events

// Tool Events
[JsonSerializable(typeof(FunctionInvocationSnapshot))]
[JsonSerializable(typeof(ToolInvocationInfo))]
[JsonSerializable(typeof(ToolResultPayload))]
[JsonSerializable(typeof(IReadOnlyList<ClientTools.IToolResultContent>), TypeInfoPropertyName = "IReadOnlyListIToolResultContent")]
[JsonSerializable(typeof(List<ClientTools.IToolResultContent>))]
[JsonSerializable(typeof(ToolCallType))]

// Background Task Events
[JsonSerializable(typeof(AgentOperationNotification))]
[JsonSerializable(typeof(AgentOperationRetentionPolicy))]
[JsonSerializable(typeof(AgentOperationTombstone))]
[JsonSerializable(typeof(AgentTurnCapabilityIdentity))]
[JsonSerializable(typeof(AgentCapabilitySourceRevision))]
[JsonSerializable(typeof(IReadOnlyList<AgentCapabilitySourceRevision>))]
[JsonSerializable(typeof(AgentOperationSnapshot))]
[JsonSerializable(typeof(AgentOperationCompletion))]
[JsonSerializable(typeof(AgentOperationFailure))]
[JsonSerializable(typeof(AgentOperationRecoveryReference))]

// Permission Events

// Clarification Events

// Middleware Events
[JsonSerializable(typeof(CompactionStatus))]
[JsonSerializable(typeof(CompactionStrategy))]
[JsonSerializable(typeof(PIIStrategy))]

// Client Tool Events
[JsonSerializable(typeof(ClientTools.ClientToolInvokeOutcomeKind))]
[JsonSerializable(typeof(ClientTools.IToolResultContent))]
[JsonSerializable(typeof(ClientTools.TextContent))]
[JsonSerializable(typeof(ClientTools.BinaryContent))]
[JsonSerializable(typeof(ClientTools.JsonContent))]
[JsonSerializable(typeof(ClientTools.ClientToolAugmentation))]

// Thread events removed - threading is now an application-level concern
// Applications should define their own thread event types if needed

// Content Events
[JsonSerializable(typeof(ContentReferenceResolutionKind))]

// Observability Events
[JsonSerializable(typeof(ContainerType))]
[JsonSerializable(typeof(HPD.Agent.Planning.AgentPlanData))]
[JsonSerializable(typeof(HPD.Agent.Planning.PlanStepData))]
[JsonSerializable(typeof(HPD.Agent.Planning.PlanStepStatus))]
[JsonSerializable(typeof(PlanUpdateType))]
[JsonSerializable(typeof(HPD.Events.ResponsePolicy))]
[JsonSerializable(typeof(HPD.Events.RequestVisibility))]
[JsonSerializable(typeof(HPD.Events.ResponderTarget))]
[JsonSerializable(typeof(HPD.Events.RespondStatus))]
[JsonSerializable(typeof(ContextMessageSnapshot))]
[JsonSerializable(typeof(ToolContextSnapshot))]
[JsonSerializable(typeof(IReadOnlyList<ContextMessageSnapshot>))]
[JsonSerializable(typeof(List<ContextMessageSnapshot>))]
[JsonSerializable(typeof(IReadOnlyList<ToolContextSnapshot>))]
[JsonSerializable(typeof(List<ToolContextSnapshot>))]
[JsonSerializable(typeof(MiddlewareStateEntrySnapshot))]
[JsonSerializable(typeof(MiddlewareStateChange))]
[JsonSerializable(typeof(IReadOnlyList<MiddlewareStateEntrySnapshot>))]
[JsonSerializable(typeof(List<MiddlewareStateEntrySnapshot>))]
[JsonSerializable(typeof(IReadOnlyList<MiddlewareStateChange>))]
[JsonSerializable(typeof(List<MiddlewareStateChange>))]
[JsonSerializable(typeof(StateScope))]
[JsonSerializable(typeof(FunctionInvocationAuditProjection))]
[JsonSerializable(typeof(FunctionInvocationAuditedEvent))]
[JsonSerializable(typeof(ToolBodyOperationCommittedFailureEvent))]
[JsonSerializable(typeof(OperationExecutionOwnerCleanupFailedEvent))]
[JsonSerializable(typeof(CommittedToolBodyOperation))]

// Channel Routing Enums
[JsonSerializable(typeof(EventChannel))]
[JsonSerializable(typeof(EventDirection))]
[JsonSerializable(typeof(InterruptionSource))]

// Channel Routing Events

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
