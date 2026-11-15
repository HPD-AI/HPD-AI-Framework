using System.Text.Json.Serialization;
using HPD.Agent.Providers;

namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Partial update for the portable fields of an <see cref="AgentConfig"/>.
/// </summary>
/// <remarks>
/// Omitted or null properties remain unchanged. Supplied collections replace the
/// complete previous collection. Use <see cref="Clear"/> to clear nullable sections.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AgentConfigUpdate
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Name { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string SystemInstructions { get; init; } = null!;
    public int? MaxAgenticIterations { get; init; }
    public int? MaxSubAgentDepth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubAgentForkOptions DefaultSubAgentForkOptions { get; init; } = null!;
    public int? ContinuationExtensionAmount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentShutdownOptions Shutdown { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentOperationRetentionPolicy OperationRetention { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentClientsConfig Clients { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<AgentProviderBackendProfile> ProviderProfiles { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<AgentProviderFamilyDefault> ProviderDefaults { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationConfig Validation { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorHandlingConfig ErrorHandling { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResilienceConfig Resilience { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrivacyConfig Privacy { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompactionConfig Compaction { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Goals.GoalConfig Goals { get; init; } = null!;
    public bool? EnableParentCommunication { get; init; }
    public bool? AllowUserQuestions { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgenticLoopConfig AgenticLoop { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentMessagesConfig Messages { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSelectionConfig ToolSelection { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CollapsingConfig Collapsing { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillRuntimeOptions Skills { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolOutputConfig ToolOutputs { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolHarnessReference> ToolHarnesses { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HPD.Agent.MCP.AgentMcpConfig Mcp { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MiddlewareReference> Middlewares { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ObservabilityConfig Observability { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BackgroundResponsesConfig BackgroundResponses { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioConfig Audio { get; init; } = null!;
    public bool? IncludeReasoningInModelHistory { get; init; }
    public bool? CoalesceDeltas { get; init; }

    /// <summary>
    /// Clears nullable sections by JSON property name.
    /// Supported names are <c>validation</c>, <c>errorHandling</c>,
    /// <c>resilience</c>, <c>privacy</c>, <c>compaction</c>, <c>goals</c>,
    /// <c>enableParentCommunication</c>, <c>agenticLoop</c>, <c>toolSelection</c>,
    /// <c>mcp</c>, <c>observability</c>, <c>backgroundResponses</c>, and <c>audio</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> Clear { get; init; } = null!;

    /// <summary>Creates a patch that replaces every portable field.</summary>
    public static AgentConfigUpdate From(AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var clear = new List<string>();
        AddClear(clear, "validation", config.Validation);
        AddClear(clear, "errorHandling", config.ErrorHandling);
        AddClear(clear, "resilience", config.Resilience);
        AddClear(clear, "privacy", config.Privacy);
        AddClear(clear, "compaction", config.Compaction);
        AddClear(clear, "goals", config.Goals);
        if (config.EnableParentCommunication is null)
            clear.Add("enableParentCommunication");
        AddClear(clear, "agenticLoop", config.AgenticLoop);
        AddClear(clear, "toolSelection", config.ToolSelection);
        AddClear(clear, "mcp", config.Mcp);
        AddClear(clear, "observability", config.Observability);
        AddClear(clear, "backgroundResponses", config.BackgroundResponses);
        AddClear(clear, "audio", config.Audio);

        return new AgentConfigUpdate
        {
            Name = config.Name,
            SystemInstructions = config.SystemInstructions,
            MaxAgenticIterations = config.MaxAgenticIterations,
            MaxSubAgentDepth = config.MaxSubAgentDepth,
            DefaultSubAgentForkOptions = config.DefaultSubAgentForkOptions,
            ContinuationExtensionAmount = config.ContinuationExtensionAmount,
            Shutdown = config.Shutdown,
            OperationRetention = config.OperationRetention,
            Clients = config.Clients,
            ProviderProfiles = config.ProviderProfiles,
            ProviderDefaults = config.ProviderDefaults,
            Validation = config.Validation!,
            ErrorHandling = config.ErrorHandling!,
            Resilience = config.Resilience!,
            Privacy = config.Privacy!,
            Compaction = config.Compaction!,
            Goals = config.Goals!,
            EnableParentCommunication = config.EnableParentCommunication,
            AllowUserQuestions = config.AllowUserQuestions,
            AgenticLoop = config.AgenticLoop!,
            Messages = config.Messages,
            ToolSelection = config.ToolSelection!,
            Collapsing = config.Collapsing,
            Skills = config.Skills,
            ToolOutputs = config.ToolOutputs,
            ToolHarnesses = config.ToolHarnesses,
            Mcp = config.Mcp!,
            Middlewares = config.Middlewares,
            Observability = config.Observability!,
            BackgroundResponses = config.BackgroundResponses!,
            Audio = config.Audio!,
            IncludeReasoningInModelHistory = config.IncludeReasoningInModelHistory,
            CoalesceDeltas = config.CoalesceDeltas,
            Clear = clear
        };
    }

    internal AgentConfig ApplyTo(AgentConfig existing, List<string> errors)
    {
        var config = AgentConfigSnapshot.Create(existing);

        foreach (var field in Clear ?? [])
        {
            switch (field)
            {
                case "validation": config.Validation = null; break;
                case "errorHandling": config.ErrorHandling = null; break;
                case "resilience": config.Resilience = null; break;
                case "privacy": config.Privacy = null; break;
                case "compaction": config.Compaction = null; break;
                case "goals": config.Goals = null; break;
                case "enableParentCommunication": config.EnableParentCommunication = null; break;
                case "agenticLoop": config.AgenticLoop = null; break;
                case "toolSelection": config.ToolSelection = null; break;
                case "mcp": config.Mcp = null; break;
                case "observability": config.Observability = null; break;
                case "backgroundResponses": config.BackgroundResponses = null; break;
                case "audio": config.Audio = null; break;
                default:
                    errors.Add($"clear contains unsupported or non-nullable field '{field}'.");
                    break;
            }
        }

        if (Name is not null) config.Name = Name;
        if (SystemInstructions is not null) config.SystemInstructions = SystemInstructions;
        if (MaxAgenticIterations is { } maxAgenticIterations)
            config.MaxAgenticIterations = maxAgenticIterations;
        if (MaxSubAgentDepth is { } maxSubAgentDepth)
            config.MaxSubAgentDepth = maxSubAgentDepth;
        if (DefaultSubAgentForkOptions is not null)
            config.DefaultSubAgentForkOptions = DefaultSubAgentForkOptions;
        if (ContinuationExtensionAmount is { } continuationExtensionAmount)
            config.ContinuationExtensionAmount = continuationExtensionAmount;
        if (Shutdown is not null) config.Shutdown = Shutdown;
        if (OperationRetention is not null) config.OperationRetention = OperationRetention;
        if (Clients is not null) config.Clients = Clients;
        if (ProviderProfiles is not null) config.ProviderProfiles = ProviderProfiles;
        if (ProviderDefaults is not null) config.ProviderDefaults = ProviderDefaults;
        if (Validation is not null) config.Validation = Validation;
        if (ErrorHandling is not null) config.ErrorHandling = ErrorHandling;
        if (Resilience is not null) config.Resilience = Resilience;
        if (Privacy is not null) config.Privacy = Privacy;
        if (Compaction is not null) config.Compaction = Compaction;
        if (Goals is not null) config.Goals = Goals;
        if (EnableParentCommunication is { } enableParentCommunication)
            config.EnableParentCommunication = enableParentCommunication;
        if (AllowUserQuestions is { } allowUserQuestions)
            config.AllowUserQuestions = allowUserQuestions;
        if (AgenticLoop is not null) config.AgenticLoop = AgenticLoop;
        if (Messages is not null) config.Messages = Messages;
        if (ToolSelection is not null) config.ToolSelection = ToolSelection;
        if (Collapsing is not null) config.Collapsing = Collapsing;
        if (Skills is not null) config.Skills = Skills;
        if (ToolOutputs is not null) config.ToolOutputs = ToolOutputs;
        if (ToolHarnesses is not null) config.ToolHarnesses = ToolHarnesses;
        if (Mcp is not null) config.Mcp = Mcp;
        if (Middlewares is not null) config.Middlewares = Middlewares;
        if (Observability is not null) config.Observability = Observability;
        if (BackgroundResponses is not null) config.BackgroundResponses = BackgroundResponses;
        if (Audio is not null) config.Audio = Audio;
        if (IncludeReasoningInModelHistory is { } includeReasoning)
            config.IncludeReasoningInModelHistory = includeReasoning;
        if (CoalesceDeltas is { } coalesceDeltas)
            config.CoalesceDeltas = coalesceDeltas;

        return config;
    }

    private static void AddClear(List<string> fields, string name, object? value)
    {
        if (value is null)
            fields.Add(name);
    }
}
