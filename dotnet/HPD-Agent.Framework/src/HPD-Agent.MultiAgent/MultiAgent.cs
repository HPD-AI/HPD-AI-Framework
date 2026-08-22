using HPD.Agent;
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
public class MultiAgent
{
    private readonly GraphBuilder _graphBuilder;
    private readonly Dictionary<string, Agent.Agent> _agents = new();
    private readonly Dictionary<string, AgentConfig> _agentConfigs = new();
    private readonly Dictionary<string, AgentNodeOptions> _options = new();
    private readonly HashSet<(string From, string To)> _declaredEdges = new();

    private WorkflowSettingsConfig _settings;
    private ISessionStore? _sessionStore;
    private readonly List<Action<IServiceCollection>> _serviceConfigurators = new();
    private string? _workflowName;

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

    internal MultiAgent(MultiAgentWorkflowConfig config)
    {
        _graphBuilder = new GraphBuilder();
        _settings = config.Settings;
        _workflowName = config.Name;

        // Add agents from config
        foreach (var (nodeId, nodeConfig) in config.Agents)
        {
            _agentConfigs[nodeId] = nodeConfig.Agent;
            _options[nodeId] = ConvertToOptions(nodeConfig);
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
    public MultiAgent WithInMemorySessionStore()
        => WithSessionStore(new InMemorySessionStore());

    /// <summary>
    /// Use the segmented local-file session store for multi-agent conversation policies.
    /// </summary>
    public MultiAgent WithFileSessionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return WithSessionStore(new FileSessionStore(rootDirectory));
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
        _graphBuilder.WithMaxIterations(maxIterations);
        return this;
    }

    /// <summary>
    /// Set execution timeout.
    /// </summary>
    public MultiAgent WithTimeout(TimeSpan timeout)
    {
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
                factories[id] = new InlineAgentFactory(builderAction);
            }
            else
            {
                // Agent from config only
                factories[id] = new ConfigAgentFactory(config);
            }
        }

        // Configure graph
        _graphBuilder.WithName(_workflowName ?? "MultiAgentWorkflow");

        // Wire iteration options if configured
        if (_settings.IterationOptions != null)
        {
            _graphBuilder.WithIterationOptions(new HPD.Graph.Abstractions.Graph.IterationOptions
            {
                MaxIterations = _settings.IterationOptions.MaxIterations,
                EnableAutoConvergence = _settings.IterationOptions.EnableAutoConvergence,
                IgnoreFieldsForChangeDetection = _settings.IterationOptions.IgnoreFieldsForChangeDetection != null
                    ? new HashSet<string>(_settings.IterationOptions.IgnoreFieldsForChangeDetection)
                    : null,
                AlwaysDirtyNodes = _settings.IterationOptions.AlwaysDirtyNodes != null
                    ? new HashSet<string>(_settings.IterationOptions.AlwaysDirtyNodes)
                    : null
            });
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
                var opts = _options.TryGetValue(id, out var o) ? o : new AgentNodeOptions();

                if (opts.Timeout.HasValue)
                    node.WithTimeout(opts.Timeout.Value);

                if (opts.RetryPolicy != null)
                    node.WithRetryPolicy(opts.RetryPolicy);

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
            _options,
            services.BuildServiceProvider(),
            _workflowName,
            _settings,
            _sessionStore));
    }

    // Internal methods for EdgeBuilder
    internal void AddEdgeInternal(string from, string to, EdgeCondition? condition)
    {
        _declaredEdges.Add((from, to));

        _graphBuilder.AddOrReplaceEdge(from, to, edge =>
        {
            if (condition != null)
            {
                edge.WithCondition(condition);
            }
        });
    }

    internal void UpdateEdgeCondition(string from, string to, EdgeCondition? condition)
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
        var agentIds = GetAllAgentIds().ToArray();
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

    private static EdgeCondition MapCondition(Config.ConditionConfig c) => new EdgeCondition
    {
        Type = c.Type,
        Field = c.Field,
        Value = c.Value,
        RegexOptions = c.RegexOptions,
        Conditions = c.Conditions?.Select(MapCondition).ToList()
    };

    private static AgentNodeOptions ConvertToOptions(AgentNodeConfig config)
    {
        var options = new AgentNodeOptions
        {
            OutputMode = config.OutputMode,
            Timeout = config.Timeout,
            MaxConcurrentExecutions = config.MaxConcurrent,
            InputKey = config.InputKey,
            OutputKey = config.OutputKey,
            InputTemplate = config.InputTemplate,
            RunConfig = new AgentRunConfig
            {
                SystemInstructions = string.IsNullOrWhiteSpace(config.AdditionalInstructions)
                    ? null
                    : new SystemInstructionsRunConfig { Append = config.AdditionalInstructions }
            }
        };

        if (config.Retry != null)
        {
            options.RetryPolicy = new RetryPolicy
            {
                MaxAttempts = config.Retry.MaxAttempts,
                InitialDelay = config.Retry.InitialDelay,
                Strategy = config.Retry.Strategy,
                MaxDelay = config.Retry.MaxDelay
            };
        }

        return options;
    }
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

}
