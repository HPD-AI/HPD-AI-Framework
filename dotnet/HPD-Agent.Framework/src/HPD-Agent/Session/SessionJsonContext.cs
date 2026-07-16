using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.ClientTools;
using HPD.Agent.Planning;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;


namespace HPD.Agent;

/// <summary>
/// JSON serialization context for Session types (AOT-compatible).
/// Combines M.E.AI JsonContext with HPD-specific session types.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
// Session types
[JsonSerializable(typeof(Session))]
[JsonSerializable(typeof(Thread))]
[JsonSerializable(typeof(ThreadDescriptor))]
[JsonSerializable(typeof(ThreadForkDescriptor))]
[JsonSerializable(typeof(ThreadRuntimeChildDescriptor))]
[JsonSerializable(typeof(FileThreadDescriptorState))]
[JsonSerializable(typeof(ThreadKind))]
[JsonSerializable(typeof(ThreadVisibility))]
[JsonSerializable(typeof(ThreadHistoryCompactionCheckpointEvent))]
[JsonSerializable(typeof(ThreadHistoryCompactionMode))]
[JsonSerializable(typeof(AgentEvent))]
[JsonSerializable(typeof(List<AgentEvent>))]
[JsonSerializable(typeof(ToolResultPayload))]

// HPD-specific types
[JsonSerializable(typeof(AgentLoopState))]
[JsonSerializable(typeof(ValidationErrorResponse))]
[JsonSerializable(typeof(MiddlewareState))]
[JsonSerializable(typeof(ClientToolStateData))]
[JsonSerializable(typeof(ClientToolAugmentation))]
[JsonSerializable(typeof(clientToolHarnessDefinition))]
[JsonSerializable(typeof(clientToolHarnessDefinition[]))]
[JsonSerializable(typeof(ClientToolDefinition))]
[JsonSerializable(typeof(ClientToolDefinition[]))]
[JsonSerializable(typeof(ClientSkillDefinition))]
[JsonSerializable(typeof(ClientSkillDefinition[]))]
[JsonSerializable(typeof(ClientSkillReference))]
[JsonSerializable(typeof(ClientSkillReference[]))]
[JsonSerializable(typeof(ContextItem))]
[JsonSerializable(typeof(ContextItem[]))]
[JsonSerializable(typeof(ImmutableHashSet<string>))]
[JsonSerializable(typeof(ImmutableDictionary<string, object?>), TypeInfoPropertyName = "ImmutableDictionaryStringObjectNullable")]
[JsonSerializable(typeof(ImmutableDictionary<string, int>))]
[JsonSerializable(typeof(ImmutableDictionary<string, clientToolHarnessDefinition>), TypeInfoPropertyName = "ImmutableDictionaryStringClientToolHarnessDefinition")]
[JsonSerializable(typeof(ImmutableDictionary<string, ClientToolProviderToolBinding>), TypeInfoPropertyName = "ImmutableDictionaryStringClientToolProviderToolBinding")]
[JsonSerializable(typeof(ImmutableDictionary<string, ContextItem>), TypeInfoPropertyName = "ImmutableDictionaryStringContextItem")]
[JsonSerializable(typeof(IReadOnlyList<clientToolHarnessDefinition>))]
[JsonSerializable(typeof(IReadOnlyList<ContextItem>))]
[JsonSerializable(typeof(IReadOnlySet<string>))]

// Common middleware state types that may be serialized in AgentLoopState.
[JsonSerializable(typeof(BatchPermissionStateData))]
[JsonSerializable(typeof(CircuitBreakerStateData))]
[JsonSerializable(typeof(ContinuationPermissionStateData))]
[JsonSerializable(typeof(ErrorTrackingStateData))]
[JsonSerializable(typeof(CompactionStateData))]
[JsonSerializable(typeof(CompactionSnapshot))]
[JsonSerializable(typeof(CompactionRunConfig))]
[JsonSerializable(typeof(ModelContextWindowOptions))]
[JsonSerializable(typeof(ThreadContextUsage))]
[JsonSerializable(typeof(CompactionStrategyOptions))]
[JsonSerializable(typeof(MessageCountingCompactionOptions))]
[JsonSerializable(typeof(SummarizingCompactionOptions))]
[JsonSerializable(typeof(CompactionTriggerOptions))]
[JsonSerializable(typeof(CountCompactionTriggerOptions))]
[JsonSerializable(typeof(ContextWindowCompactionTriggerOptions))]
[JsonSerializable(typeof(CompositeCompactionTriggerOptions))]
[JsonSerializable(typeof(CompactionRetentionOptions))]
[JsonSerializable(typeof(PreserveThreadHistoryOptions))]
[JsonSerializable(typeof(CompactThreadHistoryOptions))]
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
[JsonSerializable(typeof(ContainerMiddlewareState))]
[JsonSerializable(typeof(ContainerInstructionSet))]
[JsonSerializable(typeof(RecoveryInfo))]
[JsonSerializable(typeof(ImmutableDictionary<string, ContainerInstructionSet>), TypeInfoPropertyName = "ImmutableDictionaryStringContainerInstructionSet")]
[JsonSerializable(typeof(ImmutableDictionary<string, RecoveryInfo>), TypeInfoPropertyName = "ImmutableDictionaryStringRecoveryInfo")]

// M.E.AI types (explicitly added for session persistence)
// Note: Most M.E.AI types are registered via AIJsonUtilities.DefaultOptions
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(List<ChatMessage>))]
[JsonSerializable(typeof(IReadOnlyList<ChatMessage>))]
[JsonSerializable(typeof(AIContent))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(JsonElement))]

// HPD-Agent Typed Content Classes (Phase 1 - Typed Content)
// These must be serializable for session persistence
[JsonSerializable(typeof(HPD.Agent.ImageContent))]
[JsonSerializable(typeof(HPD.Agent.AudioContent))]
[JsonSerializable(typeof(HPD.Agent.VideoContent))]
[JsonSerializable(typeof(HPD.Agent.DocumentContent))]

// Common .NET types that may appear in tool results
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]

public partial class SessionJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Combined options that merge SessionJsonContext with M.E.AI's AIJsonUtilities.DefaultOptions.
    /// Use this for session serialization to support all M.E.AI types including primitives in tool results.
    /// </summary>
    public static JsonSerializerOptions CombinedOptions { get; } = CreateCombinedOptions();

    /// <summary>
    /// Combined source-generated context with M.E.AI and HPD custom content resolvers.
    /// </summary>
    public static SessionJsonContext Combined { get; } = new(new JsonSerializerOptions(CombinedOptions));

    private static JsonSerializerOptions CreateCombinedOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new AgentEventJsonConverter());

        // Start with our SessionJsonContext for HPD-specific types
        options.TypeInfoResolverChain.Add(new SessionJsonContext());
        options.TypeInfoResolverChain.Add(AgentEventJsonContext.Default);

        // Add M.E.AI's type resolvers for all primitives and M.E.AI types
        foreach (var resolver in AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)
        {
            if (resolver != null)
            {
                options.TypeInfoResolverChain.Add(resolver);
            }
        }

        // Register HPD-Agent custom content types as AIContent derived types
        // These extend DataContent but need explicit registration for polymorphic serialization
        options.AddAIContentType<ImageContent>("hpd:image");
        options.AddAIContentType<AudioContent>("hpd:audio");
        options.AddAIContentType<VideoContent>("hpd:video");
        options.AddAIContentType<DocumentContent>("hpd:document");

        options.MakeReadOnly();
        return options;
    }
}
