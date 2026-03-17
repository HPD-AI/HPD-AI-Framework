using System.Text.Json;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Serialization;
using HPD.RAG.Pipeline.Internal;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Pipeline;

/// <summary>
/// Fluent builder for constructing MRAG pipelines backed by HPD.Graph.
///
/// <para>Usage pattern:</para>
/// <code>
/// var pipeline = await MragPipeline.Create()
///     .WithName("IngestionV1")
///     .AddHandler("read",  MragHandlerNames.ReadMarkdown)
///     .AddHandler("chunk", MragHandlerNames.ChunkByHeader)
///     .AddHandler("write", MragHandlerNames.WriteInMemory)
///     .From("START").To("read").To("chunk").To("write").To("END").Done()
///     .BuildIngestionAsync();
/// </code>
///
/// <para>Mirrors the MultiAgent / GraphBuilder pattern exactly.</para>
/// </summary>
public class MragPipeline
{
    // ------------------------------------------------------------------ //
    // Internal state                                                       //
    // ------------------------------------------------------------------ //

    private string? _name;
    private readonly GraphBuilder _graphBuilder = new();

    // Node tracking — keyed by nodeId
    private readonly HashSet<string> _nodeIds = new(StringComparer.Ordinal);

    // Pending edges: (from, to, fromPort?, condition?)
    private readonly List<(string From, string To, int? FromPort, EdgeCondition? Condition)> _edges = new();

    // Adapters to register into the graph service provider
    // Stores: (handler name string, DI type to resolve at execution time)
    private readonly List<(string HandlerName, Type ServiceType)> _adapterRegistrations = new();

    // Compiled inner graph (set by BuildSubPipelineAsync)
    internal Graph? _compiledSubGraph;

    private MragPipeline() { }

    // ------------------------------------------------------------------ //
    // Factory                                                              //
    // ------------------------------------------------------------------ //

    /// <summary>Creates a new pipeline builder.</summary>
    public static MragPipeline Create() => new();

    // ------------------------------------------------------------------ //
    // Configuration                                                        //
    // ------------------------------------------------------------------ //

    /// <summary>Sets the pipeline name. Required before calling any Build*Async method.</summary>
    public MragPipeline WithName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pipeline name cannot be empty.", nameof(name));
        _name = name;
        return this;
    }

    /// <summary>Sets the maximum graph iteration count for cyclic pipelines.</summary>
    public MragPipeline WithMaxIterations(int maxIterations)
    {
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations),
                "Max iterations must be greater than zero.");
        _graphBuilder.WithMaxIterations(maxIterations);
        return this;
    }

    /// <summary>Sets the global execution timeout.</summary>
    public MragPipeline WithTimeout(TimeSpan timeout)
    {
        _graphBuilder.WithExecutionTimeout(timeout);
        return this;
    }

    // ------------------------------------------------------------------ //
    // Node registration                                                    //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Adds a handler node backed by a registered <c>[GraphNodeHandler]</c>.
    /// </summary>
    /// <typeparam name="TConfig">
    ///   The handler's typed <c>Config</c> class. Omit or use <c>object</c> when no config is needed.
    /// </typeparam>
    /// <param name="nodeId">Unique node identifier within this pipeline.</param>
    /// <param name="handlerName">Handler name (use <see cref="MragHandlerNames"/> constants).</param>
    /// <param name="configure">Optional lambda to populate handler config.</param>
    /// <param name="options">Optional node-level resilience overrides.</param>
    public MragPipeline AddHandler<TConfig>(
        string nodeId,
        string handlerName,
        Action<TConfig>? configure = null,
        Action<MragNodeOptions>? options = null)
        where TConfig : new()
    {
        ValidateNodeId(nodeId);
        ValidateHandlerName(handlerName);

        var nodeOptions = new MragNodeOptions();
        options?.Invoke(nodeOptions);

        _graphBuilder.AddHandlerNode(nodeId, nodeId, handlerName, nb =>
        {
            ApplyNodeOptions(nb, nodeOptions);

            if (configure != null)
            {
                var config = new TConfig();
                configure(config);
                // Serialize TConfig to JSON and store as the "config" key.
                // The HPD.Graph source-generated handler bridge deserialises "config"
                // back to its TConfig at execution time.
                var configJson = JsonSerializer.Serialize(
                    config,
                    MragJsonSerializerContext.Shared.Options);
                nb.WithConfig("config", configJson);
            }
        });

        _nodeIds.Add(nodeId);
        return this;
    }

    /// <summary>
    /// Adds a handler node that carries no typed config.
    /// </summary>
    public MragPipeline AddHandler(
        string nodeId,
        string handlerName,
        Action<MragNodeOptions>? options = null)
        => AddHandler<object>(nodeId, handlerName, configure: null, options: options);

    /// <summary>
    /// Adds a Map (fan-out / fan-in) stage backed by a compiled sub-pipeline.
    /// The sub-pipeline must first be compiled via <see cref="BuildSubPipelineAsync"/>.
    /// </summary>
    public MragPipeline AddMapStage(
        string nodeId,
        MragPipeline subPipeline,
        Action<MragMapStageOptions>? configure = null)
    {
        ValidateNodeId(nodeId);
        ArgumentNullException.ThrowIfNull(subPipeline);

        if (subPipeline._compiledSubGraph == null)
            throw new InvalidOperationException(
                $"Sub-pipeline has not been compiled. " +
                $"Call BuildSubPipelineAsync() on the sub-pipeline before passing it to AddMapStage('{nodeId}').");

        var stageOptions = new MragMapStageOptions();
        configure?.Invoke(stageOptions);

        _graphBuilder.AddSubGraphNode(nodeId, nodeId, subPipeline._compiledSubGraph, nb =>
        {
            nb.WithMaxParallelExecutions(stageOptions.MaxParallelTasks);
        });

        _nodeIds.Add(nodeId);
        return this;
    }

    /// <summary>
    /// Adds a Tier-1 processor node.
    /// <typeparamref name="TProcessor"/> must implement <c>IMragProcessor&lt;TIn, TOut&gt;</c>
    /// and be registered in the caller's DI container before <see cref="MragIngestionPipeline.RunAsync"/>
    /// / <see cref="MragRetrievalPipeline.RetrieveAsync"/> is called.
    ///
    /// Input key: "input". Output key: "output". Port: 0.
    /// </summary>
    public MragPipeline AddProcessor<TProcessor>(
        string nodeId,
        Action<MragNodeOptions>? options = null)
        where TProcessor : class
    {
        ValidateNodeId(nodeId);

        var nodeOptions = new MragNodeOptions();
        options?.Invoke(nodeOptions);

        var adapterHandlerName = $"mrag:processor:{nodeId}";

        _graphBuilder.AddHandlerNode(nodeId, nodeId, adapterHandlerName, nb =>
        {
            ApplyNodeOptions(nb, nodeOptions);
        });

        _nodeIds.Add(nodeId);
        _adapterRegistrations.Add((adapterHandlerName, typeof(TProcessor)));
        return this;
    }

    /// <summary>
    /// Adds a Tier-1.5 router node.
    /// <typeparamref name="TRouter"/> must implement <c>IMragRouter&lt;TIn&gt;</c>
    /// and be registered in the caller's DI container.
    ///
    /// <paramref name="ports"/> must equal the number of distinct output port branches.
    /// Wire branches via: <c>.From(nodeId).Port(0).To("branch-a").Done()</c>
    /// </summary>
    public MragPipeline AddRouter<TRouter>(
        string nodeId,
        int ports,
        Action<MragNodeOptions>? options = null)
        where TRouter : class
    {
        ValidateNodeId(nodeId);
        if (ports < 2)
            throw new ArgumentOutOfRangeException(nameof(ports),
                "Router nodes must have at least 2 output ports.");

        var nodeOptions = new MragNodeOptions();
        options?.Invoke(nodeOptions);

        var adapterHandlerName = $"mrag:router:{nodeId}";

        _graphBuilder.AddRouterNode(nodeId, nodeId, adapterHandlerName, nb =>
        {
            nb.WithOutputPorts(ports);
            ApplyNodeOptions(nb, nodeOptions);
        });

        _nodeIds.Add(nodeId);
        _adapterRegistrations.Add((adapterHandlerName, typeof(TRouter)));
        return this;
    }

    /// <summary>
    /// Adds a raw handler node that directly implements
    /// <see cref="IGraphNodeHandler{MragPipelineContext}"/>.
    /// Use this for advanced scenarios where neither Tier-1 nor built-in handlers suffice.
    /// </summary>
    public MragPipeline AddRawHandler<THandler>(
        string nodeId,
        Action<MragNodeOptions>? options = null)
        where THandler : IGraphNodeHandler<MragPipelineContext>
    {
        ValidateNodeId(nodeId);

        var nodeOptions = new MragNodeOptions();
        options?.Invoke(nodeOptions);

        var adapterHandlerName = $"mrag:raw:{nodeId}";

        _graphBuilder.AddHandlerNode(nodeId, nodeId, adapterHandlerName, nb =>
        {
            ApplyNodeOptions(nb, nodeOptions);
        });

        _nodeIds.Add(nodeId);
        _adapterRegistrations.Add((adapterHandlerName, typeof(THandler)));
        return this;
    }

    // ------------------------------------------------------------------ //
    // Edge wiring                                                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Starts an edge chain from one or more source nodes.
    /// Returns a <see cref="MragEdgeBuilder"/> for fluent wiring.
    /// </summary>
    public MragEdgeBuilder From(params string[] sourceNodes)
    {
        if (sourceNodes == null || sourceNodes.Length == 0)
            throw new ArgumentException("At least one source node is required.", nameof(sourceNodes));
        return new MragEdgeBuilder(this, sourceNodes);
    }

    // Called by MragEdgeBuilder
    internal void AddEdgeInternal(string from, string to, int? fromPort, EdgeCondition? condition)
    {
        _edges.Add((from, to, fromPort, condition));
    }

    // ------------------------------------------------------------------ //
    // Build methods                                                        //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Compiles the inner graph and stores it so this pipeline can be passed to
    /// <see cref="AddMapStage"/>. Returns <c>this</c> (not a typed terminal).
    /// </summary>
    public Task<MragPipeline> BuildSubPipelineAsync(CancellationToken ct = default)
    {
        var name = _name
            ?? throw new ArgumentException("Call WithName() before BuildSubPipelineAsync().");
        _compiledSubGraph = BuildGraph(name);
        return Task.FromResult(this);
    }

    /// <summary>Builds and returns a <see cref="MragIngestionPipeline"/>.</summary>
    public Task<MragIngestionPipeline> BuildIngestionAsync(CancellationToken ct = default)
    {
        var (graph, services) = BuildCore();
        return Task.FromResult(new MragIngestionPipeline(_name!, graph, services));
    }

    /// <summary>Builds and returns a <see cref="MragRetrievalPipeline"/>.</summary>
    public Task<MragRetrievalPipeline> BuildRetrievalAsync(CancellationToken ct = default)
    {
        var (graph, services) = BuildCore();
        return Task.FromResult(new MragRetrievalPipeline(_name!, graph, services));
    }

    /// <summary>Builds and returns a <see cref="MragEvaluationPipeline"/>.</summary>
    public Task<MragEvaluationPipeline> BuildEvaluationAsync(CancellationToken ct = default)
    {
        var (graph, services) = BuildCore();
        return Task.FromResult(new MragEvaluationPipeline(_name!, graph, services));
    }

    // ------------------------------------------------------------------ //
    // Private helpers                                                      //
    // ------------------------------------------------------------------ //

    private (Graph graph, IServiceProvider services) BuildCore()
    {
        var name = _name
            ?? throw new ArgumentException(
                "Pipeline name is required. Call WithName() before Build*Async().");

        var graph = BuildGraph(name);
        var services = BuildServiceProvider();
        return (graph, services);
    }

    private Graph BuildGraph(string name)
    {
        _graphBuilder.WithName(name);

        // AddStartNode / AddEndNode are idempotent — GraphBuilder.Build() also auto-adds
        // them if missing, but we want to ensure they appear in the node list.
        _graphBuilder.AddStartNode();
        _graphBuilder.AddEndNode();

        // Register all pending edges.
        foreach (var (from, to, fromPort, condition) in _edges)
        {
            if (fromPort.HasValue || condition != null)
            {
                _graphBuilder.AddEdge(from, to, eb =>
                {
                    if (fromPort.HasValue)
                        eb.FromPort(fromPort.Value);
                    if (condition != null)
                        eb.WithCondition(condition);
                });
            }
            else
            {
                _graphBuilder.AddEdge(from, to);
            }
        }

        return _graphBuilder.Build();
    }

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Register a DeferredAdapterHandler for each processor / router / raw-handler node.
        // The deferred handler resolves the actual processor/router from the *execution-time*
        // IServiceProvider (the one passed to RunAsync / RetrieveAsync / BackfillAsync),
        // which is where user TProcessor / TRouter registrations live.
        foreach (var (handlerName, serviceType) in _adapterRegistrations)
        {
            services.AddSingleton<IGraphNodeHandler<MragPipelineContext>>(
                new DeferredAdapterHandler(handlerName, serviceType));
        }

        return services.BuildServiceProvider();
    }

    private static void ApplyNodeOptions(NodeBuilder nb, MragNodeOptions options)
    {
        if (options.RetryPolicy != null)
        {
            nb.WithRetryPolicy(new RetryPolicy
            {
                MaxAttempts = options.RetryPolicy.MaxAttempts,
                InitialDelay = options.RetryPolicy.InitialDelay,
                Strategy = MapBackoffStrategy(options.RetryPolicy.Strategy),
                MaxDelay = options.RetryPolicy.MaxDelay
            });
        }

        if (options.ErrorPropagation != null)
        {
            nb.WithErrorPolicy(MapErrorPropagation(options.ErrorPropagation));
        }
    }

    private static BackoffStrategy MapBackoffStrategy(MragBackoffStrategy s) => s switch
    {
        MragBackoffStrategy.Constant => BackoffStrategy.Constant,
        MragBackoffStrategy.Linear => BackoffStrategy.Linear,
        MragBackoffStrategy.Exponential => BackoffStrategy.Exponential,
        MragBackoffStrategy.JitteredExponential => BackoffStrategy.JitteredExponential,
        _ => BackoffStrategy.JitteredExponential
    };

    private static ErrorPropagationPolicy MapErrorPropagation(MragErrorPropagation ep)
    {
        // MragErrorPropagation's Mode field is internal.
        // We compare against the known singleton static instances which cover the common cases.
        if (ReferenceEquals(ep, MragErrorPropagation.StopPipeline))
            return ErrorPropagationPolicy.StopGraph();
        if (ReferenceEquals(ep, MragErrorPropagation.SkipDependents))
            return ErrorPropagationPolicy.SkipDependents();
        if (ReferenceEquals(ep, MragErrorPropagation.Isolate))
            return ErrorPropagationPolicy.Isolate();
        // FallbackTo() returns a new instance each call — cannot use ReferenceEquals.
        // Extract the FallbackNodeId via ToString() is unreliable, so default to Isolate.
        // SA-9 (DI milestone) can extend this when FallbackNodeId is exposed publicly.
        return ErrorPropagationPolicy.Isolate();
    }

    private void ValidateNodeId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("Node ID cannot be empty.", nameof(nodeId));
        if (_nodeIds.Contains(nodeId))
            throw new InvalidOperationException(
                $"A node with ID '{nodeId}' has already been added to this pipeline.");
    }

    private static void ValidateHandlerName(string handlerName)
    {
        if (string.IsNullOrWhiteSpace(handlerName))
            throw new ArgumentException("Handler name cannot be empty.", nameof(handlerName));
    }
}
