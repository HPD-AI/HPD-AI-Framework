using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Middleware;
using HPD.Agent.StructuredOutput;
using HPD.Events;

namespace HPD.Agent.Hosting.Serialization;

/// <summary>
/// Source-generated JSON serialization context for all HPD-Agent hosting DTOs.
/// Enables Native AOT compilation by eliminating runtime reflection.
///
/// Used by:
/// - ErrorResponses (in AspNetCore) for HTTP error responses during data access operations
/// - SseEventHandler streaming for agent events (via chain in AspNetCore JsonOptionsSetup)
/// - Minimal API endpoints that return these DTOs
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
// Session DTOs
[JsonSerializable(typeof(SessionDto))]
[JsonSerializable(typeof(SessionDto[]))]
[JsonSerializable(typeof(List<SessionDto>))]
// Thread DTOs
[JsonSerializable(typeof(ThreadDto))]
[JsonSerializable(typeof(ThreadDto[]))]
[JsonSerializable(typeof(List<ThreadDto>))]
[JsonSerializable(typeof(ThreadRunDto))]
[JsonSerializable(typeof(ThreadRunDto[]))]
[JsonSerializable(typeof(List<ThreadRunDto>))]
[JsonSerializable(typeof(ThreadRunErrorDto))]
[JsonSerializable(typeof(ThreadRunModelBackgroundOperationDto))]
[JsonSerializable(typeof(ThreadRunBackgroundTaskDto))]
[JsonSerializable(typeof(List<ThreadRunBackgroundTaskDto>))]
[JsonSerializable(typeof(ThreadRunBackgroundHandleDto))]
[JsonSerializable(typeof(List<ThreadRunBackgroundHandleDto>))]
// Thread event DTOs
[JsonSerializable(typeof(AgentEvent))]
[JsonSerializable(typeof(AgentEvent[]))]
[JsonSerializable(typeof(List<AgentEvent>))]
// Content DTOs
[JsonSerializable(typeof(ContentDto))]
[JsonSerializable(typeof(ContentDto[]))]
[JsonSerializable(typeof(List<ContentDto>))]
// Request DTOs
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(UpdateSessionRequest))]
[JsonSerializable(typeof(SearchSessionsRequest))]
[JsonSerializable(typeof(CreateThreadRequest))]
[JsonSerializable(typeof(UpdateThreadRequest))]
[JsonSerializable(typeof(ForkThreadRequest))]
[JsonSerializable(typeof(StreamTextRequest))]
[JsonSerializable(typeof(ContextUsageRequest))]
[JsonSerializable(typeof(ThreadContextUsage))]
[JsonSerializable(typeof(ThreadHistoryCompactionCheckpointEvent))]
// Run config DTO graph used by StreamTextRequest
[JsonSerializable(typeof(AgentRunConfig))]
[JsonSerializable(typeof(CompactionConfig))]
[JsonSerializable(typeof(CompactionRunPolicy))]
[JsonSerializable(typeof(AutomaticCompactionPolicy))]
[JsonSerializable(typeof(ThreadCompactionRequest))]
[JsonSerializable(typeof(CompactThreadInputEvent))]
[JsonSerializable(typeof(CompactionTrigger))]
[JsonSerializable(typeof(TurnCountCompactionTrigger))]
[JsonSerializable(typeof(InputTokenCompactionTrigger))]
[JsonSerializable(typeof(ContextPercentageCompactionTrigger))]
[JsonSerializable(typeof(CompactionPoint))]
[JsonSerializable(typeof(CompactAtCurrentHead))]
[JsonSerializable(typeof(CompactAtMessage))]
[JsonSerializable(typeof(CompactAtTurn))]
[JsonSerializable(typeof(CompactionPreservation))]
[JsonSerializable(typeof(PreserveNoPreviousHistory))]
[JsonSerializable(typeof(PreservePreviousTurns))]
[JsonSerializable(typeof(PreservePreviousUserMessages))]
[JsonSerializable(typeof(PreviousHistoryLimit))]
[JsonSerializable(typeof(PreviousItemCountLimit))]
[JsonSerializable(typeof(PreviousTokenBudgetLimit))]
[JsonSerializable(typeof(CompactionStrategy))]
[JsonSerializable(typeof(RemovalCompaction))]
[JsonSerializable(typeof(SummarizingCompaction))]
[JsonSerializable(typeof(CompactionSpecification))]
[JsonSerializable(typeof(ThreadForkCompaction))]
[JsonSerializable(typeof(InheritThreadForkCompaction))]
[JsonSerializable(typeof(DisableThreadForkCompaction))]
[JsonSerializable(typeof(ApplyThreadForkCompaction))]
[JsonSerializable(typeof(ThreadJournalCursor))]
[JsonSerializable(typeof(CompactionCommitMode))]
[JsonSerializable(typeof(AgentModelTransportMode))]
[JsonSerializable(typeof(AgentPermissionMode))]
[JsonSerializable(typeof(AgentClientConfig))]
[JsonSerializable(typeof(ClientProviderConfig))]
[JsonSerializable(typeof(AudioRunConfig))]
[JsonSerializable(typeof(ChatRunConfig))]
[JsonSerializable(typeof(UploadStrategy))]
[JsonSerializable(typeof(CompactionContinuation))]
[JsonSerializable(typeof(StructuredOutputOptions))]
[JsonSerializable(typeof(AgentClientInput))]
[JsonSerializable(typeof(ClientToolDefinition))]
[JsonSerializable(typeof(IToolResultContent))]
[JsonSerializable(typeof(IReadOnlyList<IToolResultContent>))]
[JsonSerializable(typeof(List<IToolResultContent>))]
[JsonSerializable(typeof(TextContent))]
[JsonSerializable(typeof(BinaryContent))]
[JsonSerializable(typeof(JsonContent))]
[JsonSerializable(typeof(ClientToolInvokeOutcomeKind))]
[JsonSerializable(typeof(ClientToolInvokeRequestEvent))]
[JsonSerializable(typeof(ClientToolInvokeOutcomeEvent))]
[JsonSerializable(typeof(ClientToolBackgroundOperationOutcomeState))]
[JsonSerializable(typeof(ClientToolBackgroundOperationOutcomeEvent))]
[JsonSerializable(typeof(ClientAppProviderReference))]
[JsonSerializable(typeof(ClientAppProviderReference[]))]
[JsonSerializable(typeof(List<ClientAppProviderReference>))]
[JsonSerializable(typeof(ClientProviderSelector))]
[JsonSerializable(typeof(ClientToolHarnessSelector))]
[JsonSerializable(typeof(IReadOnlyList<ClientToolHarnessSelector>))]
[JsonSerializable(typeof(ClientAppProviderBindingPolicy))]
[JsonSerializable(typeof(ClientAppProviderDescriptor))]
[JsonSerializable(typeof(ClientToolProviderIdentity))]
[JsonSerializable(typeof(ClientToolProviderContext))]
[JsonSerializable(typeof(ClientToolProviderReadiness))]
[JsonSerializable(typeof(ClientToolProviderConnectionState))]
[JsonSerializable(typeof(ClientToolProviderBindingLeaseStatus))]
[JsonSerializable(typeof(ClientToolProviderManifest))]
[JsonSerializable(typeof(ClientToolProviderSnapshot))]
[JsonSerializable(typeof(ClientToolProviderConnectionRegistration))]
[JsonSerializable(typeof(ClientToolProviderBindingScope))]
[JsonSerializable(typeof(ClientToolProviderBindingLease))]
[JsonSerializable(typeof(ClientToolProviderBindingResult))]
[JsonSerializable(typeof(ClientToolProviderQuery))]
[JsonSerializable(typeof(ClientToolProviderHelloMessage))]
[JsonSerializable(typeof(ClientToolProviderWelcomeMessage))]
[JsonSerializable(typeof(ClientToolProviderManifestMessage))]
[JsonSerializable(typeof(ClientToolProviderHeartbeatMessage))]
[JsonSerializable(typeof(ClientToolProviderReleaseMessage))]
[JsonSerializable(typeof(ClientToolProviderErrorMessage))]
[JsonSerializable(typeof(ClientToolProviderInvokeToolMessage))]
[JsonSerializable(typeof(ClientToolProviderInvokeOutcomeMessage))]
[JsonSerializable(typeof(ClientToolProviderBackgroundOperationOutcomeMessage))]
[JsonSerializable(typeof(ClientToolProviderToolBinding))]
[JsonSerializable(typeof(ClientToolProviderInvocationRequest))]
[JsonSerializable(typeof(ClientToolProviderBackgroundOperationDescriptor))]
[JsonSerializable(typeof(AudioInputMode))]
[JsonSerializable(typeof(AudioOutputMode))]
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
// Note: Agent input/output events are covered by AgentEventJsonContext.
[JsonSerializable(typeof(RespondResult))]
[JsonSerializable(typeof(RespondStatus))]
[JsonSerializable(typeof(ClientToolContentDto))]
[JsonSerializable(typeof(ClientToolContentDto[]))]
[JsonSerializable(typeof(List<ClientToolContentDto>))]
// Agent DTOs — AgentConfig is covered by HPDJsonContext in the chain
[JsonSerializable(typeof(AgentSummaryDto))]
[JsonSerializable(typeof(AgentSummaryDto[]))]
[JsonSerializable(typeof(List<AgentSummaryDto>))]
[JsonSerializable(typeof(StoredAgentDto))]
[JsonSerializable(typeof(CreateAgentRequest))]
[JsonSerializable(typeof(UpdateAgentRequest))]
// ReasoningOptions — used by AgentRunConfig when events are serialized through AgentEventJsonContext.
[JsonSerializable(typeof(HPD.Agent.ReasoningOptions))]
[JsonSerializable(typeof(HPD.Agent.ReasoningEffort))]
[JsonSerializable(typeof(HPD.Agent.ReasoningOutput))]
// Primitive collections used in responses
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
public partial class HPDAgentApiJsonSerializerContext : JsonSerializerContext
{
}
