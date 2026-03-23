using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Caching;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Validation;

namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// A node in the execution graph.
/// Nodes represent units of work (handlers) or control flow (START/END/Router).
/// Immutable after construction.
/// </summary>
public sealed record Node
{
    /// <summary>
    /// Unique node identifier within the graph.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable node name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Node type (Start, End, Handler, Router, SubGraph).
    /// </summary>
    public required NodeType Type { get; init; }

    /// <summary>
    /// Handler name for Handler and Router nodes.
    /// Must match a registered IGraphNodeHandler.HandlerName.
    /// Null for Start/End nodes.
    /// </summary>
    public string? HandlerName { get; init; }

    /// <summary>
    /// Node-specific configuration.
    /// Deserialized into handler-specific config types at runtime.
    ///
    /// TODO (JSON graph definitions): Change this to JsonElement or string (raw JSON) to enable
    /// clean STJ roundtripping. Dictionary&lt;string, object&gt; does not deserialize safely —
    /// STJ loses type information on object values (everything becomes JsonElement anyway but
    /// without the original type context). Switching to JsonElement here unblocks full JSON
    /// graph definition support (SubGraphRef / MapProcessorGraphRef loader). All handlers
    /// that currently read Node.Config via GetNodeConfig&lt;T&gt;() would need updating to
    /// deserialize from JsonElement instead of casting object values.
    /// </summary>
    public IReadOnlyDictionary<string, object> Config { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// Timeout for this node execution.
    /// Null = no timeout (use graph-level default).
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Retry policy for this node.
    /// Null = no retries.
    /// </summary>
    public RetryPolicy? RetryPolicy { get; init; }

    /// <summary>
    /// Additional metadata (tags, labels, etc.).
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Enable checkpointing for this node.
    /// If true, node state is saved after execution.
    /// </summary>
    public bool EnableCheckpointing { get; init; } = true;

    /// <summary>
    /// Maximum number of times this node can execute (for loop detection).
    /// Null = unlimited.
    /// </summary>
    public int? MaxExecutions { get; init; }

    /// <summary>
    /// Sub-graph definition (for NodeType.SubGraph).
    /// Either embedded or referenced via SubGraphRef.
    /// </summary>
    public Graph? SubGraph { get; init; }

    /// <summary>
    /// Sub-graph reference (path or URI to graph definition).
    /// Alternative to inline SubGraph - load graph at runtime.
    /// </summary>
    public string? SubGraphRef { get; init; }

    /// <summary>
    /// Maximum number of parallel executions for this node within a layer.
    /// When limit is reached, additional executions wait for available slots.
    /// Null = unbounded parallelism (default).
    /// Use this to prevent resource exhaustion (e.g., database connections, API rate limits).
    /// Note: This limits concurrent executions in a layer, not input buffering.
    /// </summary>
    public int? MaxParallelExecutions { get; init; }

    /// <summary>
    /// Error propagation policy for this node.
    /// Defines how errors from this node affect downstream execution.
    /// Null = use graph-level default (StopGraph).
    /// </summary>
    public Execution.ErrorPropagationPolicy? ErrorPolicy { get; init; }

    /// <summary>
    /// Version identifier for this node's state schema.
    /// Used during checkpoint resume to validate compatibility.
    /// Increment when handler signature or state format changes (breaking changes).
    /// Default: "1.0"
    /// Examples: "1.0", "2.0", "2.1"
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Number of output ports this node has.
    /// Default: 1 (single output on port 0).
    /// Multi-output nodes declare N ports.
    /// IMPORTANT: Port 0 is the default/implicit port for single-output nodes.
    /// </summary>
    public int OutputPortCount { get; init; } = 1;

    // ===== ARTIFACT PROPERTIES (Data Orchestration Primitives - Phase 1) =====

    /// <summary>
    /// Declares that this node produces a named artifact.
    /// When set, node outputs are automatically registered in artifact registry.
    /// Artifact key is prefixed with SubGraph namespace if applicable (see Primitive 5).
    ///
    /// Example:
    ///   ProducesArtifact = ArtifactKey.FromPath("database", "users")
    ///   → Artifact "database/users" will be registered when node completes
    ///
    /// Enables:
    /// - Artifact-centric workflows (request "users_table@2025-01-15" not "execute graph X")
    /// - Data lineage tracking (what inputs created this artifact?)
    /// - Demand-driven execution (materialize artifact → auto-detect required nodes)
    /// </summary>
    public ArtifactKey? ProducesArtifact { get; init; }

    /// <summary>
    /// Declares artifact dependencies (alternative to explicit node dependencies).
    /// Orchestrator resolves which nodes produce these artifacts and builds dependency graph.
    /// Supports both explicit and namespace-relative keys.
    ///
    /// Example:
    ///   RequiresArtifacts = [
    ///     ArtifactKey.FromPath("database", "users"),
    ///     ArtifactKey.FromPath("database", "orders")
    ///   ]
    ///   → Orchestrator finds nodes that produce these artifacts
    ///   → Creates implicit edges from those nodes to this node
    ///
    /// Benefits:
    /// - Declarative dependencies (say WHAT you need, not HOW to get it)
    /// - Loose coupling (don't need to know specific node IDs)
    /// - Multi-producer support (automatically resolves best producer)
    /// </summary>
    public IReadOnlyList<ArtifactKey>? RequiresArtifacts { get; init; }

    // ===== MAP NODE PROPERTIES =====
    //
    // DECISION: Do you need a router?
    //
    // NO ROUTER (Homogeneous - use MapProcessorGraph):
    //   - All items are SAME TYPE and need SAME PROCESSING
    //   - Example: List of emails all need validation → use ONE graph
    //   - Set: MapProcessorGraph property
    //
    // YES ROUTER (Heterogeneous - use MapProcessorGraphs + MapRouterName):
    //   - Items are DIFFERENT TYPES and need DIFFERENT PROCESSING
    //   - Example: Mixed docs (PDF/Image/Video) each need specialized processing
    //   - Set: MapProcessorGraphs dictionary + MapRouterName + optional MapDefaultGraph
    //

    /// <summary>
    /// Processor graph for Map nodes (NodeType.Map).
    /// This graph is executed once per item in the input collection.
    /// Similar to SubGraph but with iteration semantics.
    ///
    /// Use when ALL items are SAME TYPE and need SAME PROCESSING (homogeneous).
    /// Mutually exclusive with MapProcessorGraphs (heterogeneous).
    /// </summary>
    public Graph? MapProcessorGraph { get; init; }

    /// <summary>
    /// Reference to processor graph for Map nodes (path or URI).
    /// Alternative to inline MapProcessorGraph - loaded at runtime.
    /// </summary>
    public string? MapProcessorGraphRef { get; init; }

    /// <summary>
    /// Maximum number of concurrent processor graph executions.
    /// - Null: Unbounded parallelism
    /// - 0: Auto (Environment.ProcessorCount)
    /// - N > 0: Limit to N concurrent executions
    /// Default: 0 (auto)
    /// </summary>
    public int? MaxParallelMapTasks { get; init; }

    /// <summary>
    /// Channel name to read input items from (for Map nodes).
    /// If not specified, reads from "node_output:{previousNodeId}".
    /// Input must be IEnumerable&lt;T&gt;.
    /// </summary>
    public string? MapInputChannel { get; init; }

    /// <summary>
    /// Channel name to write aggregated results to (for Map nodes).
    /// If not specified, writes to "node_output:{nodeId}".
    /// Uses Append channel semantics to preserve all results.
    /// </summary>
    public string? MapOutputChannel { get; init; }

    /// <summary>
    /// Error handling strategy for map processing.
    /// Default: FailFast
    /// </summary>
    public MapErrorMode? MapErrorMode { get; init; }

    /// <summary>
    /// Expected input item type name (optional validation).
    /// Example: "MyApp.Models.TextChunk"
    /// Used for runtime type checking and better error messages.
    /// </summary>
    public string? MapItemType { get; init; }

    /// <summary>
    /// Expected result type name (optional validation).
    /// Example: "System.Single[]"
    /// </summary>
    public string? MapResultType { get; init; }

    // ===== HETEROGENEOUS MAP PROPERTIES (v1.6) =====
    //
    // Use these properties when you have MIXED ITEM TYPES that need DIFFERENT PROCESSING.
    // The router inspects each item and returns a key to select which graph to use.
    //

    /// <summary>
    /// Multiple processor graphs for heterogeneous Map nodes, keyed by routing value.
    /// When specified, MapRouterName determines which graph to use per item.
    /// Example: { "pdf": pdfGraph, "image": imageGraph, "video": videoGraph }
    /// Mutually exclusive with MapProcessorGraph.
    ///
    /// REQUIRES: MapRouterName must be specified (router decides which graph per item)
    /// OPTIONAL: MapDefaultGraph provides fallback for unmatched routing values
    /// </summary>
    public IReadOnlyDictionary<string, Graph>? MapProcessorGraphs { get; init; }

    /// <summary>
    /// Name of the router to use for per-item routing.
    /// Must match an IMapRouter.RouterName registered in DI.
    /// Required when MapProcessorGraphs is specified.
    /// Example: "DocumentTypeRouter"
    /// Resolved from DI at runtime (same pattern as HandlerName).
    /// </summary>
    public string? MapRouterName { get; init; }

    /// <summary>
    /// Default processor graph to use if routing value doesn't match any key in MapProcessorGraphs.
    /// If null and no match found, behavior depends on MapErrorMode:
    /// - FailFast: Throws exception immediately
    /// - ContinueWithNulls: Adds null to results
    /// - ContinueOmitFailures: Skips item silently
    /// </summary>
    public Graph? MapDefaultGraph { get; init; }

    // ===== PARTITIONING (Phase 2: Data Orchestration Primitives) =====

    /// <summary>
    /// Declares that this node produces/consumes partitioned data.
    /// When set, node is executed once per partition key.
    /// If ProducesArtifact is also set, each execution produces artifactKey@partitionKey.
    /// Integrates with existing Map node infrastructure—partitions become input to Map iteration.
    ///
    /// Example (Daily partitions):
    ///   Partitions = TimePartitionDefinition.Daily(
    ///       start: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
    ///       end: new DateTimeOffset(2025, 1, 31, 0, 0, 0, TimeSpan.Zero)
    ///   )
    ///   → Generates 31 partitions: "2025-01-01", "2025-01-02", ..., "2025-01-31"
    ///
    /// Example (Regional partitions):
    ///   Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west", "eu-central")
    ///   → Generates 3 partitions
    ///
    /// Example (Multi-dimensional: daily × region):
    ///   Partitions = MultiPartitionDefinition.Combine(
    ///       TimePartitionDefinition.Daily(...),
    ///       StaticPartitionDefinition.FromKeys("us-east", "us-west")
    ///   )
    ///   → Generates Cartesian product: ["2025-01-01", "us-east"], ["2025-01-01", "us-west"], ...
    /// </summary>
    public PartitionDefinition? Partitions { get; init; }

    /// <summary>
    /// Maps output partitions to required input partitions (for cross-partition dependencies).
    /// Example: Weekly aggregation node maps week partition to 7 daily input partitions.
    /// Null means 1:1 partition alignment (output partition = input partition).
    ///
    /// Example (Weekly from Daily):
    ///   PartitionDependencies = PartitionDependencyMapping.WeeklyFromDaily()
    ///   Output partition "2025-W03" requires input partitions:
    ///   ["2025-01-15", "2025-01-16", ..., "2025-01-21"]
    ///
    /// Example (Monthly from Daily):
    ///   PartitionDependencies = PartitionDependencyMapping.MonthlyFromDaily()
    ///   Output partition "2025-01" requires all daily partitions in January
    /// </summary>
    public PartitionDependencyMapping? PartitionDependencies { get; init; }

    // ===== SUSPENSION OPTIONS (Layered Suspension) =====

    /// <summary>
    /// Options for suspension behavior when this node returns Suspended.
    /// Controls checkpointing, event emission, and active waiting.
    /// Null = use orchestrator defaults (30s active wait, events enabled, checkpoint first).
    /// </summary>
    public SuspensionOptions? SuspensionOptions { get; init; }

    // ===== PHASE 5: HIERARCHICAL NAMESPACES =====

    /// <summary>
    /// Namespace prefix for all artifacts produced by this subgraph (Phase 5: Hierarchical Namespaces).
    /// When set, all ProducesArtifact declarations in child nodes are automatically prefixed.
    ///
    /// Example:
    ///   SubGraph namespace: ["pipeline", "stage1"]
    ///   Child node produces: ["users"]
    ///   Actual artifact key: ["pipeline", "stage1", "users"]
    ///
    /// Prevents naming collisions when composing multiple subgraphs.
    /// Uses existing scope pattern from IGraphStateScope.
    ///
    /// Validation Rules:
    /// - Max depth: 10 levels (prevents excessive nesting)
    /// - Characters: Alphanumeric, hyphen, underscore only (a-zA-Z0-9_-)
    /// - No leading/trailing hyphens or underscores
    /// - No consecutive hyphens or underscores
    /// - Length: 1-50 characters per segment
    ///
    /// Best Practices:
    /// - Use 2-3 levels for most applications (e.g., ["team", "service"])
    /// - Prefer short, descriptive names (e.g., ["etl", "extract"] not ["extract_transform_load", "extraction_phase"])
    /// - Mirror organizational structure (e.g., ["sales", "reports", "daily"])
    /// </summary>
    public IReadOnlyList<string>? ArtifactNamespace { get; init; }

    /// <summary>
    /// Input validation schemas.
    /// Validated before handler execution (fail-fast).
    /// Keys are input names, values are schemas with type and constraint validation.
    /// Null = no validation (default, zero overhead).
    /// </summary>
    public IReadOnlyDictionary<string, InputSchema>? InputSchemas { get; init; }

    /// <summary>
    /// Cache configuration for this node.
    /// When set, orchestrator automatically caches results using INodeCacheStore.
    /// Cache key is computed based on Strategy (inputs, code, config).
    /// Null = no caching (default, zero overhead).
    /// </summary>
    public CacheOptions? Cache { get; init; }
}
