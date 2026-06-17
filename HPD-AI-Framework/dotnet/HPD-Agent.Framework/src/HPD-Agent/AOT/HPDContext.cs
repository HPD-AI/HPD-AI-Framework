using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Agent.Planning;
using System.Collections.Immutable;

/// <summary>
/// JSON serialization context for HPD-Agent core types (AOT-compatible).
/// Does not include FFI-specific types - see HPDFFIJsonContext in HPD-Agent.FFI project.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true, 
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
// --- Framework-specific types ---
[JsonSerializable(typeof(ValidationErrorResponse))]
[JsonSerializable(typeof(ValidationError))]
[JsonSerializable(typeof(List<ValidationError>))]

// --- Common primitive and collection types for AI function return values ---
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(IDictionary<string, object>))]
[JsonSerializable(typeof(IDictionary<string, object?>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(List<double>))]
[JsonSerializable(typeof(List<object>))]

// --- JSON Node types for AOT compatibility ---
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonNode))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonObject))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonArray))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonValue))]

// --- Agent configuration types ---
[JsonSerializable(typeof(StoredAgent))]
[JsonSerializable(typeof(AgentConfig))]
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
[JsonSerializable(typeof(AgentClientConfig))]
[JsonSerializable(typeof(ClientProviderConfig))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(McpConfig))]
[JsonSerializable(typeof(CollapsingConfig))]
[JsonSerializable(typeof(HPD.Agent.ReasoningOptions))]
[JsonSerializable(typeof(HPD.Agent.ReasoningEffort))]
[JsonSerializable(typeof(HPD.Agent.ReasoningOutput))]

// --- ToolHarness and Middleware reference types (Config Serialization) ---
[JsonSerializable(typeof(ToolHarnessReference))]
[JsonSerializable(typeof(List<ToolHarnessReference>))]
[JsonSerializable(typeof(MiddlewareReference))]
[JsonSerializable(typeof(List<MiddlewareReference>))]

// --- Per-invocation run options (AgentRunConfig) ---
[JsonSerializable(typeof(AgentRunConfig))]
[JsonSerializable(typeof(AudioRunConfig))]
[JsonSerializable(typeof(ChatRunConfig))]
[JsonSerializable(typeof(Dictionary<string, bool>))]  // For PermissionOverrides

// --- HPD-Agent Typed Content Classes (Phase 1 - Typed Content) ---
[JsonSerializable(typeof(HPD.Agent.ImageContent))]
[JsonSerializable(typeof(HPD.Agent.AudioContent))]
[JsonSerializable(typeof(HPD.Agent.VideoContent))]
[JsonSerializable(typeof(HPD.Agent.DocumentContent))]
[JsonSerializable(typeof(LocalContentMetadata))]

// --- Conversation storage and serialization types ---
[JsonSerializable(typeof(BatchPermissionStateData))]
[JsonSerializable(typeof(CircuitBreakerStateData))]
[JsonSerializable(typeof(ContinuationPermissionStateData))]
[JsonSerializable(typeof(ErrorTrackingStateData))]
[JsonSerializable(typeof(CompactionStateData))]
[JsonSerializable(typeof(CompactionSnapshot))]
[JsonSerializable(typeof(CompactionStrategyOptions))]
[JsonSerializable(typeof(MessageCountingCompactionOptions))]
[JsonSerializable(typeof(SummarizingCompactionOptions))]
[JsonSerializable(typeof(CompactionTriggerOptions))]
[JsonSerializable(typeof(CountCompactionTriggerOptions))]
[JsonSerializable(typeof(TokenBudgetCompactionTriggerOptions))]
[JsonSerializable(typeof(ContextWindowCompactionTriggerOptions))]
[JsonSerializable(typeof(CompositeCompactionTriggerOptions))]
[JsonSerializable(typeof(CompactionRetentionOptions))]
[JsonSerializable(typeof(PreserveThreadHistoryOptions))]
[JsonSerializable(typeof(CompactThreadHistoryOptions))]
[JsonSerializable(typeof(DeleteCompactedMessagesOptions))]
[JsonSerializable(typeof(CompactionBoundaryOptions))]
[JsonSerializable(typeof(ExactCompactedMessagesBoundaryOptions))]
[JsonSerializable(typeof(IncludePreviousMessagesBoundaryOptions))]
[JsonSerializable(typeof(IncludeMessageTurnBoundaryOptions))]
[JsonSerializable(typeof(IncludeToolCallGroupBoundaryOptions))]
[JsonSerializable(typeof(CompositeCompactionBoundaryOptions))]
[JsonSerializable(typeof(PermissionPersistentStateData))]
[JsonSerializable(typeof(TotalErrorThresholdStateData))]
[JsonSerializable(typeof(PlanModePersistentStateData))]
[JsonSerializable(typeof(AgentPlanData))]
[JsonSerializable(typeof(PlanStepData))]
[JsonSerializable(typeof(ClientToolStateData))]
[JsonSerializable(typeof(ContainerMiddlewareState))]
[JsonSerializable(typeof(ContainerInstructionSet))]
[JsonSerializable(typeof(RecoveryInfo))]
[JsonSerializable(typeof(ImmutableHashSet<string>))]
[JsonSerializable(typeof(ImmutableDictionary<string, clientToolHarnessDefinition>), TypeInfoPropertyName = "ImmutableDictionaryStringClientToolHarnessDefinition")]
[JsonSerializable(typeof(ImmutableDictionary<string, ContextItem>), TypeInfoPropertyName = "ImmutableDictionaryStringContextItem")]
[JsonSerializable(typeof(ImmutableDictionary<string, ContainerInstructionSet>), TypeInfoPropertyName = "ImmutableDictionaryStringContainerInstructionSet")]
[JsonSerializable(typeof(ImmutableDictionary<string, RecoveryInfo>), TypeInfoPropertyName = "ImmutableDictionaryStringRecoveryInfo")]

// --- Client Tools types ---
[JsonSerializable(typeof(HPD.Agent.ClientTools.clientToolHarnessDefinition))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.clientToolHarnessDefinition[]))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ClientToolDefinition))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ClientToolDefinition[]))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ClientSkillDefinition))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ClientSkillDefinition[]))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ClientSkillReference))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ClientSkillReference[]))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ContextItem))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ContextItem[]))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.AgentClientInput))]

// --- Background Responses types ---
[JsonSerializable(typeof(BackgroundResponsesConfig))]
[JsonSerializable(typeof(OperationStatus))]
[JsonSerializable(typeof(ThreadRunStartedEvent))]
[JsonSerializable(typeof(ThreadRunCompletedEvent))]
[JsonSerializable(typeof(BackgroundOperationStartedEvent))]
[JsonSerializable(typeof(BackgroundOperationStatusEvent))]
[JsonSerializable(typeof(BackgroundOperationInfo))]
[JsonSerializable(typeof(ToolCallBackgroundTaskStartedEvent))]
[JsonSerializable(typeof(ToolCallBackgroundTaskCompletedEvent))]
[JsonSerializable(typeof(ToolCallBackgroundTaskCancelledEvent))]
[JsonSerializable(typeof(ToolCallBackgroundTaskFaultedEvent))]
[JsonSerializable(typeof(FunctionInvocationSnapshot))]
[JsonSerializable(typeof(ToolInvocationInfo))]

// --- Internal storage state types (nested classes) ---
// Note: Nested classes need full type paths for AOT
// These are internal implementation details but need serialization support

// --- Additional utility types for generic serialization ---
[JsonSerializable(typeof(object[]))]  // For dynamic object arrays in logging
[JsonSerializable(typeof(string[]))]  // For toolharness parameters that accept string arrays (e.g. glob patterns)

public partial class HPDJsonContext : JsonSerializerContext
{
}
