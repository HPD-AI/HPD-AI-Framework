using System.ComponentModel;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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

        /// <summary>Gets the stable capability identity used for idempotent child allocation.</summary>
        public required CapabilityId CapabilityId { get; init; }

        /// <summary>
        /// Gets the parent function execution context, when the subagent is invoked from a tool call.
        /// </summary>
        public FunctionExecutionContext? ParentContext { get; init; }

        /// <summary>
        /// Gets the model-requested invocation mode, when the subagent allows model choice.
        /// </summary>
        public AgentInvocationMode? RequestedMode { get; init; }

        /// <summary>
        /// Gets the model-requested child context, when the definition allows model choice.
        /// </summary>
        public SubAgentContext? RequestedContext { get; init; }
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

        /// <summary>Gets the framework-generated identifier local to the parent thread.</summary>
        public SubAgentLocalId? LocalId { get; init; }
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
        AgentInvocationMode mode;
        try
        {
            mode = AgentInvocationModes.Resolve(
                definition.InvocationModePolicy,
                request.RequestedMode);
        }
        catch (InvalidOperationException ex)
        {
            return AgentInvocationModes.CreateFailureResult(
                GetCreationStorageName(request),
                AgentOperationSourceKind.SubAgent,
                ex.Message,
                "invalid_invocation_mode");
        }

        var admission = await AdmitInvocationAsync(request, cancellationToken).ConfigureAwait(false);

        if (mode == AgentInvocationMode.Background)
            return await RegisterBackgroundInvocationAsync(request, admission).ConfigureAwait(false);

        var result = await InvokeSynchronousCoreAsync(
            request,
            admission,
            AgentInvocationMode.Synchronous,
            cancellationToken).ConfigureAwait(false);
        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Synchronous,
            Text = result.Text,
            ToolResult = new SubAgentOperationResult
            {
                Status = SubAgentOperationStatus.Completed,
                Child = result.LocalId?.Value,
                InvocationId = result.InvocationId,
                Output = result.Text
            }
        };
    }

    private static async Task<AgentInvocationResult> RegisterBackgroundInvocationAsync(
        SubAgentInvocationRequest request,
        AdmittedSubAgentInvocation admission)
    {
        var definition = request.Definition;
        var parentContext = request.ParentContext;
        if (parentContext is null || !parentContext.CanStartOperations)
        {
            return AgentInvocationModes.CreateFailureResult(
                GetCreationStorageName(request),
                AgentOperationSourceKind.SubAgent,
                "Background invocation requires an active agent runtime.");
        }

        var receipt = await parentContext.StartOperationAsync(
                GetCreationStorageName(request),
                CreateBackgroundDescriptorMetadata(definition, GetCreationStorageName(request)),
                definition.OperationNotification,
                async (_, runtimeToken) =>
                {
                    var result = await InvokeSynchronousCoreAsync(
                        request,
                        admission,
                        AgentInvocationMode.Background,
                        runtimeToken).ConfigureAwait(false);
                    return new AgentOperationCompletion(result.Text);
                }).ConfigureAwait(false);

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Background,
            Operation = receipt
        };
    }

    private static async Task<SubAgentInvocationResult> InvokeSynchronousCoreAsync(
        SubAgentInvocationRequest request,
        AdmittedSubAgentInvocation admission,
        AgentInvocationMode invocationMode,
        CancellationToken cancellationToken)
    {
        var definition = request.Definition;
        var contextPolicy = admission.ContextPolicy;
        await using var runtime = await AcquireRuntimeAsync(
            definition,
            request.ParentContext,
            admission.Route,
            cancellationToken).ConfigureAwait(false);
        var agent = runtime.Agent;
        AttachParentCoordinator(agent, request.ParentContext);

        var route = admission.Route;
        var localId = admission.LocalId;
        agent.AgentMetadata = CreateSubAgentMetadata(
            agent,
            definition,
            request.ParentContext?.GetParentAgentMetadata());
        var threadExecutionId = Guid.NewGuid().ToString("N");
        var publisher = new AgentEventPublisher(
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
                    contextPolicy,
                    invocationMode), cancellationToken).ConfigureAwait(false);
            }
            await publisher.CommitAndPublishAsync(
                new ThreadKey(route.SessionId, route.ThreadId),
                new ThreadExecutionStartedEvent(threadExecutionId, definition.AgentId, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            childRunStarted = true;
            var initialMessageCount = await ResolveMessageCountAsync(
                agent,
                route,
                cancellationToken).ConfigureAwait(false);

            await using var inheritedClientLease = request.ParentContext?.ClientSet?.AcquireBorrowedLease();
            await agent.RunAsync(new UserMessagesInputEvent { Messages = [
                new ChatMessage(ChatRole.User, request.Input)
            ],
                SessionId = route.SessionId,
                ThreadId = route.ThreadId,
                RunConfig = definition.RunConfig.Resolve(
                    request.ParentContext?.RunConfig,
                    request.ParentContext?.ClientSet,
                    agent.Config,
                    agent.ProviderComposition),
                InheritedChatClient = request.ParentContext?.GetEffectiveChatClientHandle(),
                InheritedChatMode = definition.RunConfig.Clients.Chat
            }, cancellationToken).ConfigureAwait(false);

            var text = await ResolveAssistantTextAfterAsync(
                agent,
                route,
                initialMessageCount,
                cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(
                    $"Subagent '{definition.Name}' completed without an assistant response.");
            }
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
                AgentId = agent.AgentId,
                LocalId = localId
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

    /// <summary>
    /// Completes the durable admission phase shared by synchronous and background creation.
    /// Child routing and parent registration are committed before any execution is scheduled.
    /// </summary>
    private static async ValueTask<AdmittedSubAgentInvocation> AdmitInvocationAsync(
        SubAgentInvocationRequest request,
        CancellationToken cancellationToken)
    {
        var definition = request.Definition;
        var contextPolicy = SubAgentContexts.Resolve(definition.ContextPolicy, request.RequestedContext);
        ValidateCreationDepth(request);
        var replay = await FindRegisteredCreationAsync(request, cancellationToken).ConfigureAwait(false);
        if (replay is { ChildThread: { } replayRoute })
        {
            return new AdmittedSubAgentInvocation(
                new SubAgentInvocationRoute(
                    replayRoute.SessionId,
                    replayRoute.ThreadId,
                    replay.CreationInvocationId),
                replay.LocalId,
                replay.CreationContext switch
                {
                    SubAgentCreationContext.Fork => SubAgentContextPolicy.Fork,
                    SubAgentCreationContext.Fresh => SubAgentContextPolicy.Fresh,
                    SubAgentCreationContext.Isolated => SubAgentContextPolicy.Isolated,
                    _ => throw new InvalidOperationException("subagent_creation_context_invalid")
                });
        }
        var plannedRoute = PlanInvocationRoute(
            definition,
            request.ParentContext,
            GetCreationStorageName(request),
            contextPolicy);
        await using var runtime = await AcquireRuntimeAsync(
            definition,
            request.ParentContext,
            plannedRoute,
            cancellationToken).ConfigureAwait(false);
        AttachParentCoordinator(runtime.Agent, request.ParentContext);
        var route = await EnsureInvocationRouteAsync(
            runtime.Agent,
            definition,
            request.ParentContext,
            GetCreationStorageName(request),
            plannedRoute,
            contextPolicy,
            cancellationToken).ConfigureAwait(false);
        var localId = await RegisterChildAsync(request, route, contextPolicy, cancellationToken)
            .ConfigureAwait(false);
        return new AdmittedSubAgentInvocation(route, localId, contextPolicy);
    }

    private static void ValidateCreationDepth(SubAgentInvocationRequest request)
    {
        var definition = request.Definition;
        var parentDepth = request.ParentContext?.GetParentAgentMetadata()?.Depth ?? 0;
        var maxDepth = request.ParentContext?.GetParentAgentConfigSnapshot()?.MaxSubAgentDepth ?? 4;
        if (!definition.Availability.AllowsInvocationFrom(parentDepth))
        {
            var maximumDepth = definition.Availability.MaximumChildDepth;
            throw new InvalidOperationException(
                maximumDepth is null
                    ? $"Subagent '{definition.Name}' is not available from agent depth {parentDepth}."
                    : $"Subagent '{definition.Name}' may only create children through depth {maximumDepth.Value}.");
        }
        if (parentDepth + 1 > maxDepth)
            throw new InvalidOperationException(
                $"Subagent '{definition.Name}' would exceed MaxSubAgentDepth ({maxDepth}).");
    }

    private static async ValueTask<SubAgentChildReference?> FindRegisteredCreationAsync(
        SubAgentInvocationRequest request,
        CancellationToken cancellationToken)
    {
        var context = request.ParentContext;
        var store = context?.GetParentSessionStore();
        if (context?.SessionId is null || context.ThreadId is null || store is null)
            return null;
        var projection = await new SubAgentChildRegistry(store).ProjectAsync(
            new ThreadKey(context.SessionId, context.ThreadId),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return projection.Children.Values.FirstOrDefault(child =>
            string.Equals(child.ParentToolCallId, context.FunctionCallId, StringComparison.Ordinal) &&
            child.CapabilityId == request.CapabilityId);
    }

    private sealed record AdmittedSubAgentInvocation(
        SubAgentInvocationRoute Route,
        SubAgentLocalId? LocalId,
        SubAgentContextPolicy ContextPolicy);

    private static IReadOnlyDictionary<string, string> CreateBackgroundDescriptorMetadata(
        SubAgent definition,
        string taskName)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invocation.kind"] = "subagent",
            ["invocation.mode"] = "background",
            ["subAgent.name"] = definition.Name,
            ["subAgent.localStorageName"] = taskName,
            ["subAgent.sourceKind"] = definition.Configuration.GetType().Name
        };

        if (!string.IsNullOrWhiteSpace(definition.AgentId))
            metadata["subAgent.agentId"] = definition.AgentId!;

        return metadata;
    }

    /// <summary>Dispatches a framework-owned lifecycle action against the current parent's registry.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<object?> ControlAsync(
        string action,
        JsonElement branch,
        FunctionExecutionContext functionContext,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(functionContext);
        var store = functionContext.GetParentSessionStore()
            ?? throw new InvalidOperationException("subagent_unavailable: no durable parent session store is configured.");
        if (functionContext.SessionId is null || functionContext.ThreadId is null)
            throw new InvalidOperationException("subagent_unavailable: the current parent has no durable thread identity.");
        var registry = new SubAgentChildRegistry(store);
        var projection = await registry.ProjectAsync(
            new ThreadKey(functionContext.SessionId, functionContext.ThreadId),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.Equals(action, "list", StringComparison.Ordinal))
        {
            return new SubAgentListResult(projection.Children.Values
                .OrderBy(static child => child.LocalId.Value, StringComparer.Ordinal)
                .Select(static child => new SubAgentListItem(
                    child.LocalId.Value,
                    child.RoleName,
                    child.Availability,
                    child.CreatedAt,
                    child.UnavailableReason))
                .ToArray());
        }

        var localValue = branch.TryGetProperty("child", out var childProperty)
            ? childProperty.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(localValue) ||
            !projection.TryGet(new SubAgentLocalId(localValue), out var child))
            return Failure("subagent_unknown", "This child is not registered under the current parent. Use list to inspect available children.");
        if (child.Availability == SubAgentChildAvailability.Detached)
            return Failure("subagent_detached_by_fork", child.UnavailableReason ?? "This child was detached by the parent fork. Start a new role action.", child.LocalId.Value);
        if (child.Availability != SubAgentChildAvailability.Available || child.ChildThread is null)
            return Failure("subagent_unavailable", child.UnavailableReason ?? "This child is currently unavailable.", child.LocalId.Value);

        if (string.Equals(action, "continue", StringComparison.Ordinal))
        {
            var resolver = functionContext.Services?.GetService<IAgentRuntimeResolver>()
                ?? throw new InvalidOperationException("subagent_unavailable: no agent runtime resolver is configured.");
            var route = child.ChildThread.Value;
            var input = branch.GetProperty("input").GetString() ?? string.Empty;
            var executionId = Guid.NewGuid().ToString("N");
            var invocationId = Guid.NewGuid().ToString("N");
            await using var lease = await resolver.GetOrBuildAsync(
                child.ChildAgentId, route.SessionId, route.ThreadId, cancellationToken).ConfigureAwait(false);
            var publisher = new AgentEventPublisher(store, lease.Agent.EventCoordinator);
            await publisher.CommitAndPublishAsync(
                route,
                new ThreadExecutionStartedEvent(executionId, child.ChildAgentId, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            try
            {
                await lease.Agent.RunAsync(new UserMessagesInputEvent
                {
                    Messages = [new ChatMessage(ChatRole.User, input)],
                    SessionId = route.SessionId,
                    ThreadId = route.ThreadId
                }, cancellationToken).ConfigureAwait(false);
                await publisher.CommitAndPublishAsync(
                    route,
                    new ThreadExecutionFinishedEvent(
                        executionId,
                        child.ChildAgentId,
                        ThreadExecutionOutcome.Succeeded,
                        DateTimeOffset.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
                var projected = await store.ProjectThreadAsync(
                    route.SessionId,
                    route.ThreadId,
                    ThreadProjectionPurpose.ThreadHistory,
                    CancellationToken.None).ConfigureAwait(false);
                return new SubAgentOperationResult
                {
                    Status = SubAgentOperationStatus.Completed,
                    Child = child.LocalId.Value,
                    InvocationId = invocationId,
                    ThreadExecutionId = executionId,
                    Output = projected?.Messages.LastOrDefault(static message => message.Role == ChatRole.Assistant)?.Text
                };
            }
            catch (Exception exception)
            {
                await publisher.CommitAndPublishAsync(
                    route,
                    new ThreadExecutionFinishedEvent(
                        executionId,
                        child.ChildAgentId,
                        exception is OperationCanceledException ? ThreadExecutionOutcome.Cancelled : ThreadExecutionOutcome.Failed,
                        DateTimeOffset.UtcNow,
                        exception is OperationCanceledException ? null : new ThreadExecutionError(exception.GetType().Name, exception.Message)),
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        return Failure(
            action is "sendMessage" or "cancel" ? "subagent_not_running" : "subagent_unavailable",
            $"The '{action}' control is unavailable because no shared execution controller is configured.",
            child.LocalId.Value);

        static SubAgentOperationResult Failure(string code, string message, string? child = null) => new()
        {
            Status = SubAgentOperationStatus.Unavailable,
            Child = child,
            Error = new SubAgentOperationError(code, message)
        };
    }

    private static async ValueTask<SubAgentLocalId?> RegisterChildAsync(
        SubAgentInvocationRequest request,
        SubAgentInvocationRoute route,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        var context = request.ParentContext;
        var store = context?.GetParentSessionStore();
        if (context?.SessionId is null || context.ThreadId is null || store is null)
            return null;
        var parent = new ThreadKey(context.SessionId, context.ThreadId);
        var registry = new SubAgentChildRegistry(store);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var projection = await registry.ProjectAsync(parent, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var replay = projection.Children.Values.FirstOrDefault(child =>
                string.Equals(child.ParentToolCallId, context.FunctionCallId, StringComparison.Ordinal) &&
                child.CapabilityId == request.CapabilityId);
            if (replay is not null) return replay.LocalId;
            var ordinal = projection.Children.Values.Count(child =>
                string.Equals(child.RoleName, request.Definition.Name, StringComparison.Ordinal)) + 1;
            var localId = new SubAgentLocalId($"{Normalize(request.Definition.Name)}-{ordinal}");
            var child = new SubAgentChildReference
            {
                LocalId = localId,
                RoleName = request.Definition.Name,
                CapabilityId = request.CapabilityId,
                ChildAgentId = request.Definition.AgentId,
                Availability = SubAgentChildAvailability.Available,
                ChildThread = new ThreadKey(route.SessionId, route.ThreadId),
                CreationContext = contextPolicy switch
                {
                    SubAgentContextPolicy.Fork => SubAgentCreationContext.Fork,
                    SubAgentContextPolicy.Fresh => SubAgentCreationContext.Fresh,
                    SubAgentContextPolicy.Isolated => SubAgentCreationContext.Isolated,
                    _ => throw new InvalidOperationException("ModelChoice must resolve before child registration.")
                },
                CreationInvocationId = route.InvocationId,
                ParentToolCallId = context.FunctionCallId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            try
            {
                return (await registry.RegisterAsync(parent, child, cancellationToken).ConfigureAwait(false)).LocalId;
            }
            catch (InvalidOperationException exception) when (
                exception.Message == "subagent_creation_conflict" && attempt < 7) { }
        }
        throw new InvalidOperationException("subagent_creation_conflict");
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
        var contextPolicy = SubAgentContexts.Resolve(subAgent.ContextPolicy, requestedContext: null);
        var route = PlanInvocationRoute(subAgent, functionContext, taskName, contextPolicy);
        return await EnsureInvocationRouteAsync(
            agent,
            subAgent,
            functionContext,
            taskName,
            route,
            contextPolicy,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SubAgentInvocationRoute> EnsureInvocationRouteAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string taskName,
        SubAgentInvocationRoute route,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(subAgent);
        var runId = route.InvocationId;
        var sessionId = await ResolveSessionAsync(agent, subAgent, functionContext, taskName, runId, contextPolicy, cancellationToken)
            .ConfigureAwait(false);
        var threadId = await ResolveThreadAsync(agent, subAgent, functionContext, taskName, sessionId, runId, contextPolicy, cancellationToken)
            .ConfigureAwait(false);

        functionContext?.ResultMetadata.Set("subAgentStatus", "started");
        functionContext?.ResultMetadata.Set("subAgentSessionId", sessionId);
        functionContext?.ResultMetadata.Set("subAgentThreadId", threadId);
        functionContext?.ResultMetadata.Set("subAgentName", subAgent.Name);
        functionContext?.ResultMetadata.Set("subAgentLocalStorageName", taskName);
        functionContext?.ResultMetadata.Set("invocationId", runId);

        return new SubAgentInvocationRoute(sessionId, threadId, runId);
    }

    private static SubAgentInvocationRoute PlanInvocationRoute(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string taskName,
        SubAgentContextPolicy contextPolicy)
    {
        var runId = Guid.NewGuid().ToString("N");
        var sessionId = contextPolicy == SubAgentContextPolicy.Isolated
            ? BuildSessionId(subAgent, taskName, runId)
            : functionContext?.SessionId
                ?? throw new InvalidOperationException("Parent-session subagents require a parent SessionId.");
        var threadId = BuildThreadId(subAgent, taskName, runId);
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
        public ValueTask DisposeAsync() => Agent.DisposeAsync();
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

    private static async Task<int> ResolveMessageCountAsync(
        Agent agent,
        SubAgentInvocationRoute route,
        CancellationToken cancellationToken)
    {
        var store = agent.Config.SessionStore;
        if (store == null)
            return 0;

        var thread = await store.ProjectThreadAsync(
            route.SessionId,
            route.ThreadId,
            ThreadProjectionPurpose.ThreadHistory,
            cancellationToken).ConfigureAwait(false);

        return thread?.Messages.Count ?? 0;
    }

    private static async Task<string> ResolveAssistantTextAfterAsync(
        Agent agent,
        SubAgentInvocationRoute route,
        int initialMessageCount,
        CancellationToken cancellationToken)
    {
        var store = agent.Config.SessionStore;
        if (store == null)
            return string.Empty;

        var thread = await store.ProjectThreadAsync(
            route.SessionId,
            route.ThreadId,
            ThreadProjectionPurpose.ThreadHistory,
            cancellationToken).ConfigureAwait(false);

        return thread?.Messages
            .Skip(initialMessageCount)
            .LastOrDefault(message => message.Role == ChatRole.Assistant)?.Text
            ?? string.Empty;
    }

    private static async Task<string> ResolveSessionAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string taskName,
        string runId,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        if (contextPolicy != SubAgentContextPolicy.Isolated)
        {
            return functionContext?.SessionId
                ?? throw new InvalidOperationException("Parent-session subagents require a parent SessionId.");
        }

        var sessionId = BuildSessionId(subAgent, taskName, runId);
        await agent.CreateSessionAsync(
            sessionId,
            BuildMetadata(subAgent, functionContext, taskName, runId, contextPolicy),
            cancellationToken).ConfigureAwait(false);
        return sessionId;
    }

    private static async Task<string> ResolveThreadAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string taskName,
        string sessionId,
        string runId,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        var metadata = BuildMetadata(subAgent, functionContext, taskName, runId, contextPolicy);

        switch (contextPolicy)
        {
            case SubAgentContextPolicy.Fresh:
            case SubAgentContextPolicy.Isolated:
            {
                var threadId = BuildThreadId(subAgent, taskName, runId);
                await CreateEmptyThreadAsync(agent, sessionId, threadId, metadata, cancellationToken).ConfigureAwait(false);
                return threadId;
            }

            case SubAgentContextPolicy.Fork:
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
                    Compaction = subAgent.ForkCompaction
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
                throw new ArgumentOutOfRangeException(nameof(contextPolicy));
        }
    }

    private static string BuildThreadId(SubAgent subAgent, string taskName, string runId)
    {
        var prefix = $"subagent/{Normalize(subAgent.Name)}";
        return $"{prefix}/{Normalize(taskName)}/{runId[..Math.Min(12, runId.Length)]}";
    }

    private static string BuildSessionId(SubAgent subAgent, string taskName, string runId) =>
        $"subagent/{Normalize(subAgent.Name)}/{Normalize(taskName)}/{runId[..Math.Min(12, runId.Length)]}";

    private static Dictionary<string, object> BuildMetadata(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string taskName,
        string runId,
        SubAgentContextPolicy contextPolicy)
    {
        var metadata = subAgent.Metadata is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(subAgent.Metadata, StringComparer.Ordinal);

        // Runtime-owned routing fields are authoritative and cannot be replaced by
        // application metadata supplied on the reusable subagent definition.
        metadata["kind"] = "subagent";
        metadata["subAgentName"] = subAgent.Name;
        metadata["subAgentLocalStorageName"] = taskName;
        metadata["subAgentSourceKind"] = subAgent.Configuration.GetType().Name;
        metadata["parentSessionId"] = functionContext?.SessionId ?? string.Empty;
        metadata["parentThreadId"] = functionContext?.ThreadId ?? string.Empty;
        metadata["parentToolCallId"] = functionContext?.FunctionCallId ?? string.Empty;
        metadata["invocationId"] = runId;
        metadata["contextPolicy"] = contextPolicy.ToString();
        metadata["visibility"] = "hidden";
        metadata["createdBy"] = "subagent";

        metadata["defaultAgentId"] = subAgent.AgentId;

        return metadata;
    }

    private static string GetCreationStorageName(SubAgentInvocationRequest request)
    {
        var callId = request.ParentContext?.FunctionCallId ?? Guid.NewGuid().ToString("N");
        return $"{Normalize(request.Definition.Name)}-{Normalize(callId)[..Math.Min(12, Normalize(callId).Length)]}";
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
