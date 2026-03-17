using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.FFI;
using HPD.Agent.MCP;
using HPD.Agent.Planning;
using HPD.Agent.StructuredOutput;

namespace HPD.Agent.FFI;

/// <summary>
/// JSON serialization context for HPD-Agent FFI exports (AOT-compatible).
/// Includes all core types plus FFI-specific types like RustFunctionInfo, ToolkitRegistry, etc.
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
[JsonSerializable(typeof(ProviderConfig))]
[JsonSerializable(typeof(McpConfig))]
[JsonSerializable(typeof(ValidationConfig))]
[JsonSerializable(typeof(ErrorHandlingConfig))]
[JsonSerializable(typeof(DocumentHandlingConfig))]
[JsonSerializable(typeof(HistoryReductionConfig))]

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

// --- FFI-specific native Toolkit types (language-agnostic) ---
[JsonSerializable(typeof(NativeFunctionInfo))]
[JsonSerializable(typeof(List<NativeFunctionInfo>))]
[JsonSerializable(typeof(ToolkitRegistry))]
[JsonSerializable(typeof(ToolkitInfo))]
[JsonSerializable(typeof(FunctionInfo))]
[JsonSerializable(typeof(ToolkitStats))]
[JsonSerializable(typeof(ToolkitSummary))]
[JsonSerializable(typeof(ToolkitExecutionResult))]

// --- Internal Agent Event Types (for protocol adapters) ---
[JsonSerializable(typeof(AgentEvent))]
[JsonSerializable(typeof(MessageTurnStartedEvent))]
[JsonSerializable(typeof(MessageTurnFinishedEvent))]
[JsonSerializable(typeof(MessageTurnErrorEvent))]
[JsonSerializable(typeof(AgentTurnStartedEvent))]
[JsonSerializable(typeof(AgentTurnFinishedEvent))]
[JsonSerializable(typeof(TextMessageStartEvent))]
[JsonSerializable(typeof(TextDeltaEvent))]
[JsonSerializable(typeof(TextMessageEndEvent))]
[JsonSerializable(typeof(ReasoningMessageStartEvent))]
[JsonSerializable(typeof(ReasoningDeltaEvent))]
[JsonSerializable(typeof(ReasoningMessageEndEvent))]
[JsonSerializable(typeof(ToolCallStartEvent))]
[JsonSerializable(typeof(ToolCallArgsEvent))]
[JsonSerializable(typeof(ToolCallEndEvent))]
[JsonSerializable(typeof(ToolCallResultEvent))]
[JsonSerializable(typeof(PermissionRequestEvent))]
[JsonSerializable(typeof(PermissionResponseEvent))]
[JsonSerializable(typeof(PermissionApprovedEvent))]
[JsonSerializable(typeof(PermissionDeniedEvent))]
[JsonSerializable(typeof(ContinuationRequestEvent))]
[JsonSerializable(typeof(ContinuationResponseEvent))]
[JsonSerializable(typeof(ClarificationRequestEvent))]
[JsonSerializable(typeof(ClarificationResponseEvent))]
[JsonSerializable(typeof(MiddlewareErrorEvent))]

// --- Structured Output Types ---
[JsonSerializable(typeof(StructuredOutputOptions))]
[JsonSerializable(typeof(StructuredOutputErrorEvent))]
[JsonSerializable(typeof(StructuredResultEventDto))]
[JsonSerializable(typeof(StructuredOutputStartEvent))]
[JsonSerializable(typeof(StructuredOutputPartialEvent))]
[JsonSerializable(typeof(StructuredOutputCompleteEvent))]

// --- Agent State Types ---
[JsonSerializable(typeof(AgentLoopState))]
[JsonSerializable(typeof(MiddlewareState))]
[JsonSerializable(typeof(CircuitBreakerStateData))]
[JsonSerializable(typeof(ErrorTrackingStateData))]
[JsonSerializable(typeof(ContinuationPermissionStateData))]
[JsonSerializable(typeof(HistoryReductionStateData))]
[JsonSerializable(typeof(CachedReduction))]
[JsonSerializable(typeof(BatchPermissionStateData))]
[JsonSerializable(typeof(TotalErrorThresholdStateData))]

// --- Checkpointing / Resume Types ---
// (Removed legacy SessionCheckpoint type)

// --- Permission Types ---
[JsonSerializable(typeof(PermissionChoice))]
[JsonSerializable(typeof(PermissionDecision))]

// --- AGUI Protocol Types ---

[JsonSerializable(typeof(TextMessageStartEvent))]
[JsonSerializable(typeof(TextMessageEndEvent))]
[JsonSerializable(typeof(ToolCallStartEvent))]
[JsonSerializable(typeof(ToolCallArgsEvent))]
[JsonSerializable(typeof(ToolCallEndEvent))]
[JsonSerializable(typeof(ToolCallResultEvent))]
[JsonSerializable(typeof(StateSnapshotEvent))]

public partial class HPDFFIJsonContext : JsonSerializerContext
{
}
