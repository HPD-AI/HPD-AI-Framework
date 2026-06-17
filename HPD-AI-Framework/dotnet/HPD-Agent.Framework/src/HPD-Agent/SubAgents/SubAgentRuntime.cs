using System.ComponentModel;
using HPD.Agent.Middleware;

namespace HPD.Agent;

/// <summary>
/// Runtime helpers used by source-generated subagent wrappers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SubAgentRuntime
{
    public sealed record Route(string SessionId, string ThreadId, string RunId);

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
        var threadId = await ResolveThreadAsync(agent, subAgent, functionContext, sessionId, runId, cancellationToken)
            .ConfigureAwait(false);

        functionContext?.ResultMetadata.Set("subAgentStatus", "started");
        functionContext?.ResultMetadata.Set("subAgentSessionId", sessionId);
        functionContext?.ResultMetadata.Set("subAgentThreadId", threadId);
        functionContext?.ResultMetadata.Set("subAgentName", subAgent.Name);
        functionContext?.ResultMetadata.Set("subAgentRunId", runId);

        return new Route(sessionId, threadId, runId);
    }

    public static void MarkCompleted(
        FunctionExecutionContext? functionContext,
        Route route)
    {
        functionContext?.ResultMetadata.Set("subAgentStatus", "completed");
        functionContext?.ResultMetadata.Set("subAgentSessionId", route.SessionId);
        functionContext?.ResultMetadata.Set("subAgentThreadId", route.ThreadId);
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
        functionContext?.ResultMetadata.Set("subAgentThreadId", route.ThreadId);
        functionContext?.ResultMetadata.Set("subAgentRunId", route.RunId);
        functionContext?.ResultMetadata.Set("subAgentErrorType", exception.GetType().Name);
    }

    internal static async Task<string> CreateEmptyThreadAsync(
        Agent agent,
        string sessionId,
        string threadId,
        Dictionary<string, object>? metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var store = agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");

        var session = await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new SessionNotFoundException(sessionId);
        session.Store = store;

        if (await store.LoadThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false) != null)
            throw new InvalidOperationException($"Thread '{threadId}' already exists in session '{sessionId}'.");

        var thread = new Thread(sessionId, threadId) { Session = session };
        if (metadata != null)
        {
            var extensionMetadata = new Dictionary<string, object>(metadata, StringComparer.Ordinal);
            thread.ApplyRuntimeMetadata(extensionMetadata);
            foreach (var kvp in extensionMetadata)
                thread.Metadata[kvp.Key] = kvp.Value;
        }

        session.LastActivity = thread.LastActivity;
        await store.SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        await store.SaveInitialThreadAsync(sessionId, thread, cancellationToken).ConfigureAwait(false);
        return thread.Id;
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

    private static async Task<string> ResolveThreadAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string sessionId,
        string runId,
        CancellationToken cancellationToken)
    {
        var policy = subAgent.ExecutionPolicy;
        var metadata = BuildMetadata(subAgent, functionContext, runId);

        switch (policy.ThreadPolicy)
        {
            case SubAgentThreadPolicy.ParentThread:
                return functionContext?.ThreadId
                    ?? throw new InvalidOperationException("ParentThread subagents require a parent ThreadId.");

            case SubAgentThreadPolicy.ExistingThread:
            {
                var threadId = policy.ExistingThreadId
                    ?? throw new InvalidOperationException("ExistingThreadId is required.");
                var store = agent.Config?.SessionStore
                    ?? throw new InvalidOperationException("No session store configured.");
                _ = await store.LoadThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Existing thread '{threadId}' not found in session '{sessionId}'.");
                return threadId;
            }

            case SubAgentThreadPolicy.FreshThread:
            {
                var threadId = BuildThreadId(subAgent, runId);
                await CreateEmptyThreadAsync(agent, sessionId, threadId, metadata, cancellationToken).ConfigureAwait(false);
                return threadId;
            }

            case SubAgentThreadPolicy.ForkFromParentThread:
            {
                var parentSessionId = functionContext?.SessionId
                    ?? throw new InvalidOperationException("ForkFromParentThread subagents require a parent SessionId.");
                var parentThreadId = functionContext.ThreadId
                    ?? throw new InvalidOperationException("ForkFromParentThread subagents require a parent ThreadId.");
                var store = agent.Config?.SessionStore
                    ?? throw new InvalidOperationException("No session store configured.");
                var parentThread = await store.LoadThreadAsync(parentSessionId, parentThreadId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Parent thread '{parentThreadId}' not found in session '{parentSessionId}'.");
                var forkPoint = parentThread.Messages.LastOrDefault()?.MessageId
                    ?? throw new InvalidOperationException("Cannot fork subagent thread from an empty parent thread.");
                var threadId = BuildThreadId(subAgent, runId);
                var forkOptions = new ThreadForkOptions
                {
                    Metadata = metadata,
                    CompactionIntent = subAgent.ExecutionPolicy.ThreadCompaction switch
                    {
                        SubAgentThreadCompaction.Enabled => ThreadForkCompactionIntent.Enabled,
                        SubAgentThreadCompaction.Disabled => ThreadForkCompactionIntent.Disabled,
                        SubAgentThreadCompaction.PreferCache => ThreadForkCompactionIntent.PreferCache,
                        _ => ThreadForkCompactionIntent.Inherit
                    }
                };
                await agent.ForkThreadAsync(
                    parentSessionId,
                    parentThreadId,
                    threadId,
                    forkPoint,
                    forkOptions,
                    cancellationToken).ConfigureAwait(false);
                return threadId;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(policy.ThreadPolicy));
        }
    }

    private static string BuildThreadId(SubAgent subAgent, string runId)
    {
        var prefix = string.IsNullOrWhiteSpace(subAgent.ExecutionPolicy.ThreadNamePrefix)
            ? $"subagent/{Normalize(subAgent.Name)}"
            : subAgent.ExecutionPolicy.ThreadNamePrefix!.Trim('/');
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
            ["parentThreadId"] = functionContext?.ThreadId ?? string.Empty,
            ["parentToolCallId"] = functionContext?.FunctionCallId ?? string.Empty,
            ["subAgentRunId"] = runId,
            ["sessionPolicy"] = subAgent.ExecutionPolicy.SessionPolicy.ToString(),
            ["threadPolicy"] = subAgent.ExecutionPolicy.ThreadPolicy.ToString(),
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
