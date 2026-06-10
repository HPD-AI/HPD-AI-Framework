using System.Text.Json;

namespace HPD.Graph.Abstractions.Config;

/// <summary>
/// Serializable graph definition used for persistence, APIs, and workflow builders.
/// Runtime execution still uses Graph/Node/Edge.
/// </summary>
public sealed record GraphConfig
{
    public string SchemaVersion { get; init; } = "2.1";
    public required string GraphId { get; init; }
    public string GraphVersion { get; init; } = "1.0.0";
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string EntryNodeId { get; init; } = "START";
    public string ExitNodeId { get; init; } = "END";
    public int MaxIterations { get; init; } = 10;
    public TimeSpan? ExecutionTimeout { get; init; }
    public IterationOptionsConfig? IterationOptions { get; init; }
    public CloningPolicyConfig? CloningPolicy { get; init; }
    public IReadOnlyDictionary<string, NodeConfig> Nodes { get; init; } = new Dictionary<string, NodeConfig>();
    public IReadOnlyList<EdgeConfig> Edges { get; init; } = Array.Empty<EdgeConfig>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record NodeConfig
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required NodeKindConfig Type { get; init; }
    public string? HandlerName { get; init; }
    public JsonElement? Config { get; init; }
    public TimeSpan? Timeout { get; init; }
    public RetryPolicyConfig? RetryPolicy { get; init; }
    public ErrorPropagationPolicyConfig? ErrorPolicy { get; init; }
    public SuspensionOptionsConfig? SuspensionOptions { get; init; }
    public bool EnableCheckpointing { get; init; } = true;
    public int? MaxExecutions { get; init; }
    public int? MaxParallelExecutions { get; init; }
    public int OutputPortCount { get; init; } = 1;
    public string? SubGraphRef { get; init; }
    public GraphConfig? SubGraph { get; init; }
    public GraphConfig? MapProcessorGraph { get; init; }
    public string? MapProcessorGraphRef { get; init; }
    public int? MaxParallelMapTasks { get; init; }
    public string? MapInputChannel { get; init; }
    public string? MapOutputChannel { get; init; }
    public MapErrorModeConfig? MapErrorMode { get; init; }
    public string? MapItemType { get; init; }
    public string? MapResultType { get; init; }
    public IReadOnlyDictionary<string, GraphConfig>? MapProcessorGraphs { get; init; }
    public string? MapRouterName { get; init; }
    public GraphConfig? MapDefaultGraph { get; init; }
    public ArtifactDependencyConfig? Artifacts { get; init; }
    public PartitionDefinitionConfig? Partitions { get; init; }
    public PartitionDependencyConfig? PartitionDependencies { get; init; }
    public CacheOptionsConfig? Cache { get; init; }
    public IReadOnlyList<string>? ArtifactNamespace { get; init; }
    public IReadOnlyDictionary<string, InputSchemaConfig>? InputSchemas { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public enum MapErrorModeConfig
{
    FailFast,
    ContinueWithNulls,
    ContinueOmitFailures
}

public sealed record EdgeConfig
{
    public required string From { get; init; }
    public required string To { get; init; }
    public int? FromPort { get; init; }
    public int? ToPort { get; init; }
    public int? Priority { get; init; }
    public ConditionConfig? Condition { get; init; }
    public TimeSpan? Delay { get; init; }
    public ScheduleConstraintConfig? Schedule { get; init; }
    public EdgeRetryPolicyConfig? RetryPolicy { get; init; }
    public CloningPolicyConfig? CloningPolicy { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ConditionConfig
{
    public required ConditionKindConfig Type { get; init; }
    public string? Field { get; init; }
    public JsonElement? Value { get; init; }
    public IReadOnlyList<JsonElement>? Values { get; init; }
    public string? Pattern { get; init; }
    public bool IgnoreCase { get; init; }
    public IReadOnlyList<ConditionConfig>? All { get; init; }
    public IReadOnlyList<ConditionConfig>? Any { get; init; }
    public ConditionConfig? Not { get; init; }
}

public enum NodeKindConfig
{
    Start,
    End,
    Handler,
    Router,
    SubGraph,
    Map
}

public enum ConditionKindConfig
{
    Always,
    Default,
    FieldEquals,
    FieldNotEquals,
    FieldGreaterThan,
    FieldGreaterThanOrEqual,
    FieldLessThan,
    FieldLessThanOrEqual,
    FieldContains,
    FieldContainsAny,
    FieldContainsAll,
    FieldStartsWith,
    FieldEndsWith,
    FieldMatchesRegex,
    FieldExists,
    FieldNotExists,
    FieldEmpty,
    FieldNotEmpty,
    UpstreamOneSuccess,
    UpstreamAllDone,
    UpstreamAllDoneOneSuccess,
    All,
    Any,
    Not
}
