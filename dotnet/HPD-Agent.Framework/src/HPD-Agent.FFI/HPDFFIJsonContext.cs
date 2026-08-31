using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;
using HPD.Agent.FFI;
using HPD.Agent.MCP;
using HPD.Agent.Middleware;
using HPD.Agent.Planning;
using HPD.Agent.StructuredOutput;
using HPD.Agent.Providers;

namespace HPD.Agent.FFI;

/// <summary>
/// JSON serialization context for HPD-Agent FFI exports (AOT-compatible).
/// Includes all core types plus FFI-specific types like RustFunctionInfo, ToolHarnessRegistry, etc.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true, 
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
[JsonSerializable(typeof(ProviderClientConfig))]
[JsonSerializable(typeof(ProviderReference))]
[JsonSerializable(typeof(ProviderAuthentication))]
[JsonSerializable(typeof(ApiKeyProviderAuthentication))]
[JsonSerializable(typeof(OAuthProviderAuthentication))]
[JsonSerializable(typeof(ExternalIdentityProviderAuthentication))]
[JsonSerializable(typeof(AnonymousProviderAuthentication))]
[JsonSerializable(typeof(ProviderAuthorizationChallenge))]
[JsonSerializable(typeof(BrowserAuthorizationChallenge))]
[JsonSerializable(typeof(DeviceAuthorizationChallenge))]
[JsonSerializable(typeof(ProviderAuthorizationResponse))]
[JsonSerializable(typeof(BrowserAuthorizationResponse))]
[JsonSerializable(typeof(DeviceAuthorizationPresentationResponse))]
[JsonSerializable(typeof(ProviderDeviceAuthorizationStatus))]
[JsonSerializable(typeof(ProviderAuthorizationStatus))]
[JsonSerializable(typeof(ProviderDisconnectResult))]
[JsonSerializable(typeof(ProviderRevocationResult))]
[JsonSerializable(typeof(ProviderAccountFfiRequest))]
[JsonSerializable(typeof(CompleteProviderAuthorizationFfiRequest))]
[JsonSerializable(typeof(BeginProviderAuthorizationFfiRequest))]
[JsonSerializable(typeof(ProviderDeviceAuthorizationFfiRequest))]
[JsonSerializable(typeof(ProviderAccountFfiError))]
[JsonSerializable(typeof(AgentClientsConfig))]
[JsonSerializable(typeof(ValidationConfig))]
[JsonSerializable(typeof(ErrorHandlingConfig))]
[JsonSerializable(typeof(CompactionConfig))]
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
[JsonSerializable(typeof(AutomaticCompactionPolicy))]
[JsonSerializable(typeof(CompactionCommitMode))]
[JsonSerializable(typeof(AgentRunConfig))]
[JsonSerializable(typeof(CollapsingRunPolicy))]
[JsonSerializable(typeof(ContainerRecoveryHistoryMode))]
[JsonSerializable(typeof(AgentSecurityRunConfig))]
[JsonSerializable(typeof(AgentSandboxRunConfig))]
[JsonSerializable(typeof(AgentApprovalPolicy))]
[JsonSerializable(typeof(AgentSandboxPolicy))]
[JsonSerializable(typeof(AgentSandboxEscapePolicy))]
[JsonSerializable(typeof(HPD.Agent.Security.AgentSandboxConfiguration))]
[JsonSerializable(typeof(HPD.Agent.Security.AgentSandboxPathGrant))]
[JsonSerializable(typeof(HPD.Agent.Security.AgentSandboxPathAccess))]
[JsonSerializable(typeof(CompactionRunPolicy))]
[JsonSerializable(typeof(ThreadCompactionRequest))]
[JsonSerializable(typeof(ThreadContextUsage))]
[JsonSerializable(typeof(AudioRunConfig))]

// --- Plan Mode types (from HPD.Agent.Planning) ---
[JsonSerializable(typeof(PlanModeConfig))]
[JsonSerializable(typeof(PlanStepStatus))]

// --- Conversation and messaging types ---
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(ChatRole))]
[JsonSerializable(typeof(List<ChatMessage>))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(ChatOptions))]

// --- Extensions.AI types for conversation support ---
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(ChatRole))]
[JsonSerializable(typeof(ChatOptions))]
[JsonSerializable(typeof(UsageDetails))]
[JsonSerializable(typeof(AdditionalPropertiesDictionary))]
[JsonSerializable(typeof(ChatFinishReason))]
[JsonSerializable(typeof(ChatResponseUpdate))]
[JsonSerializable(typeof(FunctionCallContent))]
[JsonSerializable(typeof(FunctionResultContent))]
[JsonSerializable(typeof(AIContent))]
[JsonSerializable(typeof(List<ChatMessage>))]
[JsonSerializable(typeof(List<AIContent>))]
[JsonSerializable(typeof(IList<ChatMessage>))]
[JsonSerializable(typeof(IEnumerable<ChatMessage>))]

// --- FFI-specific native ToolHarness types (language-agnostic) ---
[JsonSerializable(typeof(NativeFunctionInfo))]
[JsonSerializable(typeof(List<NativeFunctionInfo>))]
[JsonSerializable(typeof(ToolHarnessRegistry))]
[JsonSerializable(typeof(ToolHarnessInfo))]
[JsonSerializable(typeof(FunctionInfo))]
[JsonSerializable(typeof(HARNESStats))]
[JsonSerializable(typeof(HARNESSummary))]
[JsonSerializable(typeof(ToolHarnessExecutionResult))]

// --- Internal Agent Event Types (for protocol adapters) ---
[JsonSerializable(typeof(ProviderUsageMeasurement))]
[JsonSerializable(typeof(MessageTurnUsageSummary))]
[JsonSerializable(typeof(ProviderReportedMonetaryObservation))]
[JsonSerializable(typeof(AgentOperationSnapshot))]
[JsonSerializable(typeof(FfiAgentOperation))]
[JsonSerializable(typeof(List<FfiAgentOperation>))]
[JsonSerializable(typeof(AgentOperationReceipt))]
[JsonSerializable(typeof(AgentOperationNotification))]
[JsonSerializable(typeof(AgentOperationTombstone))]
[JsonSerializable(typeof(FunctionInvocationSnapshot))]
[JsonSerializable(typeof(ToolInvocationInfo))]
[JsonSerializable(typeof(ToolResultPayload))]
[JsonSerializable(typeof(IReadOnlyList<HPD.Agent.ClientTools.IToolResultContent>), TypeInfoPropertyName = "IReadOnlyListIToolResultContent")]
[JsonSerializable(typeof(List<HPD.Agent.ClientTools.IToolResultContent>))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ClientToolInvokeOutcomeKind))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.ClientToolOperationOutcomeState))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.TextContent))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.BinaryContent))]
[JsonSerializable(typeof(HPD.Agent.ClientTools.JsonContent))]

// --- Structured Output Types ---
[JsonSerializable(typeof(StructuredOutputOptions))]
[JsonSerializable(typeof(StructuredResultEventDto))]

// --- Agent State Types ---
[JsonSerializable(typeof(AgentLoopState))]
[JsonSerializable(typeof(MiddlewareState))]
[JsonSerializable(typeof(CircuitBreakerStateData))]
[JsonSerializable(typeof(ErrorTrackingStateData))]
[JsonSerializable(typeof(ContinuationPermissionStateData))]
[JsonSerializable(typeof(IReadOnlyList<ChatMessage>))]
[JsonSerializable(typeof(BatchPermissionStateData))]
[JsonSerializable(typeof(TotalErrorThresholdStateData))]

// --- Checkpointing / Resume Types ---
// (Removed legacy SessionCheckpoint type)

// --- Permission Types ---
[JsonSerializable(typeof(PermissionChoice))]
[JsonSerializable(typeof(PermissionDecision))]

// --- AGUI Protocol Types ---


public partial class HPDFFIJsonContext : JsonSerializerContext
{
}
