using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.MultiAgent;

/// <summary>
/// Determines how a multi-agent workflow writes node agent transcripts into HPD sessions and threads.
/// </summary>
public enum MultiAgentConversationMode
{
    /// <summary>
    /// Preserve process-local workflow execution. Node agent turns are not routed into durable sessions.
    /// </summary>
    None,

    /// <summary>
    /// Route every node agent turn into one shared workflow thread.
    /// </summary>
    SharedWorkflowThread,

    /// <summary>
    /// Route each node agent into a stable thread inside one workflow session.
    /// </summary>
    ThreadPerAgent,

    /// <summary>
    /// Fork one thread per node agent from a shared workflow root thread.
    /// </summary>
    ForkThreadPerAgent
}

/// <summary>
/// Configures multi-agent conversation routing.
/// </summary>
public sealed record MultiAgentConversationConfig
{
    /// <summary>
    /// Conversation routing mode.
    /// </summary>
    public MultiAgentConversationMode Mode { get; init; } = MultiAgentConversationMode.None;

    /// <summary>
    /// Optional durable session id. When omitted, one is derived from the workflow execution.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Root thread id used by shared and forked policies.
    /// </summary>
    public string RootThreadId { get; init; } = "workflow";

    /// <summary>
    /// Prefix used when creating per-agent threads.
    /// </summary>
    public string ThreadPrefix { get; init; } = "workflow-agent";
}

/// <summary>
/// Convenience factory for multi-agent conversation policies.
/// </summary>
public static class MultiAgentConversationPolicies
{
    public static MultiAgentConversationConfig None() => new();

    public static MultiAgentConversationConfig SharedWorkflowThread(
        string? sessionId = null,
        string rootThreadId = "workflow") =>
        new()
        {
            Mode = MultiAgentConversationMode.SharedWorkflowThread,
            SessionId = sessionId,
            RootThreadId = rootThreadId
        };

    public static MultiAgentConversationConfig ThreadPerAgent(
        string? sessionId = null,
        string threadPrefix = "workflow-agent") =>
        new()
        {
            Mode = MultiAgentConversationMode.ThreadPerAgent,
            SessionId = sessionId,
            ThreadPrefix = threadPrefix
        };

    public static MultiAgentConversationConfig ForkThreadPerAgent(
        string? sessionId = null,
        string rootThreadId = "workflow",
        string threadPrefix = "workflow-agent") =>
        new()
        {
            Mode = MultiAgentConversationMode.ForkThreadPerAgent,
            SessionId = sessionId,
            RootThreadId = rootThreadId,
            ThreadPrefix = threadPrefix
        };
}

public sealed record MultiAgentConversationRoute(string? SessionId, string? ThreadId);

public sealed record MultiAgentConversationContext(
    string WorkflowExecutionId,
    string WorkflowName,
    string NodeId,
    Agent.Agent Agent,
    string Input,
    AgentGraphContext GraphContext,
    AgentNodeOptions NodeOptions);

public interface IMultiAgentConversationRuntime
{
    ValueTask<MultiAgentConversationRoute> ResolveRouteAsync(
        MultiAgentConversationContext context,
        CancellationToken cancellationToken);

    ValueTask<IAsyncDisposable> EnterRouteAsync(
        MultiAgentConversationRoute route,
        CancellationToken cancellationToken);
}

internal sealed class NoopMultiAgentConversationRuntime : IMultiAgentConversationRuntime
{
    public static NoopMultiAgentConversationRuntime Instance { get; } = new();

    private NoopMultiAgentConversationRuntime()
    {
    }

    public ValueTask<MultiAgentConversationRoute> ResolveRouteAsync(
        MultiAgentConversationContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new MultiAgentConversationRoute(null, null));

    public ValueTask<IAsyncDisposable> EnterRouteAsync(
        MultiAgentConversationRoute route,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAsyncDisposable>(NoopRouteLease.Instance);
}

internal sealed class MultiAgentConversationRuntime : IMultiAgentConversationRuntime
{
    private readonly MultiAgentConversationConfig _config;
    private readonly ISessionStore _store;
    private readonly string _workflowName;
    private readonly string _executionId;
    private readonly string _originalInput;
    private readonly string _sessionId;
    private readonly Lazy<Task> _sessionSetup;
    private readonly Lazy<Task<string>> _rootSetup;
    private readonly ConcurrentDictionary<string, Lazy<Task<MultiAgentConversationRoute>>> _routes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _routeLocks = new(StringComparer.Ordinal);

    public MultiAgentConversationRuntime(
        MultiAgentConversationConfig config,
        ISessionStore store,
        string workflowName,
        string executionId,
        string originalInput)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        _config = config;
        _store = store;
        _workflowName = workflowName;
        _executionId = executionId;
        _originalInput = originalInput;
        _sessionId = string.IsNullOrWhiteSpace(config.SessionId)
            ? $"workflow-{Normalize(workflowName)}-{Normalize(executionId)}"
            : config.SessionId!;
        _sessionSetup = new Lazy<Task>(() => EnsureSessionAsync(CancellationToken.None));
        _rootSetup = new Lazy<Task<string>>(() => EnsureRootThreadAsync(CancellationToken.None));
    }

    public ValueTask<MultiAgentConversationRoute> ResolveRouteAsync(
        MultiAgentConversationContext context,
        CancellationToken cancellationToken)
    {
        if (_config.Mode == MultiAgentConversationMode.None)
        {
            return ValueTask.FromResult(new MultiAgentConversationRoute(null, null));
        }

        var route = _routes.GetOrAdd(context.NodeId, nodeId =>
            new Lazy<Task<MultiAgentConversationRoute>>(
                () => ResolveRouteCoreAsync(context with { NodeId = nodeId }, cancellationToken)));

        return new ValueTask<MultiAgentConversationRoute>(route.Value);
    }

    public async ValueTask<IAsyncDisposable> EnterRouteAsync(
        MultiAgentConversationRoute route,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(route.SessionId) ||
            string.IsNullOrWhiteSpace(route.ThreadId))
        {
            return NoopRouteLease.Instance;
        }

        var key = $"{route.SessionId}:{route.ThreadId}";
        var gate = _routeLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreRouteLease(gate);
    }

    private async Task<MultiAgentConversationRoute> ResolveRouteCoreAsync(
        MultiAgentConversationContext context,
        CancellationToken cancellationToken)
    {
        await _sessionSetup.Value.ConfigureAwait(false);

        return _config.Mode switch
        {
            MultiAgentConversationMode.SharedWorkflowThread =>
                new MultiAgentConversationRoute(_sessionId, await _rootSetup.Value.ConfigureAwait(false)),

            MultiAgentConversationMode.ThreadPerAgent =>
                new MultiAgentConversationRoute(
                    _sessionId,
                    await EnsureAgentThreadAsync(context, forkFromRoot: false, cancellationToken).ConfigureAwait(false)),

            MultiAgentConversationMode.ForkThreadPerAgent =>
                new MultiAgentConversationRoute(
                    _sessionId,
                    await EnsureAgentThreadAsync(context, forkFromRoot: true, cancellationToken).ConfigureAwait(false)),

            _ => new MultiAgentConversationRoute(null, null)
        };
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        var existing = await _store.LoadSessionAsync(_sessionId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            existing.Store = _store;
            ApplySessionMetadata(existing);
            await _store.SaveSessionAsync(existing, cancellationToken).ConfigureAwait(false);
            return;
        }

        var bootstrap = new Agent.Agent(new AgentConfig { Name = "MultiAgentConversationBootstrap" }, null, null);
        bootstrap.Config!.SessionStore = _store;
        bootstrap.Config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
        await bootstrap.CreateSessionAsync(
            _sessionId,
            BuildSessionMetadata(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> EnsureRootThreadAsync(CancellationToken cancellationToken)
    {
        await _sessionSetup.Value.ConfigureAwait(false);

        var rootThreadId = NormalizeThreadId(_config.RootThreadId);
        var thread = await _store.LoadThreadAsync(_sessionId, rootThreadId, cancellationToken).ConfigureAwait(false);
        if (thread == null)
        {
            var bootstrap = new Agent.Agent(new AgentConfig { Name = "MultiAgentConversationBootstrap" }, null, null);
            bootstrap.Config!.SessionStore = _store;
            bootstrap.Config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
            await bootstrap.CreateThreadAsync(_sessionId, rootThreadId, "Workflow", cancellationToken).ConfigureAwait(false);
            thread = await _store.LoadThreadAsync(_sessionId, rootThreadId, cancellationToken).ConfigureAwait(false);
        }

        if (thread != null)
        {
            ApplyWorkflowMetadata(thread, nodeId: null, agent: null);

            if (_config.Mode == MultiAgentConversationMode.ForkThreadPerAgent && thread.Messages.Count == 0)
            {
                thread.AddMessage(new ChatMessage(ChatRole.User, _originalInput)
                {
                    MessageId = $"workflow-input-{Normalize(_executionId)}"
                });
            }

            await _store.SaveInitialThreadAsync(_sessionId, thread, cancellationToken).ConfigureAwait(false);
        }

        return rootThreadId;
    }

    private async Task<string> EnsureAgentThreadAsync(
        MultiAgentConversationContext context,
        bool forkFromRoot,
        CancellationToken cancellationToken)
    {
        var threadId = BuildAgentThreadId(context.NodeId);
        var existing = await _store.LoadThreadAsync(_sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return threadId;
        }

        if (forkFromRoot)
        {
            var rootThreadId = await _rootSetup.Value.ConfigureAwait(false);
            var root = await _store.LoadThreadAsync(_sessionId, rootThreadId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Workflow root thread '{rootThreadId}' was not found.");
            var forkPoint = root.Messages.LastOrDefault()?.MessageId
                ?? throw new InvalidOperationException($"Workflow root thread '{rootThreadId}' has no message to fork from.");

            await context.Agent.ForkThreadAsync(
                _sessionId,
                rootThreadId,
                threadId,
                forkPoint,
                BuildThreadMetadata(context.NodeId, context.Agent),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await context.Agent.CreateThreadAsync(
                _sessionId,
                threadId,
                context.NodeId,
                cancellationToken).ConfigureAwait(false);
        }

        var thread = await _store.LoadThreadAsync(_sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (thread != null)
        {
            ApplyWorkflowMetadata(thread, context.NodeId, context.Agent);
            await _store.AppendThreadMetadataUpdatedAsync(thread, cancellationToken).ConfigureAwait(false);
        }

        return threadId;
    }

    private string BuildAgentThreadId(string nodeId)
    {
        var prefix = NormalizeThreadId(_config.ThreadPrefix);
        return $"{prefix}-{Normalize(_executionId)}-{Normalize(nodeId)}";
    }

    private void ApplyWorkflowMetadata(HPD.Agent.Thread thread, string? nodeId, Agent.Agent? agent)
    {
        foreach (var kvp in BuildThreadMetadata(nodeId, agent))
        {
            thread.Metadata[kvp.Key] = kvp.Value;
        }
    }

    private void ApplySessionMetadata(Session session)
    {
        foreach (var kvp in BuildSessionMetadata())
        {
            session.Metadata[kvp.Key] = kvp.Value;
        }
    }

    private Dictionary<string, object> BuildSessionMetadata()
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = "multi-agent",
            ["workspaceKind"] = "multi-agent-workflow",
            ["workflowName"] = _workflowName,
            ["workflowExecutionId"] = _executionId,
            ["conversationMode"] = _config.Mode.ToString(),
            ["rootThreadId"] = NormalizeThreadId(_config.RootThreadId),
            ["threadPrefix"] = NormalizeThreadId(_config.ThreadPrefix),
            ["createdBy"] = "multi-agent"
        };
    }

    private Dictionary<string, object> BuildThreadMetadata(string? nodeId, Agent.Agent? agent)
    {
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = "multi-agent",
            ["workflowName"] = _workflowName,
            ["workflowExecutionId"] = _executionId,
            ["conversationMode"] = _config.Mode.ToString(),
            ["visibility"] = "visible",
            ["createdBy"] = "multi-agent"
        };

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            metadata["nodeId"] = nodeId!;
        }

        if (agent != null)
        {
            metadata["agentId"] = agent.AgentId;
            metadata["agentName"] = agent.Name;
        }

        return metadata;
    }

    private static string NormalizeThreadId(string value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized) ? "workflow" : normalized;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "workflow";
        }

        var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9._-]+", "-");
        normalized = Regex.Replace(normalized, "-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "workflow" : normalized;
    }
}

internal sealed class NoopRouteLease : IAsyncDisposable
{
    public static NoopRouteLease Instance { get; } = new();

    private NoopRouteLease()
    {
    }

    public ValueTask DisposeAsync() => default;
}

internal sealed class SemaphoreRouteLease(SemaphoreSlim gate) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        gate.Release();
        return default;
    }
}
