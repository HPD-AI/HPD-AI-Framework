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
[JsonSerializable(typeof(ThreadRunBackgroundOperationDto))]
[JsonSerializable(typeof(ThreadRunBackgroundTaskDto))]
[JsonSerializable(typeof(List<ThreadRunBackgroundTaskDto>))]
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
// Run config DTO graph used by StreamTextRequest
[JsonSerializable(typeof(AgentRunConfig))]
[JsonSerializable(typeof(AgentModelTransportMode))]
[JsonSerializable(typeof(AgentClientConfig))]
[JsonSerializable(typeof(ClientProviderConfig))]
[JsonSerializable(typeof(AudioRunConfig))]
[JsonSerializable(typeof(ChatRunConfig))]
[JsonSerializable(typeof(UploadStrategy))]
[JsonSerializable(typeof(CompactionBehavior))]
[JsonSerializable(typeof(StructuredOutputOptions))]
[JsonSerializable(typeof(AgentClientInput))]
[JsonSerializable(typeof(ClientToolDefinition))]
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
