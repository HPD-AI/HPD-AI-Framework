using System.ComponentModel;
using System.Text;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Runtime services for invoking thread-native subagents.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SubAgentRuntime
{
    /// <summary>
    /// Describes a subagent invocation request.
    /// </summary>
    public sealed record SubAgentInvocationRequest
    {
        /// <summary>
        /// Gets the subagent definition returned by the <see cref="SubAgentAttribute"/> method.
        /// </summary>
        public required SubAgent Definition { get; init; }

        /// <summary>
        /// Gets the user input to send to the child agent.
        /// </summary>
        public required string Input { get; init; }

        /// <summary>
        /// Gets the parent function execution context, when the subagent is invoked from a tool call.
        /// </summary>
        public FunctionExecutionContext? ParentContext { get; init; }

        /// <summary>
        /// Gets the model-requested invocation mode, when the subagent allows model choice.
        /// </summary>
        public AgentInvocationMode? RequestedMode { get; init; }
    }

    /// <summary>
    /// Describes the completed subagent invocation.
    /// </summary>
    public sealed record SubAgentInvocationResult
    {
        /// <summary>
        /// Gets the text returned to the parent tool call.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>
        /// Gets the session used by the child agent.
        /// </summary>
        public required string SessionId { get; init; }

        /// <summary>
        /// Gets the thread used by the child agent.
        /// </summary>
        public required string ThreadId { get; init; }

        /// <summary>
        /// Gets the runtime-generated subagent invocation id.
        /// </summary>
        public required string RunId { get; init; }

        /// <summary>
        /// Gets the child agent id.
        /// </summary>
        public required string AgentId { get; init; }
    }

    /// <summary>
    /// Describes the resolved session, thread, and run used by a subagent invocation.
    /// </summary>
    public sealed record SubAgentInvocationRoute(string SessionId, string ThreadId, string RunId);

    /// <summary>
    /// Invokes a subagent using the shared runtime path used by generated and reflection wrappers.
    /// </summary>
    /// <param name="request">The invocation request.</param>
    /// <param name="cancellationToken">A token that cancels child agent construction or execution.</param>
    /// <returns>The subagent invocation result.</returns>
    public static async Task<AgentInvocationResult> InvokeAsync(
        SubAgentInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Definition);

        var definition = request.Definition;
        AgentInvocationMode mode;
        try
        {
            mode = AgentInvocationModes.Resolve(
                definition.InvocationModePolicy,
                request.RequestedMode);
        }
        catch (InvalidOperationException ex)
        {
            return AgentInvocationModes.CreateReceiptResult(
                definition.Name,
                BackgroundTaskSourceKind.SubAgent,
                ex.Message,
                "invalid_invocation_mode");
        }

        if (mode == AgentInvocationMode.Background)
            return RegisterBackgroundInvocation(request);

        var result = await InvokeSynchronousCoreAsync(request, cancellationToken).ConfigureAwait(false);
        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Synchronous,
            Text = result.Text
        };
    }

    private static AgentInvocationResult RegisterBackgroundInvocation(SubAgentInvocationRequest request)
    {
        var definition = request.Definition;
        var parentContext = request.ParentContext;
        if (parentContext is null || !parentContext.CanRegisterBackgroundTasks)
        {
            return AgentInvocationModes.CreateReceiptResult(
                definition.Name,
                BackgroundTaskSourceKind.SubAgent,
                "Background invocation requires an active agent runtime.");
        }

        var registration = parentContext.RegisterBackgroundTask(
            new BackgroundTaskDescriptor
            {
                Name = definition.Name,
                SourceKind = BackgroundTaskSourceKind.SubAgent,
                SourceId = parentContext.FunctionCallId,
                SessionId = parentContext.SessionId,
                ThreadId = parentContext.ThreadId,
                Invocation = parentContext.InvocationSnapshot,
                Notification = definition.BackgroundNotification,
                Metadata = CreateBackgroundDescriptorMetadata(definition)
            },
            async (backgroundContext, runtimeToken) =>
            {
                var result = await InvokeSynchronousCoreAsync(
                    request with { RequestedMode = AgentInvocationMode.Synchronous },
                    runtimeToken).ConfigureAwait(false);

                backgroundContext.SetCompletion(
                    summary: result.Text,
                    metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["subAgent.sessionId"] = result.SessionId,
                        ["subAgent.threadId"] = result.ThreadId,
                        ["subAgent.runId"] = result.RunId,
                        ["subAgent.agentId"] = result.AgentId
                    });
            });

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Background,
            Background = new AgentBackgroundInvocationReceipt
            {
                Status = "background_started",
                TaskId = registration.TaskId,
                Name = registration.Name,
                SourceKind = registration.SourceKind,
                SessionId = parentContext.SessionId,
                ThreadId = parentContext.ThreadId,
                Message = $"Started {definition.Name} in the background."
            }
        };
    }

    private static async Task<SubAgentInvocationResult> InvokeSynchronousCoreAsync(
        SubAgentInvocationRequest request,
        CancellationToken cancellationToken)
    {
        var definition = request.Definition;
        definition.ExecutionPolicy.Validate();

        var agentBuilder = CreateAgentBuilder(definition, request.ParentContext);
        RegisterToolHarnesses(agentBuilder, definition);
        AttachParentSessionStore(agentBuilder, request.ParentContext);

        var agent = await agentBuilder.BuildAsync(cancellationToken).ConfigureAwait(false);
        AttachParentCoordinator(agent, request.ParentContext);
        agent.AgentMetadata = CreateSubAgentMetadata(
            agent,
            definition,
            request.ParentContext?.GetParentAgentMetadata());

        var route = await ResolveInvocationRouteAsync(
            agent,
            definition,
            request.ParentContext,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var textResult = new StringBuilder();
            using var outputSubscription = agent.SubscribeAny(evt =>
            {
                if (evt is TextDeltaEvent textDelta)
                    textResult.Append(textDelta.Text);

                return ValueTask.CompletedTask;
            });

            await agent.RunAsync(new UserMessagesInputEvent { Messages = [
                new ChatMessage(ChatRole.User, request.Input)
            ],
                SessionId = route.SessionId,
                ThreadId = route.ThreadId
            }, cancellationToken).ConfigureAwait(false);

            MarkCompleted(request.ParentContext, route);

            return new SubAgentInvocationResult
            {
                Text = textResult.Length > 0
                    ? textResult.ToString()
                    : await ResolveLastAssistantTextAsync(agent, route, cancellationToken).ConfigureAwait(false),
                SessionId = route.SessionId,
                ThreadId = route.ThreadId,
                RunId = route.RunId,
                AgentId = agent.AgentId
            };
        }
        catch (Exception ex)
        {
            MarkFailed(request.ParentContext, route, ex);
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string> CreateBackgroundDescriptorMetadata(SubAgent definition)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invocation.kind"] = "subagent",
            ["invocation.mode"] = "background",
            ["subAgent.name"] = definition.Name,
            ["subAgent.sourceKind"] = definition.SourceKind.ToString()
        };

        if (!string.IsNullOrWhiteSpace(definition.AgentId))
            metadata["subAgent.agentId"] = definition.AgentId!;

        return metadata;
    }

    /// <summary>
    /// Resolves the session and thread used by a subagent invocation.
    /// </summary>
    /// <param name="agent">The child agent.</param>
    /// <param name="subAgent">The subagent definition.</param>
    /// <param name="functionContext">The parent function context, when available.</param>
    /// <param name="cancellationToken">A token that cancels route resolution.</param>
    /// <returns>The resolved subagent invocation route.</returns>
    public static async Task<SubAgentInvocationRoute> ResolveInvocationRouteAsync(
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

        return new SubAgentInvocationRoute(sessionId, threadId, runId);
    }

    /// <summary>
    /// Marks a resolved subagent invocation as completed in the parent tool-result metadata.
    /// </summary>
    /// <param name="functionContext">The parent function context, when available.</param>
    /// <param name="route">The resolved subagent invocation route.</param>
    public static void MarkCompleted(
        FunctionExecutionContext? functionContext,
        SubAgentInvocationRoute route)
    {
        functionContext?.ResultMetadata.Set("subAgentStatus", "completed");
        functionContext?.ResultMetadata.Set("subAgentSessionId", route.SessionId);
        functionContext?.ResultMetadata.Set("subAgentThreadId", route.ThreadId);
        functionContext?.ResultMetadata.Set("subAgentRunId", route.RunId);
    }

    /// <summary>
    /// Marks a resolved subagent invocation as failed in the parent tool-result metadata.
    /// </summary>
    /// <param name="functionContext">The parent function context, when available.</param>
    /// <param name="route">The resolved subagent invocation route.</param>
    /// <param name="exception">The exception that failed the subagent invocation.</param>
    public static void MarkFailed(
        FunctionExecutionContext? functionContext,
        SubAgentInvocationRoute route,
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

        if (await store.GetThreadAsync(new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false) != null)
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

    private static AgentBuilder CreateAgentBuilder(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext)
    {
        return subAgent.SourceKind == SubAgentSourceKind.StoredAgent
            ? CreateStoredSubAgentBuilder(subAgent, functionContext)
            : CreateInlineSubAgentBuilder(subAgent, functionContext);
    }

    private static AgentBuilder CreateStoredSubAgentBuilder(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext)
    {
        if (string.IsNullOrWhiteSpace(subAgent.AgentId))
            throw new InvalidOperationException("Stored-agent subagents require AgentId.");

        var builder = new AgentBuilder().WithAgentId(subAgent.AgentId);
        var parentAgentStore = functionContext?.GetParentAgentStore();
        if (parentAgentStore != null)
            builder.WithAgentStore(parentAgentStore);

        return builder;
    }

    private static AgentBuilder CreateInlineSubAgentBuilder(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext)
    {
        if (subAgent.AgentConfig == null)
            throw new InvalidOperationException("Inline-config subagents require AgentConfig.");

        var builder = new AgentBuilder(subAgent.AgentConfig);
        var parentChatClient = functionContext?.GetParentChatClient();
        if (subAgent.AgentConfig.ResolveClientConfig(Providers.ProviderClientFamily.Chat) == null &&
            parentChatClient != null)
        {
            builder.WithChatClient(parentChatClient);
        }

        return builder;
    }

    private static void RegisterToolHarnesses(AgentBuilder builder, SubAgent subAgent)
    {
        foreach (var toolType in subAgent.ToolHarnessTypes ?? Array.Empty<Type>())
            builder.WithToolHarness(toolType);
    }

    private static void AttachParentSessionStore(
        AgentBuilder builder,
        FunctionExecutionContext? functionContext)
    {
        var parentStore = functionContext?.GetParentSessionStore();
        if (parentStore != null)
            builder.WithSessionStore(parentStore);
    }

    private static void AttachParentCoordinator(
        Agent agent,
        FunctionExecutionContext? functionContext)
    {
        var parentCoordinator = functionContext?.GetParentEventCoordinator();
        if (parentCoordinator != null)
            agent.EventCoordinator.SetParent(parentCoordinator);
    }

    private static AgentMetadata CreateSubAgentMetadata(
        Agent agent,
        SubAgent subAgent,
        AgentMetadata? parentMetadata)
    {
        var agentChain = parentMetadata is not null
            ? parentMetadata.AgentChain.Concat([subAgent.Name]).ToArray()
            : [subAgent.Name];

        return new AgentMetadata
        {
            AgentName = subAgent.Name,
            AgentId = agent.AgentId,
            ParentAgentId = parentMetadata?.AgentId,
            AgentChain = agentChain,
            Depth = (parentMetadata?.Depth ?? -1) + 1
        };
    }

    private static async Task<string> ResolveLastAssistantTextAsync(
        Agent agent,
        SubAgentInvocationRoute route,
        CancellationToken cancellationToken)
    {
        var store = agent.Config.SessionStore;
        if (store == null)
            return string.Empty;

        var fallbackThread = await store.ProjectThreadAsync(
            route.SessionId,
            route.ThreadId,
            ThreadProjectionPurpose.ThreadHistory,
            cancellationToken).ConfigureAwait(false);

        return fallbackThread?.Messages.LastOrDefault(message => message.Role == ChatRole.Assistant)?.Text
            ?? string.Empty;
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
                _ = await store.GetThreadAsync(new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false)
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
                var parentThread = await store.ProjectThreadAsync(
                        parentSessionId,
                        parentThreadId,
                        ThreadProjectionPurpose.ForkConstruction,
                        cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Parent thread '{parentThreadId}' not found in session '{parentSessionId}'.");
                var forkPoint = parentThread.Messages.LastOrDefault()?.MessageId
                    ?? throw new InvalidOperationException("Cannot fork subagent thread from an empty parent thread.");
                var threadId = BuildThreadId(subAgent, runId);
                var forkOptions = new ThreadForkOptions
                {
                    Metadata = metadata,
                    Compaction = subAgent.ExecutionPolicy.ThreadCompaction
                        ?? new InheritThreadForkCompaction()
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
