using System.ComponentModel;
using HPD.Agent.Middleware;

namespace HPD.Agent;

/// <summary>
/// Runtime helpers used by source-generated subagent wrappers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SubAgentRuntime
{
    public sealed record Route(string SessionId, string BranchId, string RunId);

    public static async Task<Route> ResolveRouteAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(subAgent);

        var policy = subAgent.ExecutionPolicy;
        policy.Validate();

        var runId = Guid.NewGuid().ToString("N");
        var sessionId = await ResolveSessionAsync(agent, subAgent, functionContext, runId, cancellationToken)
            .ConfigureAwait(false);
        var branchId = await ResolveBranchAsync(agent, subAgent, functionContext, sessionId, runId, cancellationToken)
            .ConfigureAwait(false);

        functionContext?.ResultMetadata.Set("subAgentStatus", "started");
        functionContext?.ResultMetadata.Set("subAgentSessionId", sessionId);
        functionContext?.ResultMetadata.Set("subAgentBranchId", branchId);
        functionContext?.ResultMetadata.Set("subAgentName", subAgent.Name);
        functionContext?.ResultMetadata.Set("subAgentRunId", runId);

        return new Route(sessionId, branchId, runId);
    }

    public static void MarkCompleted(
        FunctionExecutionContext? functionContext,
        Route route)
    {
        functionContext?.ResultMetadata.Set("subAgentStatus", "completed");
        functionContext?.ResultMetadata.Set("subAgentSessionId", route.SessionId);
        functionContext?.ResultMetadata.Set("subAgentBranchId", route.BranchId);
        functionContext?.ResultMetadata.Set("subAgentRunId", route.RunId);
    }

    public static void MarkFailed(
        FunctionExecutionContext? functionContext,
        Route route,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        functionContext?.ResultMetadata.Set("subAgentStatus", "failed");
        functionContext?.ResultMetadata.Set("subAgentSessionId", route.SessionId);
        functionContext?.ResultMetadata.Set("subAgentBranchId", route.BranchId);
        functionContext?.ResultMetadata.Set("subAgentRunId", route.RunId);
        functionContext?.ResultMetadata.Set("subAgentErrorType", exception.GetType().Name);
    }

    internal static async Task<string> CreateEmptyBranchAsync(
        Agent agent,
        string sessionId,
        string branchId,
        Dictionary<string, object>? metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var store = agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");

        var session = await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new SessionNotFoundException(sessionId);
        session.Store = store;

        if (await store.LoadBranchAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false) != null)
            throw new InvalidOperationException($"Branch '{branchId}' already exists in session '{sessionId}'.");

        var branch = new Branch(sessionId, branchId) { Session = session };
        if (metadata != null)
        {
            foreach (var kvp in metadata)
                branch.Metadata[kvp.Key] = kvp.Value;
        }

        session.LastActivity = branch.LastActivity;
        await store.SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        await store.SaveInitialBranchAsync(sessionId, branch, cancellationToken).ConfigureAwait(false);
        return branch.Id;
    }

    private static async Task<string> ResolveSessionAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string runId,
        CancellationToken cancellationToken)
    {
        switch (subAgent.ExecutionPolicy.SessionPolicy)
        {
            case SubAgentSessionPolicy.ParentSession:
                return functionContext?.SessionId
                    ?? throw new InvalidOperationException("ParentSession subagents require a parent SessionId.");

            case SubAgentSessionPolicy.NewSession:
            {
                var sessionId = Guid.NewGuid().ToString("N");
                await agent.CreateSessionAsync(
                    sessionId,
                    BuildMetadata(subAgent, functionContext, runId),
                    cancellationToken).ConfigureAwait(false);
                return sessionId;
            }

            case SubAgentSessionPolicy.SharedSession:
            {
                var sessionId = subAgent.ExecutionPolicy.SharedSessionId
                    ?? throw new InvalidOperationException("SharedSessionId is required.");
                var store = agent.Config?.SessionStore
                    ?? throw new InvalidOperationException("No session store configured.");
                var existing = await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                if (existing == null)
                {
                    await agent.CreateSessionAsync(
                        sessionId,
                        BuildMetadata(subAgent, functionContext, runId),
                        cancellationToken).ConfigureAwait(false);
                }
                return sessionId;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(subAgent.ExecutionPolicy.SessionPolicy));
        }
    }

    private static async Task<string> ResolveBranchAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string sessionId,
        string runId,
        CancellationToken cancellationToken)
    {
        var policy = subAgent.ExecutionPolicy;
        var metadata = BuildMetadata(subAgent, functionContext, runId);

        switch (policy.BranchPolicy)
        {
            case SubAgentBranchPolicy.ParentBranch:
                return functionContext?.BranchId
                    ?? throw new InvalidOperationException("ParentBranch subagents require a parent BranchId.");

            case SubAgentBranchPolicy.ExistingBranch:
            {
                var branchId = policy.ExistingBranchId
                    ?? throw new InvalidOperationException("ExistingBranchId is required.");
                var store = agent.Config?.SessionStore
                    ?? throw new InvalidOperationException("No session store configured.");
                _ = await store.LoadBranchAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Existing branch '{branchId}' not found in session '{sessionId}'.");
                return branchId;
            }

            case SubAgentBranchPolicy.FreshBranch:
            {
                var branchId = BuildBranchId(subAgent, runId);
                await CreateEmptyBranchAsync(agent, sessionId, branchId, metadata, cancellationToken).ConfigureAwait(false);
                return branchId;
            }

            case SubAgentBranchPolicy.ForkFromParentBranch:
            {
                var parentSessionId = functionContext?.SessionId
                    ?? throw new InvalidOperationException("ForkFromParentBranch subagents require a parent SessionId.");
                var parentBranchId = functionContext.BranchId
                    ?? throw new InvalidOperationException("ForkFromParentBranch subagents require a parent BranchId.");
                var store = agent.Config?.SessionStore
                    ?? throw new InvalidOperationException("No session store configured.");
                var parentBranch = await store.LoadBranchAsync(parentSessionId, parentBranchId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Parent branch '{parentBranchId}' not found in session '{parentSessionId}'.");
                var forkPoint = parentBranch.Messages.LastOrDefault()?.MessageId
                    ?? throw new InvalidOperationException("Cannot fork subagent branch from an empty parent branch.");
                var branchId = BuildBranchId(subAgent, runId);
                var forkOptions = new BranchForkOptions
                {
                    Metadata = metadata,
                    CompactionIntent = subAgent.ExecutionPolicy.BranchCompaction switch
                    {
                        SubAgentBranchCompaction.Enabled => BranchForkCompactionIntent.Enabled,
                        SubAgentBranchCompaction.Disabled => BranchForkCompactionIntent.Disabled,
                        _ => BranchForkCompactionIntent.Inherit
                    }
                };
                await agent.ForkBranchAsync(
                    parentSessionId,
                    parentBranchId,
                    branchId,
                    forkPoint,
                    forkOptions,
                    cancellationToken).ConfigureAwait(false);
                return branchId;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(policy.BranchPolicy));
        }
    }

    private static string BuildBranchId(SubAgent subAgent, string runId)
    {
        var prefix = string.IsNullOrWhiteSpace(subAgent.ExecutionPolicy.BranchNamePrefix)
            ? $"subagent/{Normalize(subAgent.Name)}"
            : subAgent.ExecutionPolicy.BranchNamePrefix!.Trim('/');
        return $"{prefix}/{runId[..Math.Min(12, runId.Length)]}";
    }

    private static Dictionary<string, object> BuildMetadata(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string runId)
    {
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = "subagent",
            ["subAgentName"] = subAgent.Name,
            ["subAgentSourceKind"] = subAgent.SourceKind.ToString(),
            ["parentSessionId"] = functionContext?.SessionId ?? string.Empty,
            ["parentBranchId"] = functionContext?.BranchId ?? string.Empty,
            ["parentToolCallId"] = functionContext?.FunctionCallId ?? string.Empty,
            ["subAgentRunId"] = runId,
            ["sessionPolicy"] = subAgent.ExecutionPolicy.SessionPolicy.ToString(),
            ["branchPolicy"] = subAgent.ExecutionPolicy.BranchPolicy.ToString(),
            ["visibility"] = "hidden",
            ["createdBy"] = "subagent"
        };

        if (!string.IsNullOrWhiteSpace(subAgent.AgentId))
            metadata["subAgentAgentId"] = subAgent.AgentId;

        if (subAgent.Metadata != null)
        {
            foreach (var kvp in subAgent.Metadata)
                metadata[kvp.Key] = kvp.Value;
        }

        return metadata;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "agent";

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var normalized = new string(chars).Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal))
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(normalized) ? "agent" : normalized;
    }
}
