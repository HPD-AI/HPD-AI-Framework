using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPDAgent.Graph.Abstractions;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent.Config;

/// <summary>
/// Fully serializable multi-agent workflow configuration.
/// Can be loaded from JSON/YAML or built programmatically.
/// </summary>
public sealed record MultiAgentWorkflowConfig
{
    /// <summary>
    /// Workflow name (required).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Semantic version.
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// Agent nodes with full agent configuration (embeds AgentConfig).
    /// Key: node ID.
    /// </summary>
    public Dictionary<string, AgentNodeConfig> Agents { get; init; } = new();

    /// <summary>
    /// Routing between agents.
    /// </summary>
    public List<EdgeConfig> Edges { get; init; } = new();

    /// <summary>
    /// Workflow-level settings.
    /// </summary>
    public WorkflowSettingsConfig Settings { get; init; } = new();
}

/// <summary>
/// Configuration for a single agent node.
/// </summary>
public sealed record AgentNodeConfig
{
    /// <summary>
    /// Full agent configuration (embeds entire AgentConfig - serializable).
    /// </summary>
    public required AgentConfig Agent { get; init; }

    /// <summary>
    /// Output mode for this agent.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentOutputMode OutputMode { get; init; } = AgentOutputMode.String;

    /// <summary>
    /// Structured output type name (for Structured mode).
    /// Must be resolvable at runtime.
    /// </summary>
    public string? StructuredOutputType { get; init; }

    /// <summary>
    /// Union type names (for Union mode).
    /// </summary>
    public List<string>? UnionTypeNames { get; init; }

    /// <summary>
    /// Per-node timeout.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Maximum concurrent executions (for map operations).
    /// </summary>
    public int? MaxConcurrent { get; init; }

    /// <summary>
    /// Retry configuration.
    /// </summary>
    public RetryConfig? Retry { get; init; }

    /// <summary>
    /// Error handling configuration.
    /// </summary>
    public ErrorConfig? OnError { get; init; }

    /// <summary>
    /// Input key to look for from upstream nodes.
    /// </summary>
    public string? InputKey { get; init; }

    /// <summary>
    /// Output key for the agent's response.
    /// </summary>
    public string? OutputKey { get; init; }

    /// <summary>
    /// Handlebars template for constructing input.
    /// </summary>
    public string? InputTemplate { get; init; }

    /// <summary>
    /// Additional system instructions.
    /// </summary>
    public string? AdditionalInstructions { get; init; }

    /// <summary>
    /// Metadata for metrics, tagging, cost tracking.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Retry configuration.
/// </summary>
public sealed record RetryConfig
{
    /// <summary>
    /// Maximum attempts (including initial).
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Initial delay before first retry.
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Backoff strategy.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BackoffStrategy Strategy { get; init; } = BackoffStrategy.Exponential;

    /// <summary>
    /// Maximum delay between retries.
    /// </summary>
    public TimeSpan? MaxDelay { get; init; }

    /// <summary>
    /// Only retry transient errors.
    /// </summary>
    public bool OnlyTransient { get; init; } = true;
}

/// <summary>
/// Error handling configuration.
/// </summary>
public sealed record ErrorConfig
{
    /// <summary>
    /// Error handling mode.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ErrorMode Mode { get; init; } = ErrorMode.Stop;

    /// <summary>
    /// Agent to use when Mode = Fallback.
    /// </summary>
    public string? FallbackAgent { get; init; }
}

/// <summary>
/// Simplified error modes (maps to HPD.Graph's ErrorPropagationPolicy).
/// </summary>
public enum ErrorMode
{
    /// <summary>
    /// Stop entire workflow on error.
    /// </summary>
    Stop,

    /// <summary>
    /// Skip this agent, continue others.
    /// </summary>
    Skip,

    /// <summary>
    /// Ignore error, continue with partial data.
    /// </summary>
    Isolate,

    /// <summary>
    /// Use FallbackAgent instead.
    /// </summary>
    Fallback
}

/// <summary>
/// Edge configuration.
/// </summary>
public sealed record EdgeConfig
{
    /// <summary>
    /// Source node ID(s). Use "START" for entry.
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// Target node ID(s). Use "END" for exit.
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// Condition for traversing this edge (null = always).
    /// </summary>
    public ConditionConfig? When { get; init; }
}

/// <summary>
/// Condition configuration for edge routing.
/// </summary>
public sealed record ConditionConfig
{
    /// <summary>
    /// Condition type.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConditionType Type { get; init; } = ConditionType.Always;

    /// <summary>
    /// Field name to evaluate.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// Value to compare against.
    /// For FieldContainsAny/FieldContainsAll, this is a JSON array of values.
    /// </summary>
    public JsonElement? Value { get; init; }

    /// <summary>
    /// Sub-conditions for compound types (And, Or, Not).
    /// </summary>
    public List<ConditionConfig>? Conditions { get; init; }

    /// <summary>
    /// Regex flags for FieldMatchesRegex (e.g. "IgnoreCase,Multiline").
    /// </summary>
    public string? RegexOptions { get; init; }
}

/// <summary>
/// Workflow-level settings.
/// </summary>
public sealed record WorkflowSettingsConfig
{
    /// <summary>
    /// Default timeout for all agents.
    /// </summary>
    public TimeSpan? DefaultTimeout { get; init; }

    /// <summary>
    /// Enable checkpointing for durability.
    /// </summary>
    public bool EnableCheckpointing { get; init; } = false;

    /// <summary>
    /// Enable metrics collection.
    /// </summary>
    public bool EnableMetrics { get; init; } = true;

    /// <summary>
    /// When to emit streaming events.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StreamingMode StreamingMode { get; init; } = StreamingMode.PerNode;

    /// <summary>
    /// Maximum iterations for cyclic graphs.
    /// </summary>
    public int MaxIterations { get; init; } = 25;

    /// <summary>
    /// Iteration options for loops/cycles.
    /// </summary>
    public IterationOptionsConfig? IterationOptions { get; init; }
}

/// <summary>
/// Streaming mode for event emission.
/// </summary>
public enum StreamingMode
{
    /// <summary>
    /// Emit events after each agent completes.
    /// </summary>
    PerNode,

    /// <summary>
    /// Emit events after parallel layer completes.
    /// </summary>
    PerLayer
}

/// <summary>
/// Configuration for change-aware iteration 
/// </summary>
public sealed record IterationOptionsConfig
{
    /// <summary>
    /// Maximum iterations before forced stop.
    /// </summary>
    public int MaxIterations { get; init; } = 25;

    /// <summary>
    /// Enable output-hash based dirty detection.
    /// </summary>
    public bool UseChangeAwareIteration { get; init; } = false;

    /// <summary>
    /// Auto-stop when all outputs unchanged between iterations.
    /// </summary>
    public bool EnableAutoConvergence { get; init; } = true;

    /// <summary>
    /// Fields to exclude from change detection.
    /// </summary>
    public List<string>? IgnoreFieldsForChangeDetection { get; init; }

    /// <summary>
    /// Nodes that always re-execute regardless of input changes.
    /// </summary>
    public List<string>? AlwaysDirtyNodes { get; init; }
}
