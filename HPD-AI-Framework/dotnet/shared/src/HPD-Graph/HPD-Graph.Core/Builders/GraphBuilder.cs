using System.Text.Json;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Core.Config;

// Alias for SuspensionOptions to avoid conflicts
using SuspensionOpts = HPDAgent.Graph.Abstractions.Execution.SuspensionOptions;

namespace HPDAgent.Graph.Core.Builders;

/// <summary>
/// Fluent API for programmatically constructing graphs.
/// Provides a chainable interface for adding nodes and edges.
/// </summary>
public class GraphBuilder
{
    private string? _id;
    private string? _name;
    private string _version = "1.0.0";
    private readonly List<Node> _nodes = new();
    private readonly List<Edge> _edges = new();
    private string _entryNodeId = "START";
    private string _exitNodeId = "END";
    private readonly Dictionary<string, string> _metadata = new();
    private int _maxIterations = 10;
    private TimeSpan? _executionTimeout;
    private Abstractions.Execution.CloningPolicy _cloningPolicy = Abstractions.Execution.CloningPolicy.LazyClone;
    private IterationOptions? _iterationOptions;
    private bool _autoSequentialEdges = true;
    private bool _autoSequentialEdgesExplicitlyConfigured;
    private GraphConfigCompilerOptions? _compilerOptions;

    /// <summary>
    /// Creates a new GraphBuilder instance.
    /// </summary>
    public GraphBuilder()
    {
        // Auto-generate ID if not specified
        _id = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Creates a graph builder seeded from a serializable graph configuration.
    /// </summary>
    public GraphBuilder(GraphConfig config, GraphConfigCompilerOptions? compilerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        _compilerOptions = compilerOptions;
        var graph = new GraphConfigCompiler(compilerOptions).Compile(config);
        LoadFromGraph(graph);

        // Config-authored graphs should preserve their declared topology exactly.
        _autoSequentialEdges = false;
        _autoSequentialEdgesExplicitlyConfigured = true;
    }

    /// <summary>
    /// Creates a graph builder seeded from a serializable graph configuration.
    /// </summary>
    public static GraphBuilder FromConfig(GraphConfig config, GraphConfigCompilerOptions? compilerOptions = null)
        => new(config, compilerOptions);

    /// <summary>
    /// Sets the graph ID.
    /// </summary>
    public GraphBuilder WithId(string id)
    {
        _id = id ?? throw new ArgumentNullException(nameof(id));
        return this;
    }

    /// <summary>
    /// Sets the graph name.
    /// </summary>
    public GraphBuilder WithName(string name)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        return this;
    }

    /// <summary>
    /// Sets the graph version.
    /// </summary>
    public GraphBuilder WithVersion(string version)
    {
        _version = version ?? throw new ArgumentNullException(nameof(version));
        return this;
    }

    /// <summary>
    /// Sets the entry node ID (default: "START").
    /// </summary>
    public GraphBuilder WithEntryNode(string entryNodeId)
    {
        _entryNodeId = entryNodeId ?? throw new ArgumentNullException(nameof(entryNodeId));
        return this;
    }

    /// <summary>
    /// Sets the exit node ID (default: "END").
    /// </summary>
    public GraphBuilder WithExitNode(string exitNodeId)
    {
        _exitNodeId = exitNodeId ?? throw new ArgumentNullException(nameof(exitNodeId));
        return this;
    }

    /// <summary>
    /// Sets the maximum iterations for cyclic graphs.
    /// </summary>
    public GraphBuilder WithMaxIterations(int maxIterations)
    {
        _maxIterations = maxIterations;
        return this;
    }

    /// <summary>
    /// Sets the global execution timeout.
    /// </summary>
    public GraphBuilder WithExecutionTimeout(TimeSpan timeout)
    {
        _executionTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Adds metadata to the graph.
    /// </summary>
    public GraphBuilder WithMetadata(string key, string value)
    {
        _metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Sets the graph-level cloning policy for output propagation.
    /// Default: LazyClone (optimal for most workloads).
    /// </summary>
    public GraphBuilder WithCloningPolicy(Abstractions.Execution.CloningPolicy policy)
    {
        _cloningPolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets iteration options for cyclic graphs (change-aware iteration, auto-convergence, etc.).
    /// When set, overrides <see cref="WithMaxIterations"/> for the max iterations value.
    /// </summary>
    public GraphBuilder WithIterationOptions(IterationOptions options)
    {
        _iterationOptions = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    /// Enables or disables automatic sequential edge wiring when no explicit edges are added.
    /// </summary>
    public GraphBuilder WithAutoSequentialEdges(bool enabled = true)
    {
        _autoSequentialEdges = enabled;
        _autoSequentialEdgesExplicitlyConfigured = true;
        return this;
    }

    /// <summary>
    /// Adds a node to the graph.
    /// </summary>
    public GraphBuilder AddNode(
        string id,
        string name,
        NodeType type,
        string? handlerName = null,
        Action<NodeBuilder>? configure = null)
    {
        var builder = new NodeBuilder(id, name, type, handlerName);
        configure?.Invoke(builder);
        _nodes.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Adds a START node to the graph.
    /// </summary>
    public GraphBuilder AddStartNode(string id = "START", string name = "Start")
    {
        _entryNodeId = id;
        _nodes.Add(new Node
        {
            Id = id,
            Name = name,
            Type = NodeType.Start
        });
        return this;
    }

    /// <summary>
    /// Adds an END node to the graph.
    /// </summary>
    public GraphBuilder AddEndNode(string id = "END", string name = "End")
    {
        _exitNodeId = id;
        _nodes.Add(new Node
        {
            Id = id,
            Name = name,
            Type = NodeType.End
        });
        return this;
    }

    /// <summary>
    /// Adds a handler node to the graph.
    /// </summary>
    public GraphBuilder AddHandlerNode(
        string id,
        string name,
        string handlerName,
        Action<NodeBuilder>? configure = null)
    {
        return AddNode(id, name, NodeType.Handler, handlerName, configure);
    }

    /// <summary>
    /// Adds a router node to the graph.
    /// </summary>
    public GraphBuilder AddRouterNode(
        string id,
        string name,
        string handlerName,
        Action<NodeBuilder>? configure = null)
    {
        return AddNode(id, name, NodeType.Router, handlerName, configure);
    }

    /// <summary>
    /// Adds a subgraph node to the graph.
    /// </summary>
    public GraphBuilder AddSubGraphNode(
        string id,
        string name,
        Abstractions.Graph.Graph subGraph,
        Action<NodeBuilder>? configure = null)
    {
        var builder = new NodeBuilder(id, name, NodeType.SubGraph, null);
        builder.WithSubGraph(subGraph);
        configure?.Invoke(builder);
        _nodes.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Adds an edge between two nodes.
    /// </summary>
    public GraphBuilder AddEdge(
        string from,
        string to,
        Action<EdgeBuilder>? configure = null)
    {
        var builder = new EdgeBuilder(from, to);
        configure?.Invoke(builder);
        _edges.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Starts fluent edge chaining from a source node.
    /// </summary>
    public NodeChainBuilder From(string sourceNodeId)
    {
        return From([sourceNodeId]);
    }

    /// <summary>
    /// Starts fluent edge chaining from one or more source nodes.
    /// </summary>
    public NodeChainBuilder From(params string[] sourceNodeIds)
    {
        return new NodeChainBuilder(this, sourceNodeIds);
    }

    /// <summary>
    /// Adds an unconditional edge between two nodes.
    /// </summary>
    public GraphBuilder AddEdge(string from, string to)
    {
        return AddEdge(from, to, null);
    }

    /// <summary>
    /// Adds an edge or replaces an existing edge with the same source, target, and ports.
    /// Useful for DSLs that register a route before applying its final condition.
    /// </summary>
    public GraphBuilder AddOrReplaceEdge(
        string from,
        string to,
        Action<EdgeBuilder>? configure = null)
    {
        var builder = new EdgeBuilder(from, to);
        configure?.Invoke(builder);
        var edge = builder.Build();

        var index = _edges.FindIndex(existing =>
            string.Equals(existing.From, edge.From, StringComparison.Ordinal) &&
            string.Equals(existing.To, edge.To, StringComparison.Ordinal) &&
            existing.FromPort == edge.FromPort &&
            existing.ToPort == edge.ToPort);

        if (index >= 0)
        {
            _edges[index] = edge;
        }
        else
        {
            _edges.Add(edge);
        }

        return this;
    }

    // ========================================
    // Upstream State Conditions
    // ========================================

    /// <summary>
    /// Set upstream state condition on all incoming edges to a node.
    /// WARNING: This will replace any existing data-based conditions on those edges.
    /// </summary>
    /// <param name="targetNodeId">Node ID to apply condition to</param>
    /// <param name="upstreamCondition">Upstream condition type</param>
    /// <returns>This builder for chaining</returns>
    /// <exception cref="ArgumentException">If condition is not an upstream condition type</exception>
    /// <exception cref="InvalidOperationException">If node has no incoming edges or edges have conflicting conditions</exception>
    public GraphBuilder WithUpstreamCondition(string targetNodeId, ConditionType upstreamCondition)
    {
        if (upstreamCondition != ConditionType.UpstreamOneSuccess &&
            upstreamCondition != ConditionType.UpstreamAllDone &&
            upstreamCondition != ConditionType.UpstreamAllDoneOneSuccess)
        {
            throw new ArgumentException(
                $"Condition type must be upstream condition, got: {upstreamCondition}",
                nameof(upstreamCondition));
        }

        // Find all edges pointing to targetNodeId
        var incomingEdges = _edges.Where(e => e.To == targetNodeId).ToList();

        if (incomingEdges.Count == 0)
            throw new InvalidOperationException($"Node {targetNodeId} has no incoming edges");

        // Check if any edges already have non-default conditions
        var edgesWithConditions = incomingEdges
            .Where(e => e.Condition != null &&
                        e.Condition.Type != ConditionType.Always)
            .ToList();

        if (edgesWithConditions.Count > 0)
        {
            var edgeList = string.Join(", ", edgesWithConditions.Select(e => $"{e.From} → {e.To}"));
            throw new InvalidOperationException(
                $"Cannot set upstream condition on node {targetNodeId}: " +
                $"The following edges already have conditions: {edgeList}. " +
                "Upstream conditions replace existing edge conditions. " +
                "Remove existing conditions first or use separate nodes for different conditions.");
        }

        // Set condition on all incoming edges
        for (int i = 0; i < _edges.Count; i++)
        {
            var edge = _edges[i];
            if (edge.To == targetNodeId)
            {
                _edges[i] = edge with
                {
                    Condition = new EdgeCondition
                    {
                        Type = upstreamCondition
                    }
                };
            }
        }

        return this;
    }

    /// <summary>
    /// Convenience: Execute if at least one upstream succeeded (parallel fallback).
    /// </summary>
    /// <param name="targetNodeId">Node ID to apply condition to</param>
    /// <returns>This builder for chaining</returns>
    public GraphBuilder RequireOneSuccess(string targetNodeId)
    {
        return WithUpstreamCondition(targetNodeId, ConditionType.UpstreamOneSuccess);
    }

    /// <summary>
    /// Convenience: Execute when all upstreams completed (aggregation).
    /// </summary>
    /// <param name="targetNodeId">Node ID to apply condition to</param>
    /// <returns>This builder for chaining</returns>
    public GraphBuilder RequireAllDone(string targetNodeId)
    {
        return WithUpstreamCondition(targetNodeId, ConditionType.UpstreamAllDone);
    }

    /// <summary>
    /// Convenience: Execute when all done AND at least one succeeded (partial success).
    /// </summary>
    /// <param name="targetNodeId">Node ID to apply condition to</param>
    /// <returns>This builder for chaining</returns>
    public GraphBuilder RequirePartialSuccess(string targetNodeId)
    {
        return WithUpstreamCondition(targetNodeId, ConditionType.UpstreamAllDoneOneSuccess);
    }

    /// <summary>
    /// Builds the graph.
    /// </summary>
    public Abstractions.Graph.Graph Build()
    {
        var graph = BuildRuntimeGraph();
        if (HasRuntimeOnlyState(graph))
        {
            return graph;
        }

        var config = new GraphConfigExporter().Export(graph);
        return new GraphConfigCompiler(_compilerOptions).Compile(config);
    }

    /// <summary>
    /// Builds a serializable graph configuration from the current builder state.
    /// </summary>
    public GraphConfig ToConfig()
        => new GraphConfigExporter().Export(BuildRuntimeGraph());

    private Abstractions.Graph.Graph BuildRuntimeGraph()
    {
        if (string.IsNullOrWhiteSpace(_name))
            throw new InvalidOperationException("Graph name is required. Call WithName() before Build().");

        // Ensure START and END nodes exist
        if (!_nodes.Any(n => n.Id == _entryNodeId))
        {
            AddStartNode(_entryNodeId);
        }

        if (!_nodes.Any(n => n.Id == _exitNodeId))
        {
            AddEndNode(_exitNodeId);
        }

        if (_autoSequentialEdges && _edges.Count == 0)
        {
            AddAutoSequentialEdges();
        }

        return new Abstractions.Graph.Graph
        {
            Id = _id!,
            Name = _name,
            Version = _version,
            Nodes = _nodes,
            Edges = _edges,
            EntryNodeId = _entryNodeId,
            ExitNodeId = _exitNodeId,
            Metadata = _metadata,
            MaxIterations = _maxIterations,
            ExecutionTimeout = _executionTimeout,
            CloningPolicy = _cloningPolicy,
            IterationOptions = _iterationOptions
        };
    }

    private void LoadFromGraph(Abstractions.Graph.Graph graph)
    {
        _id = graph.Id;
        _name = graph.Name;
        _version = graph.Version;
        _entryNodeId = graph.EntryNodeId;
        _exitNodeId = graph.ExitNodeId;
        _maxIterations = graph.MaxIterations;
        _executionTimeout = graph.ExecutionTimeout;
        _cloningPolicy = graph.CloningPolicy;
        _iterationOptions = graph.IterationOptions;

        _metadata.Clear();
        foreach (var (key, value) in graph.Metadata)
        {
            _metadata[key] = value;
        }

        _nodes.Clear();
        _nodes.AddRange(graph.Nodes);

        _edges.Clear();
        _edges.AddRange(graph.Edges);
    }

    private static bool HasRuntimeOnlyState(Abstractions.Graph.Graph graph)
    {
        return graph.Nodes.Any(node =>
                node.ErrorPolicy?.ShouldPropagate is not null ||
                HasRuntimeOnlySubGraph(node.SubGraph)) ||
            graph.Edges.Any(edge =>
                edge.Predicate is not null ||
                edge.Schedule?.AdditionalCondition is not null ||
                edge.RetryPolicy?.RetryCondition is not null);
    }

    private static bool HasRuntimeOnlySubGraph(Abstractions.Graph.Graph? graph)
        => graph is not null && HasRuntimeOnlyState(graph);

    internal void AddBuiltEdge(Edge edge)
    {
        _edges.Add(edge);
    }

    private void AddAutoSequentialEdges()
    {
        var sequence = _nodes
            .Where(node => node.Id != _entryNodeId && node.Id != _exitNodeId)
            .ToList();

        if (sequence.Count == 0)
        {
            return;
        }

        var canAutoWire = sequence.All(node => node.Type == NodeType.Handler);
        if (!canAutoWire && !_autoSequentialEdgesExplicitlyConfigured)
        {
            return;
        }

        var previous = _entryNodeId;
        foreach (var node in sequence)
        {
            _edges.Add(new Edge { From = previous, To = node.Id });
            previous = node.Id;
        }

        _edges.Add(new Edge { From = previous, To = _exitNodeId });
    }

    /// <summary>
    /// Creates a simple linear graph from a sequence of handler names.
    /// </summary>
    public static Abstractions.Graph.Graph Linear(string graphName, params string[] handlerNames)
    {
        if (handlerNames == null || handlerNames.Length == 0)
            throw new ArgumentException("At least one handler name is required", nameof(handlerNames));

        var builder = new GraphBuilder()
            .WithName(graphName)
            .AddStartNode();

        string previousNodeId = "START";

        for (int i = 0; i < handlerNames.Length; i++)
        {
            var nodeId = $"node_{i + 1}";
            var handlerName = handlerNames[i];

            builder.AddHandlerNode(nodeId, handlerName, handlerName);
            builder.AddEdge(previousNodeId, nodeId);

            previousNodeId = nodeId;
        }

        builder.AddEndNode();
        builder.AddEdge(previousNodeId, "END");

        return builder.Build();
    }
}

/// <summary>
/// Builder for individual nodes.
/// </summary>
public class NodeBuilder
{
    private readonly string _id;
    private readonly string _name;
    private readonly NodeType _type;
    private readonly string? _handlerName;
    private readonly Dictionary<string, object> _config = new();
    private TimeSpan? _timeout;
    private RetryPolicy? _retryPolicy;
    private readonly Dictionary<string, string> _metadata = new();
    private bool _enableCheckpointing = true;
    private int? _maxExecutions;
    private Abstractions.Graph.Graph? _subGraph;
    private string? _subGraphRef;
    private int? _maxInputBufferSize;
    private ErrorPropagationPolicy? _errorPolicy;
    private SuspensionOpts? _suspensionOptions;
    private int _outputPortCount = 1;
    private Dictionary<string, Abstractions.Validation.InputSchema>? _inputSchemas;
    private Abstractions.Caching.CacheOptions? _cache;
    private ArtifactKey? _producesArtifact;
    private IReadOnlyList<ArtifactKey>? _requiresArtifacts;
    private PartitionDefinition? _partitions;
    private PartitionDependencyMapping? _partitionDependencies;
    private IReadOnlyList<string>? _artifactNamespace;

    internal NodeBuilder(string id, string name, NodeType type, string? handlerName)
    {
        _id = id;
        _name = name;
        _type = type;
        _handlerName = handlerName;
    }

    /// <summary>
    /// Adds a configuration key-value pair.
    /// </summary>
    public NodeBuilder WithConfig(string key, object value)
    {
        _config[key] = value;
        return this;
    }

    /// <summary>
    /// Replaces the node configuration with raw JSON.
    /// </summary>
    public NodeBuilder WithConfig(JsonElement config)
    {
        _config.Clear();
        _config["$value"] = config.Clone();
        return this;
    }

    /// <summary>
    /// Sets the node timeout.
    /// </summary>
    public NodeBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the retry policy.
    /// </summary>
    public NodeBuilder WithRetryPolicy(RetryPolicy policy)
    {
        _retryPolicy = policy;
        return this;
    }

    /// <summary>
    /// Adds metadata.
    /// </summary>
    public NodeBuilder WithMetadata(string key, string value)
    {
        _metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Enables or disables checkpointing.
    /// </summary>
    public NodeBuilder WithCheckpointing(bool enabled)
    {
        _enableCheckpointing = enabled;
        return this;
    }

    /// <summary>
    /// Sets the maximum execution count.
    /// </summary>
    public NodeBuilder WithMaxExecutions(int maxExecutions)
    {
        _maxExecutions = maxExecutions;
        return this;
    }

    /// <summary>
    /// Sets the subgraph for SubGraph nodes.
    /// </summary>
    public NodeBuilder WithSubGraph(Abstractions.Graph.Graph subGraph)
    {
        _subGraph = subGraph;
        return this;
    }

    /// <summary>
    /// Sets the subgraph reference for SubGraph nodes.
    /// </summary>
    public NodeBuilder WithSubGraphRef(string subGraphRef)
    {
        _subGraphRef = subGraphRef;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of parallel executions for this node.
    /// </summary>
    public NodeBuilder WithMaxParallelExecutions(int maxParallelExecutions)
    {
        _maxInputBufferSize = maxParallelExecutions;
        return this;
    }

    /// <summary>
    /// Sets the error propagation policy.
    /// </summary>
    public NodeBuilder WithErrorPolicy(ErrorPropagationPolicy policy)
    {
        _errorPolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets the suspension options for this node.
    /// Controls behavior when handler returns Suspended result.
    /// </summary>
    public NodeBuilder WithSuspensionOptions(SuspensionOpts options)
    {
        _suspensionOptions = options;
        return this;
    }

    /// <summary>
    /// Configures active wait timeout for suspension.
    /// Convenience method for common case.
    /// </summary>
    /// <param name="timeout">How long to wait for approval response.</param>
    public NodeBuilder WithActiveWait(TimeSpan timeout)
    {
        _suspensionOptions = new SuspensionOpts { ActiveWaitTimeout = timeout };
        return this;
    }

    /// <summary>
    /// Configures immediate suspend (no waiting).
    /// Use when approval may take hours/days and caller will resume from checkpoint.
    /// </summary>
    public NodeBuilder WithImmediateSuspend()
    {
        _suspensionOptions = SuspensionOpts.ImmediateSuspend;
        return this;
    }

    /// <summary>
    /// Sets the number of output ports for this node.
    /// Default: 1 (single output on port 0).
    /// Use for multi-output routing patterns (e.g., routers, splitters).
    /// </summary>
    public NodeBuilder WithOutputPorts(int portCount)
    {
        if (portCount < 1)
            throw new ArgumentOutOfRangeException(nameof(portCount), "Port count must be at least 1");
        _outputPortCount = portCount;
        return this;
    }

    /// <summary>
    /// Declares the artifact produced by this node.
    /// </summary>
    public NodeBuilder WithProducesArtifact(ArtifactKey artifactKey)
    {
        _producesArtifact = artifactKey ?? throw new ArgumentNullException(nameof(artifactKey));
        return this;
    }

    /// <summary>
    /// Declares artifact dependencies for this node.
    /// </summary>
    public NodeBuilder WithRequiresArtifacts(params ArtifactKey[] artifactKeys)
    {
        ArgumentNullException.ThrowIfNull(artifactKeys);
        _requiresArtifacts = artifactKeys.ToArray();
        return this;
    }

    /// <summary>
    /// Declares artifact dependencies for this node.
    /// </summary>
    public NodeBuilder WithRequiresArtifacts(IEnumerable<ArtifactKey> artifactKeys)
    {
        ArgumentNullException.ThrowIfNull(artifactKeys);
        _requiresArtifacts = artifactKeys.ToArray();
        return this;
    }

    /// <summary>
    /// Declares the partitions produced or consumed by this node.
    /// </summary>
    public NodeBuilder WithPartitions(PartitionDefinition partitions)
    {
        _partitions = partitions ?? throw new ArgumentNullException(nameof(partitions));
        return this;
    }

    /// <summary>
    /// Declares how output partitions map to required input partitions.
    /// </summary>
    public NodeBuilder WithPartitionDependencies(PartitionDependencyMapping partitionDependencies)
    {
        _partitionDependencies = partitionDependencies ?? throw new ArgumentNullException(nameof(partitionDependencies));
        return this;
    }

    /// <summary>
    /// Declares a namespace prefix for artifacts produced by this node or subgraph.
    /// </summary>
    public NodeBuilder WithArtifactNamespace(params string[] namespaceSegments)
    {
        ArgumentNullException.ThrowIfNull(namespaceSegments);
        _artifactNamespace = namespaceSegments.ToArray();
        return this;
    }

    /// <summary>
    /// Declares a namespace prefix for artifacts produced by this node or subgraph.
    /// </summary>
    public NodeBuilder WithArtifactNamespace(IEnumerable<string> namespaceSegments)
    {
        ArgumentNullException.ThrowIfNull(namespaceSegments);
        _artifactNamespace = namespaceSegments.ToArray();
        return this;
    }

    internal Node Build()
    {
        return new Node
        {
            Id = _id,
            Name = _name,
            Type = _type,
            HandlerName = _handlerName,
            Config = BuildConfig(),
            Timeout = _timeout,
            RetryPolicy = _retryPolicy,
            Metadata = _metadata,
            EnableCheckpointing = _enableCheckpointing,
            MaxExecutions = _maxExecutions,
            SubGraph = _subGraph,
            SubGraphRef = _subGraphRef,
            MaxParallelExecutions = _maxInputBufferSize,
            ErrorPolicy = _errorPolicy,
            SuspensionOptions = _suspensionOptions,
            OutputPortCount = _outputPortCount,
            InputSchemas = _inputSchemas,
            Cache = _cache,
            ProducesArtifact = _producesArtifact,
            RequiresArtifacts = _requiresArtifacts,
            Partitions = _partitions,
            PartitionDependencies = _partitionDependencies,
            ArtifactNamespace = _artifactNamespace
        };
    }

    private JsonElement? BuildConfig()
    {
        if (_config.Count == 0)
        {
            return null;
        }

        if (_config.Count == 1 &&
            _config.TryGetValue("$value", out var rawConfig) &&
            rawConfig is JsonElement rawElement)
        {
            return rawElement.Clone();
        }

        return GraphJsonValue.ToJsonElement(_config, "node config");
    }

    /// <summary>
    /// Adds an input schema for validation.
    /// </summary>
    public NodeBuilder WithInputSchema(string inputName, Abstractions.Validation.InputSchema schema)
    {
        _inputSchemas ??= new Dictionary<string, Abstractions.Validation.InputSchema>();
        _inputSchemas[inputName] = schema;
        return this;
    }

    /// <summary>
    /// Sets cache configuration for this node.
    /// </summary>
    public NodeBuilder WithCache(Abstractions.Caching.CacheOptions cache)
    {
        _cache = cache;
        return this;
    }
}

/// <summary>
/// Builder for individual edges.
/// </summary>
public class EdgeBuilder
{
    private readonly string _from;
    private readonly string _to;
    private EdgeCondition? _condition;
    private Func<EdgePredicateContext, bool>? _predicate;
    private readonly Dictionary<string, string> _metadata = new();
    private int? _fromPort;
    private int? _toPort;
    private int? _priority;
    private Abstractions.Execution.CloningPolicy? _cloningPolicy;
    private TimeSpan? _delay;
    private ScheduleConstraint? _schedule;
    private EdgeRetryPolicy? _retryPolicy;

    internal EdgeBuilder(string from, string to)
    {
        _from = from;
        _to = to;
    }

    /// <summary>
    /// Sets the edge condition.
    /// </summary>
    public EdgeBuilder WithCondition(EdgeCondition condition)
    {
        _condition = condition;
        return this;
    }

    /// <summary>
    /// Sets a runtime-only predicate for this edge.
    /// Predicates are intentionally not serialized. Use WithCondition for persisted graphs.
    /// </summary>
    public EdgeBuilder When(Func<EdgePredicateContext, bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    /// <summary>
    /// Sets the source output port number (0-indexed).
    /// </summary>
    public EdgeBuilder FromPort(int portNumber)
    {
        if (portNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(portNumber), "Port number must be non-negative");
        _fromPort = portNumber;
        return this;
    }

    /// <summary>
    /// Sets the destination input port number (0-indexed).
    /// Reserved for future multi-input support.
    /// </summary>
    public EdgeBuilder ToPort(int portNumber)
    {
        if (portNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(portNumber), "Port number must be non-negative");
        _toPort = portNumber;
        return this;
    }

    /// <summary>
    /// Sets the priority for edge traversal ordering (lower = higher priority).
    /// Used to ensure deterministic lazy cloning behavior.
    /// </summary>
    public EdgeBuilder WithPriority(int priority)
    {
        if (priority < 0)
            throw new ArgumentOutOfRangeException(nameof(priority), "Priority must be non-negative");
        _priority = priority;
        return this;
    }

    /// <summary>
    /// Overrides the graph-level cloning policy for this specific edge.
    /// Use to optimize specific edges (e.g., NeverClone for read-only handlers).
    /// </summary>
    public EdgeBuilder WithCloningPolicy(Abstractions.Execution.CloningPolicy policy)
    {
        _cloningPolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets a delay before traversing this edge.
    /// </summary>
    public EdgeBuilder WithDelay(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay must be non-negative");
        _delay = delay;
        return this;
    }

    /// <summary>
    /// Sets a schedule constraint for this edge.
    /// </summary>
    public EdgeBuilder WithSchedule(ScheduleConstraint schedule)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        return this;
    }

    /// <summary>
    /// Sets a cron schedule constraint for this edge using a timezone ID.
    /// </summary>
    public EdgeBuilder WithCron(
        string cronExpression,
        string? timeZoneId = null,
        TimeSpan? tolerance = null,
        Func<IGraphContext, Task<bool>>? additionalCondition = null)
    {
        var timeZone = string.IsNullOrWhiteSpace(timeZoneId)
            ? null
            : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        return WithCron(cronExpression, timeZone, tolerance, additionalCondition);
    }

    /// <summary>
    /// Sets a cron schedule constraint for this edge.
    /// </summary>
    public EdgeBuilder WithCron(
        string cronExpression,
        TimeZoneInfo? timeZone,
        TimeSpan? tolerance = null,
        Func<IGraphContext, Task<bool>>? additionalCondition = null)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("Cron expression is required.", nameof(cronExpression));
        if (tolerance.HasValue && tolerance.Value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be non-negative");

        return WithSchedule(new ScheduleConstraint
        {
            CronExpression = cronExpression,
            TimeZone = timeZone,
            Tolerance = tolerance ?? TimeSpan.FromMinutes(1),
            AdditionalCondition = additionalCondition
        });
    }

    /// <summary>
    /// Sets an edge-level retry policy for this edge.
    /// </summary>
    public EdgeBuilder WithRetryPolicy(EdgeRetryPolicy retryPolicy)
    {
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        return this;
    }

    /// <summary>
    /// Sets an edge-level retry policy using a retry interval.
    /// </summary>
    public EdgeBuilder RetryEvery(
        TimeSpan retryInterval,
        TimeSpan? maxWaitTime = null,
        int? maxRetries = null,
        EdgeRetryExhaustedBehavior exhaustedBehavior = EdgeRetryExhaustedBehavior.FailGraph,
        Func<IGraphContext, Task<bool>>? retryCondition = null)
    {
        if (retryInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryInterval), "Retry interval must be positive");
        if (maxWaitTime.HasValue && maxWaitTime.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxWaitTime), "Max wait time must be positive");
        if (maxRetries.HasValue && maxRetries.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries must be positive");

        return WithRetryPolicy(new EdgeRetryPolicy
        {
            RetryInterval = retryInterval,
            MaxWaitTime = maxWaitTime,
            MaxRetries = maxRetries,
            ExhaustedBehavior = exhaustedBehavior,
            RetryCondition = retryCondition
        });
    }

    /// <summary>
    /// Sets an edge-level retry policy using a retry interval.
    /// </summary>
    public EdgeBuilder WithRetry(
        TimeSpan retryInterval,
        TimeSpan? maxWaitTime = null,
        int? maxRetries = null,
        EdgeRetryExhaustedBehavior exhaustedBehavior = EdgeRetryExhaustedBehavior.FailGraph,
        Func<IGraphContext, Task<bool>>? retryCondition = null)
    {
        return RetryEvery(retryInterval, maxWaitTime, maxRetries, exhaustedBehavior, retryCondition);
    }

    /// <summary>
    /// Adds metadata.
    /// </summary>
    public EdgeBuilder WithMetadata(string key, string value)
    {
        _metadata[key] = value;
        return this;
    }

    internal Edge Build()
    {
        return new Edge
        {
            From = _from,
            To = _to,
            FromPort = _fromPort,
            ToPort = _toPort,
            Priority = _priority,
            Condition = _condition,
            Predicate = _predicate,
            CloningPolicy = _cloningPolicy,
            Delay = _delay,
            Schedule = _schedule,
            RetryPolicy = _retryPolicy,
            Metadata = _metadata
        };
    }
}

/// <summary>
/// Builder for fluent edges from one source node.
/// </summary>
public sealed class NodeChainBuilder
{
    private readonly GraphBuilder _graphBuilder;
    private readonly IReadOnlyList<string> _sourceNodeIds;
    private int? _sourcePort;

    internal NodeChainBuilder(GraphBuilder graphBuilder, IReadOnlyList<string> sourceNodeIds)
    {
        _graphBuilder = graphBuilder ?? throw new ArgumentNullException(nameof(graphBuilder));
        if (sourceNodeIds.Count == 0)
            throw new ArgumentException("At least one source node is required.", nameof(sourceNodeIds));
        if (sourceNodeIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Source node IDs cannot be empty.", nameof(sourceNodeIds));

        _sourceNodeIds = sourceNodeIds;
    }

    public NodeChainBuilder Port(int sourcePort)
    {
        if (sourcePort < 0)
            throw new ArgumentOutOfRangeException(nameof(sourcePort), "Port number must be non-negative");
        _sourcePort = sourcePort;
        return this;
    }

    public EdgeTargetBuilder To(string targetNodeId)
    {
        return To([targetNodeId]);
    }

    public EdgeTargetBuilder To(params string[] targetNodeIds)
    {
        if (targetNodeIds.Length == 0)
            throw new ArgumentException("At least one target node is required.", nameof(targetNodeIds));
        if (targetNodeIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Target node IDs cannot be empty.", nameof(targetNodeIds));

        var edges = new List<EdgeBuilder>();
        foreach (var sourceNodeId in _sourceNodeIds)
        {
            foreach (var targetNodeId in targetNodeIds)
            {
                var edge = new EdgeBuilder(sourceNodeId, targetNodeId);
                if (_sourcePort.HasValue)
                {
                    edge.FromPort(_sourcePort.Value);
                }

                edges.Add(edge);
            }
        }

        return new EdgeTargetBuilder(_graphBuilder, this, edges);
    }

    public FieldRouteBuilder RouteBy(string field)
    {
        return new FieldRouteBuilder(this, field);
    }

    public GraphBuilder Done()
    {
        return _graphBuilder;
    }
}

/// <summary>
/// Builder for configuring a fluent edge target.
/// </summary>
public sealed class EdgeTargetBuilder
{
    private readonly GraphBuilder _graphBuilder;
    private readonly NodeChainBuilder _chainBuilder;
    private readonly IReadOnlyList<EdgeBuilder> _edgeBuilders;
    private readonly IReadOnlyList<string> _targetNodeIds;
    private bool _committed;

    internal EdgeTargetBuilder(GraphBuilder graphBuilder, NodeChainBuilder chainBuilder, IReadOnlyList<EdgeBuilder> edgeBuilders)
    {
        _graphBuilder = graphBuilder;
        _chainBuilder = chainBuilder;
        _edgeBuilders = edgeBuilders;
        _targetNodeIds = edgeBuilders
            .Select(edgeBuilder => edgeBuilder.Build().To)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public EdgeTargetBuilder ToPort(int targetPort)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.ToPort(targetPort);
        }
        return this;
    }

    public EdgeTargetBuilder WhenEquals(string field, object? value) =>
        WithFieldCondition(ConditionType.FieldEquals, field, value);

    public EdgeTargetBuilder WhenNotEquals(string field, object? value) =>
        WithFieldCondition(ConditionType.FieldNotEquals, field, value);

    public EdgeTargetBuilder WhenGreaterThan(string field, IComparable value) =>
        WithFieldCondition(ConditionType.FieldGreaterThan, field, value);

    public EdgeTargetBuilder WhenGreaterThanOrEqual(string field, IComparable value) =>
        WithFieldCondition(ConditionType.FieldGreaterThanOrEqual, field, value);

    public EdgeTargetBuilder WhenLessThan(string field, IComparable value) =>
        WithFieldCondition(ConditionType.FieldLessThan, field, value);

    public EdgeTargetBuilder WhenLessThanOrEqual(string field, IComparable value) =>
        WithFieldCondition(ConditionType.FieldLessThanOrEqual, field, value);

    public EdgeTargetBuilder WhenContains(string field, object value) =>
        WithFieldCondition(ConditionType.FieldContains, field, value);

    public EdgeTargetBuilder WhenContainsAny(string field, params object[] values) =>
        WithFieldCondition(ConditionType.FieldContainsAny, field, values);

    public EdgeTargetBuilder WhenContainsAll(string field, params object[] values) =>
        WithFieldCondition(ConditionType.FieldContainsAll, field, values);

    public EdgeTargetBuilder WhenStartsWith(string field, string value, bool ignoreCase = false) =>
        WithFieldCondition(ConditionType.FieldStartsWith, field, value, ignoreCase);

    public EdgeTargetBuilder WhenEndsWith(string field, string value, bool ignoreCase = false) =>
        WithFieldCondition(ConditionType.FieldEndsWith, field, value, ignoreCase);

    public EdgeTargetBuilder WhenMatchesRegex(string field, string pattern) =>
        WithFieldCondition(ConditionType.FieldMatchesRegex, field, pattern);

    public EdgeTargetBuilder WhenExists(string field) =>
        WithFieldCondition(ConditionType.FieldExists, field, null);

    public EdgeTargetBuilder WhenNotExists(string field) =>
        WithFieldCondition(ConditionType.FieldNotExists, field, null);

    public EdgeTargetBuilder WhenEmpty(string field) =>
        WithFieldCondition(ConditionType.FieldIsEmpty, field, null);

    public EdgeTargetBuilder WhenNotEmpty(string field) =>
        WithFieldCondition(ConditionType.FieldIsNotEmpty, field, null);

    /// <summary>
    /// Sets a runtime-only predicate for this edge.
    /// Predicates are intentionally not serialized. Use declarative condition helpers for persisted graphs.
    /// </summary>
    public EdgeTargetBuilder When(Func<EdgePredicateContext, bool> predicate)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.When(predicate);
        }

        return this;
    }

    public EdgeTargetBuilder AsDefault()
    {
        return WithCondition(new EdgeCondition { Type = ConditionType.Default });
    }

    public EdgeTargetBuilder WithDelay(TimeSpan delay)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithDelay(delay);
        }
        return this;
    }

    public EdgeTargetBuilder WithSchedule(ScheduleConstraint schedule)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithSchedule(schedule);
        }
        return this;
    }

    public EdgeTargetBuilder WithCron(
        string cronExpression,
        string? timeZoneId = null,
        TimeSpan? tolerance = null,
        Func<IGraphContext, Task<bool>>? additionalCondition = null)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithCron(cronExpression, timeZoneId, tolerance, additionalCondition);
        }
        return this;
    }

    public EdgeTargetBuilder WithCron(
        string cronExpression,
        TimeZoneInfo? timeZone,
        TimeSpan? tolerance = null,
        Func<IGraphContext, Task<bool>>? additionalCondition = null)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithCron(cronExpression, timeZone, tolerance, additionalCondition);
        }
        return this;
    }

    public EdgeTargetBuilder WithRetryPolicy(EdgeRetryPolicy retryPolicy)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithRetryPolicy(retryPolicy);
        }
        return this;
    }

    public EdgeTargetBuilder RetryEvery(
        TimeSpan retryInterval,
        TimeSpan? maxWaitTime = null,
        int? maxRetries = null,
        EdgeRetryExhaustedBehavior exhaustedBehavior = EdgeRetryExhaustedBehavior.FailGraph,
        Func<IGraphContext, Task<bool>>? retryCondition = null)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.RetryEvery(retryInterval, maxWaitTime, maxRetries, exhaustedBehavior, retryCondition);
        }
        return this;
    }

    public EdgeTargetBuilder WithRetry(
        TimeSpan retryInterval,
        TimeSpan? maxWaitTime = null,
        int? maxRetries = null,
        EdgeRetryExhaustedBehavior exhaustedBehavior = EdgeRetryExhaustedBehavior.FailGraph,
        Func<IGraphContext, Task<bool>>? retryCondition = null)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithRetry(retryInterval, maxWaitTime, maxRetries, exhaustedBehavior, retryCondition);
        }
        return this;
    }

    public EdgeTargetBuilder WithPriority(int priority)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithPriority(priority);
        }
        return this;
    }

    public EdgeTargetBuilder WithCloningPolicy(Abstractions.Execution.CloningPolicy policy)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithCloningPolicy(policy);
        }
        return this;
    }

    public EdgeTargetBuilder WithMetadata(string key, string value)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithMetadata(key, value);
        }
        return this;
    }

    public NodeChainBuilder And()
    {
        Commit();
        return _chainBuilder;
    }

    public EdgeTargetBuilder To(string nextTargetNodeId)
    {
        Commit();
        return ContinueFromTargets().To(nextTargetNodeId);
    }

    public EdgeTargetBuilder To(params string[] nextTargetNodeIds)
    {
        Commit();
        return ContinueFromTargets().To(nextTargetNodeIds);
    }

    public GraphBuilder Done()
    {
        Commit();
        return _graphBuilder;
    }

    private EdgeTargetBuilder WithFieldCondition(ConditionType type, string field, object? value, bool ignoreCase = false)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field is required.", nameof(field));

        return WithCondition(new EdgeCondition
        {
            Type = type,
            Field = field,
            Value = value,
            RegexOptions = ignoreCase ? "IgnoreCase" : null
        });
    }

    private EdgeTargetBuilder WithCondition(EdgeCondition condition)
    {
        foreach (var edgeBuilder in _edgeBuilders)
        {
            edgeBuilder.WithCondition(condition);
        }

        return this;
    }

    private void Commit()
    {
        if (_committed)
        {
            return;
        }

        foreach (var edgeBuilder in _edgeBuilders)
        {
            _graphBuilder.AddBuiltEdge(edgeBuilder.Build());
        }
        _committed = true;
    }

    private NodeChainBuilder ContinueFromTargets()
    {
        return new NodeChainBuilder(_graphBuilder, _targetNodeIds);
    }
}

/// <summary>
/// Builder for routing one or more source nodes to targets by a source output field.
/// </summary>
public sealed class FieldRouteBuilder
{
    private readonly NodeChainBuilder _chainBuilder;
    private readonly string _field;

    internal FieldRouteBuilder(NodeChainBuilder chainBuilder, string field)
    {
        _chainBuilder = chainBuilder;
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field is required.", nameof(field));
        _field = field;
    }

    public FieldRouteBuilder When(object? value, params string[] targetNodeIds)
    {
        _chainBuilder.To(targetNodeIds).WhenEquals(_field, value).Done();
        return this;
    }

    public FieldRouteBuilder WhenAny(IEnumerable<object?> values, params string[] targetNodeIds)
    {
        ArgumentNullException.ThrowIfNull(values);
        _chainBuilder.To(targetNodeIds).WhenContainsAny(_field, values.ToArray()).Done();
        return this;
    }

    public FieldRouteBuilder Default(params string[] targetNodeIds)
    {
        _chainBuilder.To(targetNodeIds).AsDefault().Done();
        return this;
    }

    public NodeChainBuilder And()
    {
        return _chainBuilder;
    }

    public GraphBuilder Done()
    {
        return _chainBuilder.Done();
    }
}
