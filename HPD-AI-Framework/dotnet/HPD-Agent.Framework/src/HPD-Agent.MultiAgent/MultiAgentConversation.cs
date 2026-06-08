using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.MultiAgent;

/// <summary>
/// Determines how a multi-agent workflow writes node agent transcripts into HPD sessions and branches.
/// </summary>
public enum MultiAgentConversationMode
{
    /// <summary>
    /// Preserve process-local workflow execution. Node agent turns are not routed into durable sessions.
    /// </summary>
    None,

    /// <summary>
    /// Route every node agent turn into one shared workflow branch.
    /// </summary>
    SharedWorkflowBranch,

    /// <summary>
    /// Route each node agent into a stable branch inside one workflow session.
    /// </summary>
    BranchPerAgent,

    /// <summary>
    /// Fork one branch per node agent from a shared workflow root branch.
    /// </summary>
    ForkBranchPerAgent
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
    /// Root branch id used by shared and forked policies.
    /// </summary>
    public string RootBranchId { get; init; } = "workflow";

    /// <summary>
    /// Prefix used when creating per-agent branches.
    /// </summary>
    public string BranchPrefix { get; init; } = "workflow-agent";
}

/// <summary>
/// Convenience factory for multi-agent conversation policies.
/// </summary>
public static class MultiAgentConversationPolicies
{
    public static MultiAgentConversationConfig None() => new();

    public static MultiAgentConversationConfig SharedWorkflowBranch(
        string? sessionId = null,
        string rootBranchId = "workflow") =>
        new()
        {
            Mode = MultiAgentConversationMode.SharedWorkflowBranch,
            SessionId = sessionId,
            RootBranchId = rootBranchId
        };

    public static MultiAgentConversationConfig BranchPerAgent(
        string? sessionId = null,
        string branchPrefix = "workflow-agent") =>
        new()
        {
            Mode = MultiAgentConversationMode.BranchPerAgent,
            SessionId = sessionId,
            BranchPrefix = branchPrefix
        };

    public static MultiAgentConversationConfig ForkBranchPerAgent(
        string? sessionId = null,
        string rootBranchId = "workflow",
        string branchPrefix = "workflow-agent") =>
        new()
        {
            Mode = MultiAgentConversationMode.ForkBranchPerAgent,
            SessionId = sessionId,
            RootBranchId = rootBranchId,
            BranchPrefix = branchPrefix
        };
}

public sealed record MultiAgentConversationRoute(string? SessionId, string? BranchId);

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
        _rootSetup = new Lazy<Task<string>>(() => EnsureRootBranchAsync(CancellationToken.None));
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
            string.IsNullOrWhiteSpace(route.BranchId))
        {
            return NoopRouteLease.Instance;
        }

        var key = $"{route.SessionId}:{route.BranchId}";
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
            MultiAgentConversationMode.SharedWorkflowBranch =>
                new MultiAgentConversationRoute(_sessionId, await _rootSetup.Value.ConfigureAwait(false)),

            MultiAgentConversationMode.BranchPerAgent =>
                new MultiAgentConversationRoute(
                    _sessionId,
                    await EnsureAgentBranchAsync(context, forkFromRoot: false, cancellationToken).ConfigureAwait(false)),

            MultiAgentConversationMode.ForkBranchPerAgent =>
                new MultiAgentConversationRoute(
                    _sessionId,
                    await EnsureAgentBranchAsync(context, forkFromRoot: true, cancellationToken).ConfigureAwait(false)),

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

    private async Task<string> EnsureRootBranchAsync(CancellationToken cancellationToken)
    {
        await _sessionSetup.Value.ConfigureAwait(false);

        var rootBranchId = NormalizeBranchId(_config.RootBranchId);
        var branch = await _store.LoadBranchAsync(_sessionId, rootBranchId, cancellationToken).ConfigureAwait(false);
        if (branch == null)
        {
            var bootstrap = new Agent.Agent(new AgentConfig { Name = "MultiAgentConversationBootstrap" }, null, null);
            bootstrap.Config!.SessionStore = _store;
            bootstrap.Config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
            await bootstrap.CreateBranchAsync(_sessionId, rootBranchId, "Workflow", cancellationToken).ConfigureAwait(false);
            branch = await _store.LoadBranchAsync(_sessionId, rootBranchId, cancellationToken).ConfigureAwait(false);
        }

        if (branch != null)
        {
            ApplyWorkflowMetadata(branch, nodeId: null, agent: null);

            if (_config.Mode == MultiAgentConversationMode.ForkBranchPerAgent && branch.Messages.Count == 0)
            {
                branch.AddMessage(new ChatMessage(ChatRole.User, _originalInput)
                {
                    MessageId = $"workflow-input-{Normalize(_executionId)}"
                });
            }

            await _store.SaveInitialBranchAsync(_sessionId, branch, cancellationToken).ConfigureAwait(false);
        }

        return rootBranchId;
    }

    private async Task<string> EnsureAgentBranchAsync(
        MultiAgentConversationContext context,
        bool forkFromRoot,
        CancellationToken cancellationToken)
    {
        var branchId = BuildAgentBranchId(context.NodeId);
        var existing = await _store.LoadBranchAsync(_sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return branchId;
        }

        if (forkFromRoot)
        {
            var rootBranchId = await _rootSetup.Value.ConfigureAwait(false);
            var root = await _store.LoadBranchAsync(_sessionId, rootBranchId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Workflow root branch '{rootBranchId}' was not found.");
            var forkPoint = root.Messages.LastOrDefault()?.MessageId
                ?? throw new InvalidOperationException($"Workflow root branch '{rootBranchId}' has no message to fork from.");

            await context.Agent.ForkBranchAsync(
                _sessionId,
                rootBranchId,
                branchId,
                forkPoint,
                BuildBranchMetadata(context.NodeId, context.Agent),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await context.Agent.CreateBranchAsync(
                _sessionId,
                branchId,
                context.NodeId,
                cancellationToken).ConfigureAwait(false);
        }

        var branch = await _store.LoadBranchAsync(_sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (branch != null)
        {
            ApplyWorkflowMetadata(branch, context.NodeId, context.Agent);
            await _store.SaveInitialBranchAsync(_sessionId, branch, cancellationToken).ConfigureAwait(false);
        }

        return branchId;
    }

    private string BuildAgentBranchId(string nodeId)
    {
        var prefix = NormalizeBranchId(_config.BranchPrefix);
        return $"{prefix}-{Normalize(_executionId)}-{Normalize(nodeId)}";
    }

    private void ApplyWorkflowMetadata(Branch branch, string? nodeId, Agent.Agent? agent)
    {
        foreach (var kvp in BuildBranchMetadata(nodeId, agent))
        {
            branch.Metadata[kvp.Key] = kvp.Value;
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
            ["rootBranchId"] = NormalizeBranchId(_config.RootBranchId),
            ["branchPrefix"] = NormalizeBranchId(_config.BranchPrefix),
            ["createdBy"] = "multi-agent"
        };
    }

    private Dictionary<string, object> BuildBranchMetadata(string? nodeId, Agent.Agent? agent)
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

    private static string NormalizeBranchId(string value)
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
