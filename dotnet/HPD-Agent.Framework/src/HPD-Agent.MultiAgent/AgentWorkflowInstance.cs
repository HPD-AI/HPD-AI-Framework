using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Text.Json;
using HPD.Agent;
using HPD.Events;
using HPD.Events.Core;
using HPD.MultiAgent.Config;
using HPD.MultiAgent.Internal;
using HPD.MultiAgent.Routing;
using HPD.Serialization;
using HPD.Graph.Abstractions.Events;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Graph;
using HPD.Graph.Abstractions.Handlers;
using HPD.Graph.Core.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using GraphDefinition = HPD.Graph.Abstractions.Graph.Graph;

namespace HPD.MultiAgent;

/// <summary>
/// Result of a workflow execution.
/// </summary>
public sealed record WorkflowResult
{
    /// <summary>
    /// The final answer/output from the workflow.
    /// </summary>
    public string? FinalAnswer { get; init; }

    /// <summary>
    /// All outputs from the final node(s).
    /// </summary>
    public Dictionary<string, object> Outputs { get; init; } = new();

    /// <summary>
    /// Total execution duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether the workflow completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the workflow failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Exception if the workflow failed.
    /// </summary>
    public Exception? Exception { get; init; }
}

/// <summary>
/// Factory for creating reusable agents lazily; provider families are selected per run.
/// </summary>
public abstract class AgentFactory
{
    /// <summary>
    /// Build the agent without binding an invocation-specific provider client.
    /// </summary>
    public abstract Task<Agent.Agent> BuildAsync(
        ISessionStore? workflowSessionStore,
        bool requireWorkflowSessionStore,
        CancellationToken cancellationToken);

    /// <summary>
    /// Return the AgentConfig backing this factory.
    /// Used by config export to reconstruct serializable config.
    /// </summary>
    internal abstract AgentConfig? GetConfig();
}

/// <summary>
/// Factory that wraps a pre-built agent.
/// </summary>
internal sealed class PrebuiltAgentFactory : AgentFactory
{
    private readonly Agent.Agent _agent;

    public PrebuiltAgentFactory(Agent.Agent agent) => _agent = agent;

    public override Task<Agent.Agent> BuildAsync(
        ISessionStore? workflowSessionStore,
        bool requireWorkflowSessionStore,
        CancellationToken cancellationToken)
    {
        if (requireWorkflowSessionStore && !ReferenceEquals(_agent.Config?.SessionStore, workflowSessionStore))
        {
            throw new InvalidOperationException(
                $"Pre-built agent '{_agent.Name}' must use the workflow session store when multi-agent conversation policies are enabled.");
        }

        return Task.FromResult(_agent);
    }

    internal override AgentConfig? GetConfig() => _agent.Config;
}

/// <summary>
/// Factory that builds an agent from config with chat client inheritance.
/// </summary>
internal sealed class ConfigAgentFactory : AgentFactory
{
    private readonly AgentConfig _config;

    public ConfigAgentFactory(AgentConfig config)
    {
        _config = config;
    }

    public override async Task<Agent.Agent> BuildAsync(
        ISessionStore? workflowSessionStore,
        bool requireWorkflowSessionStore,
        CancellationToken cancellationToken)
    {
        var builder = new AgentBuilder(_config);

        if (workflowSessionStore != null)
        {
            builder.WithSessionStore(workflowSessionStore);
        }
        else if (requireWorkflowSessionStore)
        {
            throw new InvalidOperationException(
                "A workflow session store is required when multi-agent conversation policies are enabled.");
        }

        return await builder.BuildAsync(cancellationToken);
    }

    internal override AgentConfig? GetConfig() => _config;
}

/// <summary>
/// Factory that builds an agent from code. Runtime-only; it cannot be exported to declarative config.
/// </summary>
internal sealed class InlineAgentFactory : AgentFactory
{
    private readonly Action<AgentBuilder> _builderAction;

    public InlineAgentFactory(Action<AgentBuilder> builderAction)
    {
        _builderAction = builderAction ?? throw new ArgumentNullException(nameof(builderAction));
    }

    public override async Task<Agent.Agent> BuildAsync(
        ISessionStore? workflowSessionStore,
        bool requireWorkflowSessionStore,
        CancellationToken cancellationToken)
    {
        var config = new AgentConfig();
        var builder = new AgentBuilder(config);

        if (workflowSessionStore != null)
        {
            builder.WithSessionStore(workflowSessionStore);
        }
        else if (requireWorkflowSessionStore)
        {
            throw new InvalidOperationException(
                "A workflow session store is required when multi-agent conversation policies are enabled.");
        }

        _builderAction(builder);

        return await builder.BuildAsync(cancellationToken);
    }

    internal override AgentConfig? GetConfig() => null;
}

/// <summary>
/// A built multi-agent workflow ready for execution.
/// </summary>
public sealed class AgentWorkflowInstance : IMultiAgentWorkflow
{
    private readonly GraphDefinition _graph;
    private readonly Dictionary<string, AgentFactory> _agentFactories;
    private readonly Dictionary<string, AgentNodeOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _workflowName;
    private readonly WorkflowSettingsConfig _settings;
    private readonly ISessionStore? _workflowSessionStore;
    private readonly WorkflowEventCoordinator _eventCoordinator = new();

    // Cache of built agents (built lazily on first execution)
    private Dictionary<string, Agent.Agent>? _builtAgents;

    internal AgentWorkflowInstance(
        GraphDefinition graph,
        Dictionary<string, AgentFactory> agentFactories,
        Dictionary<string, AgentNodeOptions> options,
        IServiceProvider serviceProvider,
        string? workflowName = null,
        WorkflowSettingsConfig? settings = null,
        ISessionStore? workflowSessionStore = null)
    {
        _graph = graph;
        _agentFactories = agentFactories;
        _options = options;
        _serviceProvider = serviceProvider;
        _workflowName = workflowName ?? graph.Name ?? "Workflow";
        _settings = settings ?? new WorkflowSettingsConfig();
        _workflowSessionStore = workflowSessionStore;
    }

    /// <summary>
    /// The workflow name for identification in execution context.
    /// </summary>
    public string WorkflowName => _workflowName;

    /// <summary>
    /// Event coordinator used by this workflow instance for public workflow and child agent events.
    /// </summary>
    public WorkflowEventCoordinator Events => _eventCoordinator;

    /// <summary>
    /// Registers a removable typed subscriber for workflow or child agent events.
    /// </summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _eventCoordinator.Subscribe(handler);
    }

    /// <summary>
    /// Registers a removable typed subscriber for workflow or child agent events.
    /// </summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler)
        where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe<TEvent>(evt => new ValueTask(handler(evt)));
    }

    /// <summary>
    /// Registers a removable typed subscriber for workflow or child agent events.
    /// </summary>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe<TEvent>(evt =>
        {
            handler(evt);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Registers a removable catch-all subscriber for workflow and child agent events.
    /// </summary>
    public IDisposable SubscribeAny(Func<Event, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _eventCoordinator.SubscribeAny(handler);
    }

    /// <summary>
    /// Registers a removable catch-all subscriber for workflow and child agent events.
    /// </summary>
    public IDisposable SubscribeAny(Func<Event, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeAny(evt => new ValueTask(handler(evt)));
    }

    /// <summary>
    /// Registers a removable catch-all subscriber for workflow and child agent events.
    /// </summary>
    public IDisposable SubscribeAny(Action<Event> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeAny(evt =>
        {
            handler(evt);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Build agents lazily, caching the result for subsequent executions.
    /// Invocation-specific provider selection is applied later by each node run.
    /// </summary>
    private async Task<Dictionary<string, Agent.Agent>> BuildAgentsAsync(
        CancellationToken cancellationToken)
    {
        // Return cached agents if already built (for workflows used standalone without parent)
        if (_builtAgents != null)
            return _builtAgents;

        var agents = new Dictionary<string, Agent.Agent>();
        var requireWorkflowSessionStore = _settings.Conversation.Mode != MultiAgentConversationMode.None;
        foreach (var (id, factory) in _agentFactories)
        {
            agents[id] = await factory.BuildAsync(
                _workflowSessionStore,
                requireWorkflowSessionStore,
                cancellationToken);
        }

        if (requireWorkflowSessionStore)
        {
            foreach (var (id, agent) in agents)
            {
                if (!ReferenceEquals(agent.Config?.SessionStore, _workflowSessionStore))
                {
                    throw new InvalidOperationException(
                        $"Agent node '{id}' must use the workflow session store when multi-agent conversation policies are enabled.");
                }
            }
        }

        _builtAgents = agents;

        return agents;
    }

    /// <summary>
    /// Execute the workflow and return the final result.
    /// </summary>
    public async Task<WorkflowResult> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var outputs = new Dictionary<string, object>();
        string? finalAnswer = null;
        string? error = null;
        Exception? exception = null;
        var success = true;

        try
        {
            await foreach (var evt in ExecuteStreamingAsync(input, cancellationToken))
            {
                // Capture the last TextDeltaEvent content for final answer
                if (evt is TextDeltaEvent textDelta)
                {
                    // This will be overwritten by the last agent's output
                    // For a proper implementation, we'd track the final node
                }

                // Capture workflow agent outputs.
                if (evt is WorkflowAgentCompletedEvent agentComplete)
                {
                    if (agentComplete.Outputs != null)
                    {
                        foreach (var kvp in agentComplete.Outputs)
                        {
                            outputs[$"{agentComplete.AgentId}.{kvp.Key}"] = kvp.Value;
                        }

                        // Check for answer in the outputs
                        if (agentComplete.Outputs.TryGetValue("answer", out var answer))
                        {
                            finalAnswer = answer?.ToString();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            success = false;
            error = "Workflow was cancelled";
        }
        catch (Exception ex)
        {
            success = false;
            error = ex.Message;
            exception = ex;
        }

        return new WorkflowResult
        {
            FinalAnswer = finalAnswer,
            Outputs = outputs,
            Duration = DateTimeOffset.UtcNow - startTime,
            Success = success,
            Error = error,
            Exception = exception
        };
    }

    /// <summary>
    /// Execute the workflow with streaming events.
    /// Returns unified stream of graph and agent events.
    /// </summary>
    public IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        return ExecuteStreamingCoreAsync(input, null, null, null, cancellationToken);
    }

    /// <summary>
    /// Execute the workflow with streaming events, using a <see cref="WorkflowEventCoordinator"/>
    /// for request-session patterns (e.g. approval responses) and subscriptions.
    /// This overload avoids any direct dependency on HPD.Events.
    /// </summary>
    /// <param name="input">The input to the workflow.</param>
    /// <param name="coordinator">
    /// A <see cref="WorkflowEventCoordinator"/> used to send approval responses and receive subscription events.
    /// Call <see cref="WorkflowEventCoordinator.Approve"/> or <see cref="WorkflowEventCoordinator.Deny"/>
    /// while iterating the returned stream.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unified stream of graph and agent events.</returns>
    public async IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        WorkflowEventCoordinator coordinator,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in ExecuteStreamingCoreAsync(input, coordinator.Inner, null, null, cancellationToken))
        {
            yield return evt;
        }
    }

    async IAsyncEnumerable<Event> IMultiAgentWorkflow.ExecuteStreamingAsync(
        string input,
        HPD.Agent.Middleware.FunctionExecutionContext? parentContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in ExecuteStreamingCoreAsync(
            input,
            parentContext?.GetParentEventCoordinator(),
            parentContext?.GetParentAgentMetadata(),
            parentContext,
            cancellationToken).ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    async Task<string> IMultiAgentWorkflow.RunAsync(
        string input,
        HPD.Agent.Middleware.FunctionExecutionContext? parentContext,
        CancellationToken cancellationToken)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var evt in ((IMultiAgentWorkflow)this)
            .ExecuteStreamingAsync(input, parentContext, cancellationToken)
            .ConfigureAwait(false))
        {
            if (evt is TextDeltaEvent delta)
                text.Append(delta.Text);
        }
        return text.ToString();
    }

    /// <summary>
    /// Execute the workflow with streaming events, with optional parent coordinator for event bubbling.
    /// When a parent coordinator is provided, events will automatically bubble up to it.
    /// This enables nested workflows where events from inner workflows appear in the parent's event stream.
    /// </summary>
    /// <param name="input">The input to the workflow.</param>
    /// <param name="parentCoordinator">Optional parent event coordinator for hierarchical event bubbling.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unified stream of graph and agent events.</returns>
    public IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        HPD.Events.IEventCoordinator? parentCoordinator,
        CancellationToken cancellationToken = default)
    {
        return ExecuteStreamingCoreAsync(input, parentCoordinator, null, null, cancellationToken);
    }

    /// <summary>
    /// Execute the workflow with streaming events, with full hierarchical context support.
    /// </summary>
    /// <param name="input">The input to the workflow.</param>
    /// <param name="parentCoordinator">Optional parent event coordinator for hierarchical event bubbling.</param>
    /// <param name="parentAgentMetadata">Optional parent execution context for agent hierarchy tracking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unified stream of graph and agent events.</returns>
    public IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        HPD.Events.IEventCoordinator? parentCoordinator,
        AgentMetadata? parentAgentMetadata,
        CancellationToken cancellationToken = default)
    {
        return ExecuteStreamingCoreAsync(input, parentCoordinator, parentAgentMetadata, null, cancellationToken);
    }

    private async IAsyncEnumerable<Event> ExecuteStreamingCoreAsync(
        string input,
        HPD.Events.IEventCoordinator? parentCoordinator,
        AgentMetadata? parentAgentMetadata,
        HPD.Agent.Middleware.FunctionExecutionContext? parentContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var inheritedClientLease = parentContext?.ClientSet?.AcquireBorrowedLease();
        // Build agents lazily (with chat client inheritance if no provider configured)
        var agents = await BuildAgentsAsync(cancellationToken);

        // Create event coordinator for unified streaming
        var eventCoordinator = new EventCoordinator();

        // Internal graph events remain local. Only the public event produced by
        // WrapGraphEvent crosses the workflow boundary below.

        // Build workflow-level execution context
        var executionId = Guid.NewGuid().ToString("N");
        var randomId = executionId[..8];
        var sanitizedWorkflowName = System.Text.RegularExpressions.Regex.Replace(
            _workflowName, @"[^a-zA-Z0-9]", "_");

        var workflowContext = new AgentMetadata
        {
            AgentName = _workflowName,
            AgentId = parentAgentMetadata != null
                ? $"{parentAgentMetadata.AgentId}-{sanitizedWorkflowName}-{randomId}"
                : $"{sanitizedWorkflowName}-{randomId}",
            ParentAgentId = parentAgentMetadata?.AgentId,
            AgentChain = parentAgentMetadata != null
                ? new List<string>(parentAgentMetadata.AgentChain) { _workflowName }
                : new List<string> { _workflowName },
            Depth = (parentAgentMetadata?.Depth ?? -1) + 1
        };

        // Set ExecutionContext on each agent in the workflow for proper event attribution
        foreach (var (agentName, agent) in agents)
        {
            agent.AgentMetadata = new AgentMetadata
            {
                AgentName = agentName,
                AgentId = agent.AgentId,
                ParentAgentId = workflowContext.AgentId,
                AgentChain = new List<string>(workflowContext.AgentChain) { agentName },
                Depth = workflowContext.Depth + 1
            };
        }

        var conversationRuntime = CreateConversationRuntime(executionId, input);

        // Create context
        var context = new AgentGraphContext(
            executionId: executionId,
            graph: _graph,
            services: _serviceProvider,
            agents: agents,
            agentOptions: _options,
            originalInput: input,
            workflowName: _workflowName,
            conversation: conversationRuntime)
        {
            EventCoordinator = eventCoordinator,
            ParentExecutionContext = parentContext
        };

        // Set initial input in channels
        context.Channels["input"].Set(input);

        // Create orchestrator — pass checkpoint store when checkpointing is enabled
        var checkpointStore = _settings.EnableCheckpointing
            ? _serviceProvider.GetService<HPD.Graph.Abstractions.Checkpointing.IGraphCheckpointStore>()
            : null;

        var orchestrator = new GraphOrchestrator<AgentGraphContext>(_serviceProvider, checkpointStore: checkpointStore);

        var eventChannel = Channel.CreateUnbounded<Event>();
        using var eventSubscription = eventCoordinator.SubscribeAny(evt =>
        {
            eventChannel.Writer.TryWrite(evt);
            return ValueTask.CompletedTask;
        });

        // Start execution in background task
        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                // Emit error event
                await eventCoordinator.EmitAsync(new MessageTurnErrorEvent(
                    ErrorMessage: ex.Message,
                    Exception: ex), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                eventChannel.Writer.TryComplete();
            }
        }, cancellationToken);

        // Stream events from coordinator, wrapping graph events into agent-idiomatic workflow events
        await foreach (var evt in eventChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Wrap graph events into AgentEvent-derived workflow events
            var wrappedEvent = WrapGraphEvent(evt, workflowContext);
            if (wrappedEvent != null)
            {
                await _eventCoordinator.PublishAsync(wrappedEvent, cancellationToken).ConfigureAwait(false);
                if (parentCoordinator is not null && !ReferenceEquals(parentCoordinator, _eventCoordinator.Inner))
                {
                    await parentCoordinator.EmitAsync(wrappedEvent, cancellationToken).ConfigureAwait(false);
                }

                yield return wrappedEvent;
            }

            // Check if execution is complete
            if (context.IsComplete || context.IsCancelled)
            {
                break;
            }
        }

        // Wait for execution to complete
        await executionTask;
    }

    private IMultiAgentConversationRuntime CreateConversationRuntime(
        string executionId,
        string input)
    {
        if (_settings.Conversation.Mode == MultiAgentConversationMode.None)
        {
            return NoopMultiAgentConversationRuntime.Instance;
        }

        if (_workflowSessionStore == null)
        {
            throw new InvalidOperationException(
                "A session store is required when multi-agent conversation policies are enabled.");
        }

        return new MultiAgentConversationRuntime(
            _settings.Conversation,
            _workflowSessionStore,
            _workflowName,
            executionId,
            input);
    }

    /// <summary>
    /// Wraps internal graph events into public AgentEvent-derived workflow events.
    /// This allows consumers to use only HPD.Agent + HPD.MultiAgent without depending on HPD.Graph.
    /// </summary>
    private Event? WrapGraphEvent(Event evt, AgentMetadata workflowContext)
    {
        return evt switch
        {
            // Graph lifecycle events → Workflow events
            GraphExecutionStartedEvent g => new WorkflowStartedEvent
            {
                WorkflowName = _workflowName,
                NodeCount = _agentFactories.Count,
                LayerCount = g.LayerCount,
                Metadata = workflowContext
            },

            GraphExecutionCompletedEvent g => new WorkflowCompletedEvent
            {
                WorkflowName = _workflowName,
                Duration = g.Duration,
                SuccessfulNodes = g.SuccessfulNodes,
                FailedNodes = g.FailedNodes,
                SkippedNodes = g.SkippedNodes,
                Metadata = workflowContext
            },

            // Graph node events → public workflow agent events
            NodeExecutionStartedEvent n => new WorkflowAgentStartedEvent
            {
                WorkflowName = _workflowName,
                AgentId = n.NodeId,
                AgentName = n.HandlerName,
                LayerIndex = n.LayerIndex,
                Metadata = workflowContext
            },

            NodeExecutionCompletedEvent n => new WorkflowAgentCompletedEvent
            {
                WorkflowName = _workflowName,
                AgentId = n.NodeId,
                AgentName = n.HandlerName,
                Success = n.Result is NodeExecutionResult.Success,
                Duration = n.Duration,
                Progress = n.Progress,
                Outputs = n.Outputs,
                ErrorMessage = n.Result is NodeExecutionResult.Failure f ? f.Exception.Message : null,
                Metadata = workflowContext
            },

            NodeSkippedEvent n => new WorkflowAgentSkippedEvent
            {
                WorkflowName = _workflowName,
                AgentId = n.NodeId,
                Reason = n.Reason,
                Metadata = workflowContext
            },

            // Layer events → WorkflowLayer events
            LayerExecutionStartedEvent l => new WorkflowLayerStartedEvent
            {
                WorkflowName = _workflowName,
                LayerIndex = l.LayerIndex,
                NodeCount = l.NodeCount,
                Metadata = workflowContext
            },

            LayerExecutionCompletedEvent l => new WorkflowLayerCompletedEvent
            {
                WorkflowName = _workflowName,
                LayerIndex = l.LayerIndex,
                Duration = l.Duration,
                SuccessfulNodes = l.SuccessfulNodes,
                Metadata = workflowContext
            },

            // Edge events → WorkflowEdge events (diagnostic)
            EdgeTraversedEvent e => new WorkflowEdgeTraversedEvent
            {
                WorkflowName = _workflowName,
                FromNodeId = e.FromNodeId,
                ToNodeId = e.ToNodeId,
                HasCondition = e.HasCondition,
                ConditionDescription = e.ConditionDescription,
                Metadata = workflowContext
            },

            // Diagnostic events
            GraphDiagnosticEvent d => new WorkflowDiagnosticEvent
            {
                WorkflowName = _workflowName,
                Level = (LogLevel)(int)d.Level,  // Cast from HPD.Graph LogLevel
                Source = d.Source,
                Message = d.Message,
                NodeId = d.NodeId,
                Metadata = workflowContext
            },

            // Pass through AgentEvents unchanged (they're already in the right format)
            AgentEvent ae => ae,

            // Skip other graph events (EdgeConditionFailedEvent, iteration events, HITL events, etc.)
            // These are internal implementation details
            _ => null
        };
    }

    private static async Task SuppressCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Get the underlying graph definition.
    /// </summary>
    public GraphDefinition Graph => _graph;

    /// <summary>
    /// Get Mermaid diagram of the workflow.
    /// </summary>
    public string ToDiagram()
    {
        return _graph.ToMermaid();
    }

    /// <summary>
    /// Export the workflow configuration as JSON.
    /// Reconstructs a <see cref="MultiAgentWorkflowConfig"/> from the runtime graph and options,
    /// then serializes it with the source-generated config serializer.
    /// Agents added as pre-built instances must have an exportable AgentConfig.
    /// </summary>
    public string ExportConfigJson()
        => ExportConfig(HpdConfigFormat.Json);

    /// <summary>
    /// Export the workflow configuration as YAML.
    /// </summary>
    public string ExportConfigYaml()
        => ExportConfig(HpdConfigFormat.Yaml);

    /// <summary>
    /// Export the workflow configuration as JSON or YAML.
    /// </summary>
    public string ExportConfig(HpdConfigFormat format)
    {
        // --- Build Agents dictionary ---
        var agents = new Dictionary<string, AgentNodeConfig>();

        foreach (var (nodeId, factory) in _agentFactories)
        {
            var agentConfig = factory.GetConfig()
                ?? throw new InvalidOperationException(
                    $"Agent node '{nodeId}' cannot be exported because its factory does not expose an AgentConfig.");
            var nodeOptions = _options.TryGetValue(nodeId, out var opts) ? opts : new AgentNodeOptions();

            RetryConfig? retryConfig = null;
            if (nodeOptions.RetryPolicy is { } rp)
            {
                retryConfig = new RetryConfig
                {
                    MaxAttempts = rp.MaxAttempts,
                    InitialDelay = rp.InitialDelay,
                    Strategy = rp.Strategy,
                    MaxDelay = rp.MaxDelay,
                    OnlyTransient = rp.RetryableExceptions?.Count > 0
                };
            }

            ErrorConfig? errorConfig = null;
            if (nodeOptions.ErrorMode != ErrorMode.Stop || nodeOptions.FallbackAgentId != null)
            {
                errorConfig = new ErrorConfig
                {
                    Mode = nodeOptions.ErrorMode,
                    FallbackAgent = nodeOptions.FallbackAgentId
                };
            }

            agents[nodeId] = new AgentNodeConfig
            {
                Agent = agentConfig,
                OutputMode = nodeOptions.OutputMode,
                StructuredOutputType = nodeOptions.StructuredType?.AssemblyQualifiedName,
                UnionTypeNames = nodeOptions.UnionTypes?.Select(t => t.AssemblyQualifiedName!).ToList(),
                Timeout = nodeOptions.Timeout,
                MaxConcurrent = nodeOptions.MaxConcurrentExecutions,
                Retry = retryConfig,
                OnError = errorConfig,
                InputKey = nodeOptions.InputKey,
                OutputKey = nodeOptions.OutputKey,
                InputTemplate = nodeOptions.InputTemplate,
                AdditionalInstructions = nodeOptions.RunConfig.SystemInstructions?.Append
            };
        }

        // --- Build Edges list (skip START/END infrastructure edges) ---
        var entryId = _graph.EntryNodeId;
        var exitId = _graph.ExitNodeId;

        var edges = _graph.Edges
            .Where(e => e.From != entryId && e.To != exitId)
            .Select(e =>
            {
                ConditionConfig? when = null;
                if (e.Condition is { } c && c.Type != HPD.Graph.Abstractions.Graph.ConditionType.Always)
                {
                    when = MapEdgeConditionToConfig(c);
                }
                return new EdgeConfig { From = e.From, To = e.To, When = when };
            })
            .ToList();

        // --- Build Settings ---
        var settings = new WorkflowSettingsConfig
        {
            MaxIterations = _graph.MaxIterations,
            DefaultTimeout = _graph.ExecutionTimeout
        };

        // --- Assemble and serialize ---
        var config = new MultiAgentWorkflowConfig
        {
            Name = _workflowName,
            Version = _graph.Version,
            Agents = agents,
            Edges = edges,
            Settings = settings
        };

        return HpdConfigSerializer.Serialize(
            config,
            MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig,
            format);
    }

    /// <summary>
    /// Recursively maps an <see cref="EdgeCondition"/> to a serializable <see cref="ConditionConfig"/>.
    /// Preserves nested <c>Conditions</c> for compound types and <c>RegexOptions</c> for regex conditions.
    /// </summary>
    private static ConditionConfig MapEdgeConditionToConfig(HPD.Graph.Abstractions.Graph.EdgeCondition c) =>
        new ConditionConfig
        {
            Type = c.Type,
            Field = c.Field,
            Value = c.Value is null ? null
                  : c.Value is JsonElement je ? je
                  : JsonSerializer.SerializeToElement(c.Value),
            RegexOptions = c.RegexOptions,
            Conditions = c.Conditions?.Select(MapEdgeConditionToConfig).ToList()
        };
}

/// <summary>
/// Extension methods for handling approval workflow events.
/// </summary>
public static class ApprovalWorkflowExtensions
{
    /// <summary>
    /// Respond to a node approval request (approve).
    /// </summary>
    /// <param name="coordinator">The event coordinator.</param>
    /// <param name="requestId">The request ID from NodeApprovalRequestEvent.</param>
    /// <param name="reason">Optional reason for approval.</param>
    /// <param name="resumeData">Optional data to pass back to the node.</param>
    public static RespondResult Approve(
        this HPD.Events.IEventCoordinator coordinator,
        string requestId,
        string? reason = null,
        object? resumeData = null)
    {
        return coordinator.Respond(requestId, new NodeApprovalResponseEvent
        {
            RequestId = requestId,
            SourceName = "User",
            Approved = true,
            Reason = reason,
            ResumeData = resumeData
        });
    }

    /// <summary>
    /// Respond to a node approval request (deny).
    /// </summary>
    /// <param name="coordinator">The event coordinator.</param>
    /// <param name="requestId">The request ID from NodeApprovalRequestEvent.</param>
    /// <param name="reason">Reason for denial.</param>
    public static RespondResult Deny(
        this HPD.Events.IEventCoordinator coordinator,
        string requestId,
        string reason = "Denied by user")
    {
        return coordinator.Respond(requestId, new NodeApprovalResponseEvent
        {
            RequestId = requestId,
            SourceName = "User",
            Approved = false,
            Reason = reason
        });
    }

    /// <summary>
    /// Create an approval response event.
    /// </summary>
    public static NodeApprovalResponseEvent CreateApprovalResponse(
        string requestId,
        bool approved,
        string? reason = null,
        object? resumeData = null)
    {
        return new NodeApprovalResponseEvent
        {
            RequestId = requestId,
            SourceName = "User",
            Approved = approved,
            Reason = reason,
            ResumeData = resumeData
        };
    }
}
