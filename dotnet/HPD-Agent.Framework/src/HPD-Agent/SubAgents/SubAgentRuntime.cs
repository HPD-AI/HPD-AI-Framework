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
    /// Creates a generated tool around one registration-time subagent declaration.
    /// </summary>
    /// <param name="definition">The immutable declaration captured during registration.</param>
    /// <param name="factory">The generated function factory.</param>
    /// <returns>The generated subagent function.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static AIFunction CreateFrozenFunction(
        SubAgent definition,
        Func<SubAgent, AIFunction> factory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(factory);
        return factory(definition);
    }

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
        /// Gets the caller-assigned name for this individual delegated task.
        /// This is distinct from the reusable subagent role name.
        /// </summary>
        public required string TaskName { get; init; }

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
        public required string InvocationId { get; init; }

        /// <summary>
        /// Gets the child agent id.
        /// </summary>
        public required string AgentId { get; init; }
    }

    /// <summary>
    /// Describes the resolved session, thread, and run used by a subagent invocation.
    /// </summary>
    public sealed record SubAgentInvocationRoute(string SessionId, string ThreadId, string InvocationId);

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
        ValidateTaskName(request.TaskName);
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
                request.TaskName,
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
                request.TaskName,
                BackgroundTaskSourceKind.SubAgent,
                "Background invocation requires an active agent runtime.");
        }

        var inheritedLease = parentContext.GetEffectiveChatClientHandle()?.AcquireLease();
        BackgroundTaskRegistration registration;
        try
        {
            registration = parentContext.RegisterBackgroundTask(
                new BackgroundTaskDescriptor
                {
                    Name = request.TaskName,
                    SourceKind = BackgroundTaskSourceKind.SubAgent,
                    SourceId = parentContext.FunctionCallId,
                    SessionId = parentContext.SessionId,
                    ThreadId = parentContext.ThreadId,
                    Invocation = parentContext.InvocationSnapshot,
                    Notification = definition.BackgroundNotification,
                    Metadata = CreateBackgroundDescriptorMetadata(definition, request.TaskName)
                },
                async (backgroundContext, runtimeToken) =>
                {
                    try
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
                                ["subAgent.invocationId"] = result.InvocationId,
                                ["subAgent.agentId"] = result.AgentId,
                                ["subAgent.taskName"] = request.TaskName
                            });
                    }
                    finally
                    {
                        if (inheritedLease is not null)
                            await inheritedLease.DisposeAsync().ConfigureAwait(false);
                    }
                });
        }
        catch
        {
            inheritedLease?.Dispose();
            throw;
        }

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
        var parentDepth = request.ParentContext?.GetParentAgentMetadata()?.Depth ?? 0;
        var maxDepth = request.ParentContext?.GetParentAgentConfigSnapshot()?.MaxSubAgentDepth ?? 4;
        if (parentDepth + 1 > maxDepth)
        {
            throw new InvalidOperationException(
                $"Subagent '{definition.Name}' would exceed MaxSubAgentDepth ({maxDepth}).");
        }

        var plannedRoute = PlanInvocationRoute(definition, request.ParentContext, request.TaskName);
        await using var runtime = await AcquireRuntimeAsync(
            definition,
            request.ParentContext,
            plannedRoute,
            cancellationToken).ConfigureAwait(false);
        var agent = runtime.Agent;
        AttachParentCoordinator(agent, request.ParentContext);

        var route = await EnsureInvocationRouteAsync(
            agent,
            definition,
            request.ParentContext,
            request.TaskName,
            plannedRoute,
            cancellationToken).ConfigureAwait(false);
        agent.AgentMetadata = CreateSubAgentMetadata(
            agent,
            definition,
            request.ParentContext?.GetParentAgentMetadata());
        var threadExecutionId = Guid.NewGuid().ToString("N");
        var publisher = new ThreadEventPublisher(
            agent.Config.SessionStore ?? throw new InvalidOperationException("No session store configured."),
            agent.EventCoordinator);
        var childRunStarted = false;

        try
        {
            if (request.ParentContext is not null)
            {
                await request.ParentContext.PublishAsync(new SubAgentInvocationStartedEvent(
                    route.InvocationId,
                    request.ParentContext.FunctionCallId,
                    definition.AgentId,
                    route.SessionId,
                    route.ThreadId,
                    definition.Name,
                    request.TaskName,
                    AgentInvocationMode.Synchronous), cancellationToken).ConfigureAwait(false);
            }
            await publisher.CommitAndPublishAsync(
                new ThreadKey(route.SessionId, route.ThreadId),
                new ThreadExecutionStartedEvent(threadExecutionId, definition.AgentId, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            childRunStarted = true;

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
                ThreadId = route.ThreadId,
                InheritedChatClient = request.ParentContext?.GetEffectiveChatClientHandle()
            }, cancellationToken).ConfigureAwait(false);

            var text = textResult.Length > 0
                ? textResult.ToString()
                : await ResolveLastAssistantTextAsync(agent, route, cancellationToken).ConfigureAwait(false);
            await publisher.CommitAndPublishAsync(
                new ThreadKey(route.SessionId, route.ThreadId),
                new ThreadExecutionFinishedEvent(
                    threadExecutionId,
                    definition.AgentId,
                    ThreadExecutionOutcome.Succeeded,
                    DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            childRunStarted = false;
            if (request.ParentContext is not null)
            {
                await request.ParentContext.PublishAsync(
                    new SubAgentInvocationCompletedEvent(route.InvocationId, text),
                    CancellationToken.None).ConfigureAwait(false);
            }
            MarkCompleted(request.ParentContext, route);

            return new SubAgentInvocationResult
            {
                Text = text,
                SessionId = route.SessionId,
                ThreadId = route.ThreadId,
                InvocationId = route.InvocationId,
                AgentId = agent.AgentId
            };
        }
        catch (Exception ex)
        {
            if (childRunStarted)
            {
                await publisher.CommitAndPublishAsync(
                    new ThreadKey(route.SessionId, route.ThreadId),
                    new ThreadExecutionFinishedEvent(
                        threadExecutionId,
                        definition.AgentId,
                        ex is OperationCanceledException
                            ? ThreadExecutionOutcome.Cancelled
                            : ThreadExecutionOutcome.Failed,
                        DateTimeOffset.UtcNow,
                        ex is OperationCanceledException
                            ? null
                            : new ThreadExecutionError(ex.GetType().Name, ex.Message)),
                    CancellationToken.None).ConfigureAwait(false);
            }
            if (request.ParentContext is not null)
            {
                AgentEvent terminal = ex is OperationCanceledException
                    ? new SubAgentInvocationCancelledEvent(route.InvocationId, ex.Message)
                    : new SubAgentInvocationFailedEvent(route.InvocationId, ex.GetType().Name, ex.Message);
                await request.ParentContext.PublishAsync(terminal, CancellationToken.None).ConfigureAwait(false);
            }
            MarkFailed(request.ParentContext, route, ex);
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string> CreateBackgroundDescriptorMetadata(
        SubAgent definition,
        string taskName)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invocation.kind"] = "subagent",
            ["invocation.mode"] = "background",
            ["subAgent.name"] = definition.Name,
            ["subAgent.taskName"] = taskName,
            ["subAgent.sourceKind"] = definition.Configuration.GetType().Name
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
        string taskName,
        CancellationToken cancellationToken)
    {
        var route = PlanInvocationRoute(subAgent, functionContext, taskName);
        return await EnsureInvocationRouteAsync(
            agent,
            subAgent,
            functionContext,
            taskName,
            route,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SubAgentInvocationRoute> EnsureInvocationRouteAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string taskName,
        SubAgentInvocationRoute route,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(subAgent);
        ValidateTaskName(taskName);

        var policy = subAgent.ExecutionPolicy;
        policy.Validate();

        var runId = route.InvocationId;
        var sessionId = await ResolveSessionAsync(agent, subAgent, functionContext, taskName, runId, cancellationToken)
            .ConfigureAwait(false);
        var threadId = await ResolveThreadAsync(agent, subAgent, functionContext, taskName, sessionId, runId, cancellationToken)
            .ConfigureAwait(false);

        functionContext?.ResultMetadata.Set("subAgentStatus", "started");
        functionContext?.ResultMetadata.Set("subAgentSessionId", sessionId);
        functionContext?.ResultMetadata.Set("subAgentThreadId", threadId);
        functionContext?.ResultMetadata.Set("subAgentName", subAgent.Name);
        functionContext?.ResultMetadata.Set("subAgentTaskName", taskName);
        functionContext?.ResultMetadata.Set("invocationId", runId);

        return new SubAgentInvocationRoute(sessionId, threadId, runId);
    }

    private static SubAgentInvocationRoute PlanInvocationRoute(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string taskName)
    {
        var runId = Guid.NewGuid().ToString("N");
        var sessionId = subAgent.ExecutionPolicy.SessionPolicy switch
        {
            SubAgentSessionPolicy.ParentSession => functionContext?.SessionId
                ?? throw new InvalidOperationException("ParentSession subagents require a parent SessionId."),
            SubAgentSessionPolicy.SharedSession => subAgent.ExecutionPolicy.SharedSessionId
                ?? throw new InvalidOperationException("SharedSessionId is required."),
            SubAgentSessionPolicy.NewSession => BuildSessionId(subAgent, taskName, runId),
            _ => throw new ArgumentOutOfRangeException(nameof(subAgent.ExecutionPolicy.SessionPolicy))
        };
        var threadId = subAgent.ExecutionPolicy.ThreadPolicy == SubAgentThreadPolicy.ExistingThread
            ? subAgent.ExecutionPolicy.ExistingThreadId
                ?? throw new InvalidOperationException("ExistingThreadId is required.")
            : BuildThreadId(subAgent, taskName, runId);
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
        functionContext?.ResultMetadata.Set("invocationId", route.InvocationId);
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
        functionContext?.ResultMetadata.Set("invocationId", route.InvocationId);
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

        var thread = new Thread(sessionId, threadId, agent.AgentId)
        {
            Session = session
        };
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

    private static async Task<IAgentRuntimeLease> AcquireRuntimeAsync(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        SubAgentInvocationRoute route,
        CancellationToken cancellationToken)
    {
        if (functionContext?.Services?.GetService(typeof(IAgentRuntimeResolver)) is IAgentRuntimeResolver resolver)
        {
            return await resolver.GetOrBuildAsync(
                subAgent.AgentId,
                route.SessionId,
                route.ThreadId,
                cancellationToken).ConfigureAwait(false);
        }

        var agentStore = functionContext?.GetParentAgentStore()
            ?? throw new InvalidOperationException("Standalone subagent execution requires an IAgentStore.");
        var sessionStore = functionContext.GetParentSessionStore()
            ?? throw new InvalidOperationException("Standalone subagent execution requires an ISessionStore.");
        var stored = await agentStore.LoadAsync(subAgent.AgentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Subagent definition '{subAgent.AgentId}' was not found.");
        var config = AgentConfigSnapshot.Create(stored.Config);
        config.AgentId = subAgent.AgentId;
        var builder = new AgentBuilder(config)
            .WithAgentStore(agentStore)
            .WithSessionStore(sessionStore);
        if (functionContext.Services is not null)
            builder.WithServiceProvider(functionContext.Services);
        var agent = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        return new LocalAgentRuntimeLease(agent);
    }

    private sealed class LocalAgentRuntimeLease(Agent agent) : IAgentRuntimeLease
    {
        public Agent Agent { get; } = agent;
        public ValueTask DisposeAsync()
        {
            Agent.Dispose();
            return ValueTask.CompletedTask;
        }
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
        string taskName,
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
                var sessionId = BuildSessionId(subAgent, taskName, runId);
                await agent.CreateSessionAsync(
                    sessionId,
                    BuildMetadata(subAgent, functionContext, taskName, runId),
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
                        BuildMetadata(subAgent, functionContext, taskName, runId),
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
        string taskName,
        string sessionId,
        string runId,
        CancellationToken cancellationToken)
    {
        var policy = subAgent.ExecutionPolicy;
        var metadata = BuildMetadata(subAgent, functionContext, taskName, runId);

        switch (policy.ThreadPolicy)
        {
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
                var threadId = BuildThreadId(subAgent, taskName, runId);
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
                var threadId = BuildThreadId(subAgent, taskName, runId);
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

    private static string BuildThreadId(SubAgent subAgent, string taskName, string runId)
    {
        var prefix = string.IsNullOrWhiteSpace(subAgent.ExecutionPolicy.ThreadNamePrefix)
            ? $"subagent/{Normalize(subAgent.Name)}"
            : subAgent.ExecutionPolicy.ThreadNamePrefix!.Trim('/');
        return $"{prefix}/{Normalize(taskName)}/{runId[..Math.Min(12, runId.Length)]}";
    }

    private static string BuildSessionId(SubAgent subAgent, string taskName, string runId) =>
        $"subagent/{Normalize(subAgent.Name)}/{Normalize(taskName)}/{runId[..Math.Min(12, runId.Length)]}";

    private static Dictionary<string, object> BuildMetadata(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string taskName,
        string runId)
    {
        var metadata = subAgent.Metadata is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(subAgent.Metadata, StringComparer.Ordinal);

        // Runtime-owned routing fields are authoritative and cannot be replaced by
        // application metadata supplied on the reusable subagent definition.
        metadata["kind"] = "subagent";
        metadata["subAgentName"] = subAgent.Name;
        metadata["subAgentTaskName"] = taskName;
        metadata["subAgentSourceKind"] = subAgent.Configuration.GetType().Name;
        metadata["parentSessionId"] = functionContext?.SessionId ?? string.Empty;
        metadata["parentThreadId"] = functionContext?.ThreadId ?? string.Empty;
        metadata["parentToolCallId"] = functionContext?.FunctionCallId ?? string.Empty;
        metadata["invocationId"] = runId;
        metadata["sessionPolicy"] = subAgent.ExecutionPolicy.SessionPolicy.ToString();
        metadata["threadPolicy"] = subAgent.ExecutionPolicy.ThreadPolicy.ToString();
        metadata["visibility"] = "hidden";
        metadata["createdBy"] = "subagent";

        metadata["defaultAgentId"] = subAgent.AgentId;

        return metadata;
    }

    private static void ValidateTaskName(string taskName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        if (taskName.Length > 80)
            throw new ArgumentException("Subagent task names cannot exceed 80 characters.", nameof(taskName));

        if (!taskName.Any(char.IsLetterOrDigit))
            throw new ArgumentException("Subagent task names must contain at least one letter or number.", nameof(taskName));
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
