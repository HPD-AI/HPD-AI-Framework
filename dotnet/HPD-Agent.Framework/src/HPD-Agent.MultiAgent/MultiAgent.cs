using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.MultiAgent.Config;
using HPD.MultiAgent.Internal;
using HPD.MultiAgent.Routing;
using HPD.MultiAgent.Serialization;
using HPD.Graph.Abstractions;
using HPD.Graph.Abstractions.Graph;
using HPD.Graph.Core.Builders;
using MultiAgentEdgeBuilder = HPD.MultiAgent.Routing.EdgeBuilder;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.MultiAgent;

/// <summary>
/// Fluent builder for creating multi-agent workflows.
/// </summary>
public sealed class MultiAgent
{
    private readonly GraphBuilder _graphBuilder;
    private readonly Dictionary<string, Agent.Agent> _agents = new();
    private readonly Dictionary<string, AgentConfig> _agentConfigs = new();
    private readonly Dictionary<string, AgentNodeOptions> _options = new();
    private readonly HashSet<(string From, string To)> _declaredEdges = new();

    private WorkflowSettingsConfig _settings;
    private ISessionStore? _sessionStore;
    private WorkflowHostBinding? _host;

    public MultiAgent WithHost(WorkflowHostBinding host)
    { _host = host ?? throw new ArgumentNullException(nameof(host)); _sessionStore = host.SessionStore; return this; }
    private readonly List<Action<IServiceCollection>> _serviceConfigurators = new();
    private string? _workflowName;
    private int? _explicitMaxIterations;
    private TimeSpan? _explicitTimeout;

    /// <summary>
    /// Creates a new workflow builder.
    /// </summary>
    public MultiAgent()
    {
        _graphBuilder = new GraphBuilder();
        _settings = new WorkflowSettingsConfig();
    }

    /// <summary>
    /// Creates a workflow builder from a JSON or YAML workflow configuration file.
    /// </summary>
    public static MultiAgent FromFile(string path)
    {
        var config = MultiAgentConfigSerializer.ReadFile(path)
            ?? throw new InvalidOperationException($"Failed to deserialize MultiAgentWorkflowConfig from '{path}'.");

        return new MultiAgent(config);
    }

    public static MultiAgent FromFile(string path, WorkflowHostBinding host)
    {
        var config = host.Providers is null ? MultiAgentConfigSerializer.ReadFile(path) : MultiAgentConfigSerializer.ReadFile(path, host.Providers);
        return new MultiAgent(config ?? throw new InvalidOperationException("Workflow configuration is null."), host);
    }

    internal MultiAgent(MultiAgentWorkflowConfig config, WorkflowHostBinding? host = null)
    {
        _graphBuilder = new GraphBuilder();
        if (host is not null) WithHost(host);
        _settings = config.Settings;
        _workflowName = config.Name;

        // Add agents from config
        foreach (var (nodeId, nodeConfig) in config.Agents)
        {
            if (nodeConfig.AgentFile is not null)
                throw new InvalidOperationException($"Agent node '{nodeId}' has an unresolved agentFile. Load references using MultiAgent.FromFile().");
            _agentConfigs[nodeId] = nodeConfig.Agent
                ?? throw new InvalidOperationException($"Agent node '{nodeId}' has no resolved configuration. Load agentFile references using MultiAgent.FromFile().");
            _options[nodeId] = ConvertToOptions(nodeConfig, host);
        }

        // Add edges from config
        foreach (var edge in config.Edges)
        {
            var condition = edge.When != null ? MapCondition(edge.When) : null;
            AddEdgeInternal(edge.From, edge.To, condition);
        }
    }

    /// <summary>
    /// Set the workflow name.
    /// </summary>
    public MultiAgent WithName(string name)
    {
        _workflowName = name;
        return this;
    }

    /// <summary>
    /// Use a session store for multi-agent conversation policies.
    /// Durable workflow execution and checkpoints are owned by HPD.Base activations.
    /// </summary>
    public MultiAgent WithSessionStore(ISessionStore store)
    {
        _sessionStore = store ?? throw new ArgumentNullException(nameof(store));
        return this;
    }

    /// <summary>
    /// Use an in-memory session store for multi-agent conversation policies.
    /// </summary>
    public MultiAgent WithInMemorySessionStore(AgentEventComposition eventComposition)
    {
        ArgumentNullException.ThrowIfNull(eventComposition);
        return WithSessionStore(new InMemorySessionStore(eventComposition.Codec));
    }

    /// <summary>
    /// Use the segmented local-file session store for multi-agent conversation policies.
    /// </summary>
    public MultiAgent WithFileSessionStore(
        string rootDirectory,
        AgentEventComposition eventComposition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(eventComposition);
        return WithSessionStore(new FileSessionStore(rootDirectory, eventComposition.Codec));
    }

    /// <summary>
    /// Configure how workflow node agents write transcripts into HPD sessions and threads.
    /// </summary>
    public MultiAgent WithConversation(MultiAgentConversationConfig conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        _settings = _settings with { Conversation = conversation };
        return this;
    }

    /// <summary>
    /// Add a pre-built agent to the workflow.
    /// </summary>
    public MultiAgent AddAgent(
        string id,
        Agent.Agent agent,
        Action<AgentNodeOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));

        _agents[id] = agent ?? throw new ArgumentNullException(nameof(agent));

        var options = new AgentNodeOptions();
        configure?.Invoke(options);
        _options[id] = options;

        return this;
    }

    /// <summary>
    /// Add an agent via AgentConfig for deferred building.
    /// </summary>
    public MultiAgent AddAgent(
        string id,
        AgentConfig config,
        Action<AgentNodeOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));

        _agentConfigs[id] = config ?? throw new ArgumentNullException(nameof(config));

        var options = new AgentNodeOptions();
        configure?.Invoke(options);
        _options[id] = options;

        return this;
    }

    /// <summary>
    /// Add an agent via inline builder configuration.
    /// </summary>
    public MultiAgent AddAgent(
        string id,
        Action<AgentBuilder> configureAgent,
        Action<AgentNodeOptions>? configureNode = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));
        if (configureAgent == null)
            throw new ArgumentNullException(nameof(configureAgent));

        // Store the builder action for deferred building at execution time.
        // Inline builder agents are runtime-only and cannot be exported to declarative config.
        _agentConfigs[id] = new AgentConfig();
        _builderActions[id] = configureAgent;

        var options = new AgentNodeOptions();
        configureNode?.Invoke(options);
        _options[id] = options;

        return this;
    }

    private readonly Dictionary<string, Action<AgentBuilder>> _builderActions = new();

    /// <summary>
    /// Add a router agent that uses handoffs to decide routing.
    /// </summary>
    /// <param name="id">The node ID for this router agent.</param>
    /// <param name="config">The agent configuration.</param>
    /// <returns>A builder for configuring handoff targets.</returns>
    public RouterAgentBuilder AddRouterAgent(string id, AgentConfig config)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));

        _agentConfigs[id] = config ?? throw new ArgumentNullException(nameof(config));
        _options[id] = new AgentNodeOptions { OutputMode = AgentOutputMode.Handoff };

        return new RouterAgentBuilder(this, id);
    }

    /// <summary>
    /// Add a router agent that uses handoffs to decide routing.
    /// </summary>
    /// <param name="id">The node ID for this router agent.</param>
    /// <param name="agent">The pre-built agent.</param>
    /// <returns>A builder for configuring handoff targets.</returns>
    public RouterAgentBuilder AddRouterAgent(string id, Agent.Agent agent)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));

        _agents[id] = agent ?? throw new ArgumentNullException(nameof(agent));
        _options[id] = new AgentNodeOptions { OutputMode = AgentOutputMode.Handoff };

        return new RouterAgentBuilder(this, id);
    }

    /// <summary>
    /// Start defining edges from source nodes.
    /// </summary>
    public MultiAgentEdgeBuilder From(params string[] sourceNodes)
    {
        if (sourceNodes == null || sourceNodes.Length == 0)
            throw new ArgumentException("At least one source node is required", nameof(sourceNodes));

        return new Routing.EdgeBuilder(this, sourceNodes);
    }

    /// <summary>
    /// Set maximum iterations for cyclic graphs.
    /// </summary>
    public MultiAgent WithMaxIterations(int maxIterations)
    {
        _explicitMaxIterations = maxIterations;
        _graphBuilder.WithMaxIterations(maxIterations);
        return this;
    }

    /// <summary>
    /// Set execution timeout.
    /// </summary>
    public MultiAgent WithTimeout(TimeSpan timeout)
    {
        _explicitTimeout = timeout;
        _graphBuilder.WithExecutionTimeout(timeout);
        return this;
    }

    /// <summary>
    /// Build the workflow.
    /// </summary>
    public Task<AgentWorkflowInstance> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.Conversation.Mode != MultiAgentConversationMode.None && _sessionStore == null)
        {
            throw new InvalidOperationException(
                "A session store is required when multi-agent conversation policies are enabled. " +
                "Call WithSessionStore(), WithInMemorySessionStore(), or WithFileSessionStore().");
        }

        if (_settings.MaxConcurrency is <= 0)
            throw new ArgumentOutOfRangeException(nameof(_settings.MaxConcurrency), "Workflow concurrency must be positive.");

        var effectiveOptions = _options.ToDictionary(pair => pair.Key, pair => pair.Value.Snapshot(_host?.Providers));
        foreach (var (id, opts) in effectiveOptions)
        {
            var identity = new WorkflowNodeIdentity(_workflowName ?? "MultiAgentWorkflow", id);
            _host?.ValidateNode(identity, _agentConfigs.GetValueOrDefault(id) ?? _agents.GetValueOrDefault(id)?.Config, opts);
            if (_host is not null) opts.RunConfig = _host.Authorize(opts.RunConfig);
            AgentNodeHandler.ResolveOutput(opts, opts.RunConfig);
            if (opts.HandoffTargets?.Keys.Any(target => !GetAllAgentIds().Contains(target)) == true)
                throw new InvalidOperationException($"Node '{id}' references an unknown handoff target.");
            if (opts.OutputMode == AgentOutputMode.Handoff && opts.HandoffTargets is not { Count: > 0 })
                throw new InvalidOperationException($"Node '{id}' requires handoff targets.");
            if (_host is not null && _agents.TryGetValue(id, out var prebuilt) &&
                (!ReferenceEquals(prebuilt.Config?.EventComposition, _host.Events) || !ReferenceEquals(prebuilt.Config?.SessionStore, _host.SessionStore)))
                throw new InvalidOperationException($"Prebuilt node '{id}' does not share the host event composition/session store.");
        }

        // Create agent factories for deferred building (agents are built at execution time
        // so they can inherit the parent's chat client when no provider is configured)
        var factories = new Dictionary<string, AgentFactory>();

        // Wrap pre-built agents
        foreach (var (id, agent) in _agents)
        {
            factories[id] = new PrebuiltAgentFactory(agent);
        }

        // Create factories for agents from configs (not yet in _agents)
        foreach (var (id, config) in _agentConfigs)
        {
            if (factories.ContainsKey(id))
                continue;

            if (_builderActions.TryGetValue(id, out var builderAction))
            {
                factories[id] = new InlineAgentFactory(builderAction, _host, new WorkflowNodeIdentity(_workflowName ?? "MultiAgentWorkflow", id));
            }
            else
            {
                // Agent from config only
                factories[id] = new ConfigAgentFactory(config, _host, new WorkflowNodeIdentity(_workflowName ?? "MultiAgentWorkflow", id));
            }
        }

        // Configure graph
        _graphBuilder.WithName(_workflowName ?? "MultiAgentWorkflow");

        // Wire iteration options. The top-level MaxIterations remains the
        // simple setting; IterationOptions can override it when supplied.
        var iterationOptions = _settings.IterationOptions;
        var maxIterations = _explicitMaxIterations ??
            iterationOptions?.MaxIterations ??
            _settings.MaxIterations;
        _graphBuilder.WithMaxIterations(maxIterations);
        _graphBuilder.WithIterationOptions(
            new HPD.Graph.Abstractions.Graph.IterationOptions
            {
                MaxIterations = maxIterations,
                EnableAutoConvergence =
                    iterationOptions?.EnableAutoConvergence ?? true,
                IgnoreFieldsForChangeDetection =
                    iterationOptions?.IgnoreFieldsForChangeDetection != null
                        ? new HashSet<string>(
                            iterationOptions.IgnoreFieldsForChangeDetection)
                        : null,
                AlwaysDirtyNodes =
                    iterationOptions?.AlwaysDirtyNodes != null
                        ? new HashSet<string>(
                            iterationOptions.AlwaysDirtyNodes)
                        : null
            });

        if ((_explicitTimeout ?? _settings.DefaultTimeout) is { } timeout)
        {
            _graphBuilder.WithExecutionTimeout(timeout);
        }

        // Add START and END nodes
        _graphBuilder.AddStartNode();
        _graphBuilder.AddEndNode();

        // Add agent nodes
        foreach (var id in GetAllAgentIds())
        {
            var handlerName = $"{id}Handler";
            _graphBuilder.AddHandlerNode(id, id, handlerName, node =>
            {
                var opts = effectiveOptions.TryGetValue(id, out var o) ? o : new AgentNodeOptions();

                if (opts.RecoveryOnly) node.AsRecoveryOnly();
                node.WithOutputContract(WorkflowOutputContract.Create(opts));
                if (opts.Metadata is not null) foreach (var (key, value) in opts.Metadata) node.WithMetadata(key, value);
                if (opts.RecoveryOnly && (opts.ErrorMode != ErrorMode.Stop || opts.ExhaustionMode is not null || opts.FallbackAgentId is not null))
                    throw new InvalidOperationException("Recovery-only definitions cannot declare error policies.");
                if (!opts.RecoveryOnly) node.WithErrorPolicy(WorkflowOutputContract.ErrorPolicy(opts));

                if (opts.Timeout.HasValue)
                    node.WithTimeout(opts.Timeout.Value);

                if (opts.RetryPolicy != null)
                    node.WithRetryPolicy(ToGraphRetryPolicy(opts.RetryPolicy));

                if (opts.MaxConcurrentExecutions.HasValue)
                    node.WithMaxParallelExecutions(opts.MaxConcurrentExecutions.Value);
            });
        }

        AddInfrastructureEdges();

        // Build graph
        var graph = _graphBuilder.Build();

        // Create service provider with handlers
        var services = new ServiceCollection();

        foreach (var configureServices in _serviceConfigurators)
        {
            configureServices(services);
        }

        foreach (var id in GetAllAgentIds())
        {
            var handler = new AgentNodeHandler(id);
            services.AddSingleton<HPD.Graph.Abstractions.Handlers.IGraphNodeHandler<AgentGraphContext>>(handler);
        }

        return Task.FromResult(new AgentWorkflowInstance(
            graph,
            factories,
            effectiveOptions,
            services.BuildServiceProvider(),
            _workflowName,
            _settings,
            _sessionStore, _host));
    }

    // Internal methods for EdgeBuilder
    internal void AddEdgeInternal(
        string from,
        string to,
        WorkflowCondition? condition)
    {
        _declaredEdges.Add((from, to));

        _graphBuilder.AddOrReplaceEdge(from, to, edge =>
        {
            if (condition != null)
            {
                edge.WithCondition(ToGraphCondition(condition));
            }
        });
    }

    internal void UpdateEdgeCondition(
        string from,
        string to,
        WorkflowCondition? condition)
    {
        AddEdgeInternal(from, to, condition);
    }

    internal void AddPredicateEdge(string from, string to, Func<EdgeConditionContext, bool> predicate)
    {
        _declaredEdges.Add((from, to));

        _graphBuilder.AddOrReplaceEdge(from, to, edge =>
        {
            edge.When(ctx => predicate(new EdgeConditionContext(
                ctx.SourceOutputs is Dictionary<string, object> outputs
                    ? outputs
                    : new Dictionary<string, object>(ctx.SourceOutputs))));
        });
    }

    internal AgentNodeOptions GetOrCreateOptions(string nodeId)
    {
        if (!_options.TryGetValue(nodeId, out var options))
        {
            options = new AgentNodeOptions();
            _options[nodeId] = options;
        }
        return options;
    }

    private void AddInfrastructureEdges()
    {
        var agentIds = GetAllAgentIds().Where(id => !_options[id].RecoveryOnly).ToArray();
        if (agentIds.Length == 0)
        {
            return;
        }

        foreach (var source in agentIds.Where(id => !_declaredEdges.Any(edge =>
                     edge.To == id && edge.From != "START")))
        {
            _graphBuilder.AddOrReplaceEdge("START", source);
        }

        foreach (var sink in agentIds.Where(id => !_declaredEdges.Any(edge =>
                     edge.From == id && edge.To != "END")))
        {
            _graphBuilder.AddOrReplaceEdge(sink, "END");
        }
    }

    private IEnumerable<string> GetAllAgentIds()
    {
        return _agents.Keys.Union(_agentConfigs.Keys).Distinct();
    }

    private static WorkflowCondition MapCondition(ConditionConfig c) => new()
    {
        Type = c.Type,
        Field = c.Field,
        Value = c.Value,
        RegexOptions = c.RegexOptions,
        Conditions = c.Conditions?.Select(MapCondition).ToList()
    };

    private static HPD.Graph.Abstractions.Graph.EdgeCondition ToGraphCondition(
        WorkflowCondition condition) =>
        new()
        {
            Type = MapConditionType(condition.Type),
            Field = condition.Field,
            Value = condition.Value,
            RegexOptions = condition.RegexOptions,
            Conditions = condition.Conditions?
                .Select(ToGraphCondition)
                .ToList()
        };

    private static HPD.Graph.Abstractions.Graph.ConditionType MapConditionType(
        Config.ConditionType type) =>
        type switch
        {
            Config.ConditionType.Always => HPD.Graph.Abstractions.Graph.ConditionType.Always,
            Config.ConditionType.FieldEquals => HPD.Graph.Abstractions.Graph.ConditionType.FieldEquals,
            Config.ConditionType.FieldNotEquals => HPD.Graph.Abstractions.Graph.ConditionType.FieldNotEquals,
            Config.ConditionType.FieldGreaterThan => HPD.Graph.Abstractions.Graph.ConditionType.FieldGreaterThan,
            Config.ConditionType.FieldGreaterThanOrEqual => HPD.Graph.Abstractions.Graph.ConditionType.FieldGreaterThanOrEqual,
            Config.ConditionType.FieldLessThan => HPD.Graph.Abstractions.Graph.ConditionType.FieldLessThan,
            Config.ConditionType.FieldLessThanOrEqual => HPD.Graph.Abstractions.Graph.ConditionType.FieldLessThanOrEqual,
            Config.ConditionType.FieldExists => HPD.Graph.Abstractions.Graph.ConditionType.FieldExists,
            Config.ConditionType.FieldNotExists => HPD.Graph.Abstractions.Graph.ConditionType.FieldNotExists,
            Config.ConditionType.FieldContains => HPD.Graph.Abstractions.Graph.ConditionType.FieldContains,
            Config.ConditionType.Default => HPD.Graph.Abstractions.Graph.ConditionType.Default,
            Config.ConditionType.UpstreamOneSuccess => HPD.Graph.Abstractions.Graph.ConditionType.UpstreamOneSuccess,
            Config.ConditionType.UpstreamAllDone => HPD.Graph.Abstractions.Graph.ConditionType.UpstreamAllDone,
            Config.ConditionType.UpstreamAllDoneOneSuccess => HPD.Graph.Abstractions.Graph.ConditionType.UpstreamAllDoneOneSuccess,
            Config.ConditionType.And => HPD.Graph.Abstractions.Graph.ConditionType.And,
            Config.ConditionType.Or => HPD.Graph.Abstractions.Graph.ConditionType.Or,
            Config.ConditionType.Not => HPD.Graph.Abstractions.Graph.ConditionType.Not,
            Config.ConditionType.FieldStartsWith => HPD.Graph.Abstractions.Graph.ConditionType.FieldStartsWith,
            Config.ConditionType.FieldEndsWith => HPD.Graph.Abstractions.Graph.ConditionType.FieldEndsWith,
            Config.ConditionType.FieldMatchesRegex => HPD.Graph.Abstractions.Graph.ConditionType.FieldMatchesRegex,
            Config.ConditionType.FieldIsEmpty => HPD.Graph.Abstractions.Graph.ConditionType.FieldIsEmpty,
            Config.ConditionType.FieldIsNotEmpty => HPD.Graph.Abstractions.Graph.ConditionType.FieldIsNotEmpty,
            Config.ConditionType.FieldContainsAny => HPD.Graph.Abstractions.Graph.ConditionType.FieldContainsAny,
            Config.ConditionType.FieldContainsAll => HPD.Graph.Abstractions.Graph.ConditionType.FieldContainsAll,
            _ => throw new InvalidOperationException(
                $"Unknown workflow condition type '{type}'.")
        };

    private static HPD.Graph.Abstractions.Graph.RetryPolicy ToGraphRetryPolicy(
        RetryPolicy policy) =>
        new()
        {
            OnlyTransient = policy.OnlyTransient,
            MaxAttempts = policy.MaxAttempts,
            InitialDelay = policy.InitialDelay,
            Strategy = MapBackoffStrategy(policy.Strategy),
            MaxDelay = policy.MaxDelay,
            RetryableExceptions = policy.RetryableExceptions
        };

    private static Type? ResolveStructuredType(AgentNodeConfig config, WorkflowHostBinding? host)
    {
        if (config.OutputMode != AgentOutputMode.Structured)
            return null;

        if (string.IsNullOrWhiteSpace(config.StructuredOutputType))
            throw new ArgumentException("Structured output requires structuredOutputType.");

        // ExportConfigJson writes assembly-qualified names. Only resolve types
        // supplied by the trusted host configuration, never model output.
        var type = host?.ResolveType(config.StructuredOutputType) ?? WorkflowTypeResolver.Resolve(config.StructuredOutputType);
        if (type is null || !type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
            throw new ArgumentException(
                $"Cannot resolve structuredOutputType '{config.StructuredOutputType}' to a concrete closed class. Use an assembly-qualified type name available to the host.");
        return type;
    }

    private static AgentNodeOptions ConvertToOptions(AgentNodeConfig config, WorkflowHostBinding? host)
    {
        var options = new AgentNodeOptions
        {
            OutputMode = config.OutputMode,
            StructuredOutputMode = config.StructuredOutputMode,
            StructuredType = ResolveStructuredType(config, host),
            UnionTypes = config.UnionTypeNames?.Select(name => host?.ResolveType(name) ?? WorkflowTypeResolver.Resolve(name) ?? throw new InvalidOperationException($"Unresolved union type: {name}")).ToArray(),
            RecoveryOnly = config.RecoveryOnly,
            ExhaustionMode = config.OnError?.ExhaustionMode,
            Timeout = config.Timeout,
            MaxConcurrentExecutions = config.MaxConcurrent,
            InputKey = config.InputKey,
            OutputKey = config.OutputKey,
            InputTemplate = config.InputTemplate,
            RunConfig = config.RunConfig ?? new AgentRunConfig(),
            ErrorMode = config.OnError?.Mode ?? ErrorMode.Stop,
            AffectedNodes = config.OnError?.AffectedNodes,
            Metadata = config.Metadata,
            HandoffTargets = config.HandoffTargets,
            ClientInheritance = config.ClientInheritance ?? new AgentNodeOptions().ClientInheritance,
            FallbackAgentId = config.OnError?.FallbackAgent
        };

        if (config.Retry != null)
        {
            options.RetryPolicy = new RetryPolicy
            {
                OnlyTransient = config.Retry.OnlyTransient,
                RetryableExceptions = new HPD.Graph.Core.Config.GraphConfigCompiler().ResolveExceptionTypes(config.Retry.RetryableExceptionTypeNames),
                MaxAttempts = config.Retry.MaxAttempts,
                InitialDelay = config.Retry.InitialDelay,
                Strategy = config.Retry.Strategy,
                MaxDelay = config.Retry.MaxDelay
            };
        }

        return options;
    }

    private static HPD.Graph.Abstractions.Graph.BackoffStrategy MapBackoffStrategy(
        Config.BackoffStrategy strategy) =>
        strategy switch
        {
            Config.BackoffStrategy.Constant =>
                HPD.Graph.Abstractions.Graph.BackoffStrategy.Constant,
            Config.BackoffStrategy.Exponential =>
                HPD.Graph.Abstractions.Graph.BackoffStrategy.Exponential,
            Config.BackoffStrategy.Linear =>
                HPD.Graph.Abstractions.Graph.BackoffStrategy.Linear,
            Config.BackoffStrategy.JitteredExponential =>
                HPD.Graph.Abstractions.Graph.BackoffStrategy.JitteredExponential,
            _ => throw new InvalidOperationException(
                $"Unknown workflow backoff strategy '{strategy}'.")
        };
}

/// <summary>
/// Entry point for creating workflows fluently.
/// </summary>
public static class AgentWorkflow
{
    /// <summary>
    /// Create a new workflow builder.
    /// </summary>
    public static MultiAgent Create() => new();

    /// <summary>
    /// Create a workflow builder from a JSON or YAML workflow configuration file.
    /// </summary>
    public static MultiAgent FromFile(string path) => MultiAgent.FromFile(path);
    public static MultiAgent FromFile(string path, WorkflowHostBinding host) => MultiAgent.FromFile(path, host);

}
