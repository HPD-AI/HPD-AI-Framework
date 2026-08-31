using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent;

/// <summary>Durable execution-scoped result for an idempotent child continuation.</summary>
/// <param name="ContinuationExecutionId">The deterministic continuation execution identifier.</param>
/// <param name="Output">The exact terminal text result, or <see langword="null"/> when no text was produced.</param>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CONTINUATION_RECEIPT")]
public sealed record SubAgentContinuationReceiptEvent(
    string ContinuationExecutionId,
    string? Output) : AgentEvent
{
    /// <inheritdoc />
    public override string? ThreadExecutionId { get; init; } = ContinuationExecutionId;
}

/// <summary>
/// Runtime services for invoking thread-native subagents.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SubAgentRuntime
{
    private static readonly ConditionalWeakTable<ISessionStore, ConcurrentDictionary<string, SemaphoreSlim>>
        ContinuationAdmissions = new();
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

        if (admission.Creation.Phase == SubAgentCreationPhase.Terminal)
        {
            return new AgentInvocationResult
            {
                Mode = mode,
                Text = admission.Creation.TerminalOutput,
                ToolResult = new SubAgentOperationResult
                {
                    Status = admission.Creation.TerminalStatus ?? SubAgentOperationStatus.Failed,
                    Child = admission.LocalId?.Value,
                    InvocationId = admission.Route.InvocationId,
                    ThreadExecutionId = admission.Creation.ThreadExecutionId,
                    AgentOperationId = admission.Creation.AgentOperationId,
                    Output = admission.Creation.TerminalOutput,
                    Error = admission.Creation.Error
                }
            };
        }

        if (admission.Creation.Phase is SubAgentCreationPhase.InitialExecutionAdmitted or
            SubAgentCreationPhase.ReconciliationRequired)
        {
            return await RecoverAdmittedInvocationAsync(request, admission, mode, cancellationToken)
                .ConfigureAwait(false);
        }

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
                ThreadExecutionId = admission.Creation.ThreadExecutionId,
                Output = result.Text
            }
        };
    }

    private static async Task<AgentInvocationResult> RecoverAdmittedInvocationAsync(
        SubAgentInvocationRequest request,
        AdmittedSubAgentInvocation admission,
        AgentInvocationMode mode,
        CancellationToken cancellationToken)
    {
        var store = request.ParentContext?.GetParentSessionStore()
            ?? throw new InvalidOperationException("subagent_creation_requires_session_store");
        var active = await ThreadExecutionControllerRegistry.For(store)
            .FindActiveAsync(admission.Creation.ChildThread, cancellationToken).ConfigureAwait(false);
        if (active.IsActive && string.Equals(
                active.ThreadExecutionId,
                admission.Creation.ThreadExecutionId,
                StringComparison.Ordinal))
        {
            return new AgentInvocationResult
            {
                Mode = mode,
                ToolResult = new SubAgentOperationResult
                {
                    Status = SubAgentOperationStatus.Running,
                    Child = admission.LocalId?.Value,
                    InvocationId = admission.Route.InvocationId,
                    ThreadExecutionId = admission.Creation.ThreadExecutionId,
                    AgentOperationId = admission.Creation.AgentOperationId
                }
            };
        }

        var error = admission.Creation.Error ?? new SubAgentOperationError(
            "subagent_creation_reconciliation_required",
            "The initial child execution was durably admitted, but no matching live owner or terminal receipt exists. It will not be run again automatically.");
        if (admission.Creation.Phase != SubAgentCreationPhase.ReconciliationRequired)
        {
            await AdvanceCreationAsync(
                admission,
                SubAgentCreationPhase.ReconciliationRequired,
                SubAgentOperationStatus.Failed,
                output: null,
                error: error,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        return new AgentInvocationResult
        {
            Mode = mode,
            ToolResult = new SubAgentOperationResult
            {
                Status = SubAgentOperationStatus.Failed,
                Child = admission.LocalId?.Value,
                InvocationId = admission.Route.InvocationId,
                ThreadExecutionId = admission.Creation.ThreadExecutionId,
                Error = error
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
                },
                operationId: admission.Creation.AgentOperationId).ConfigureAwait(false);

        await PersistBackgroundReceiptAsync(admission, receipt.OperationId).ConfigureAwait(false);

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Background,
            Operation = receipt,
            ToolResult = new SubAgentOperationResult
            {
                Status = SubAgentOperationStatus.Running,
                Child = admission.LocalId?.Value,
                InvocationId = admission.Route.InvocationId,
                ThreadExecutionId = admission.Creation.ThreadExecutionId,
                AgentOperationId = receipt.OperationId
            }
        };
    }

    private static async ValueTask PersistBackgroundReceiptAsync(
        AdmittedSubAgentInvocation admission,
        string operationId)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var latest = await admission.CreationStore.GetSubAgentCreationAsync(
                admission.Creation.Key, CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidOperationException("subagent_creation_not_found");
            if (string.Equals(latest.AgentOperationId, operationId, StringComparison.Ordinal)) return;
            var updated = latest with { AgentOperationId = operationId, Revision = latest.Revision + 1 };
            try
            {
                await admission.CreationStore.WriteSubAgentCreationAsync(
                    updated,
                    new SubAgentCreationWriteCondition(latest.Revision),
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (InvalidOperationException exception) when (
                exception.Message == "subagent_creation_write_conflict" && attempt < 7) { }
        }
        throw new InvalidOperationException("subagent_creation_write_conflict");
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
        await AdvanceCreationPhaseAsync(
            admission,
            SubAgentCreationPhase.InitialExecutionAdmitted,
            cancellationToken).ConfigureAwait(false);
        var threadExecutionId = admission.Creation.ThreadExecutionId;

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
                ThreadExecutionId = threadExecutionId,
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
            if (request.ParentContext is not null)
            {
                await request.ParentContext.PublishAsync(
                    new SubAgentInvocationCompletedEvent(route.InvocationId, text),
                    CancellationToken.None).ConfigureAwait(false);
            }
            MarkCompleted(request.ParentContext, route);
            await AdvanceCreationAsync(
                admission,
                SubAgentCreationPhase.Terminal,
                SubAgentOperationStatus.Completed,
                text,
                error: null,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

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
            if (request.ParentContext is not null)
            {
                AgentEvent terminal = ex is OperationCanceledException
                    ? new SubAgentInvocationCancelledEvent(route.InvocationId, ex.Message)
                    : new SubAgentInvocationFailedEvent(route.InvocationId, ex.GetType().Name, ex.Message);
                await request.ParentContext.PublishAsync(terminal, CancellationToken.None).ConfigureAwait(false);
            }
            MarkFailed(request.ParentContext, route, ex);
            await AdvanceCreationAsync(
                admission,
                SubAgentCreationPhase.Terminal,
                ex is OperationCanceledException
                    ? SubAgentOperationStatus.Cancelled
                    : SubAgentOperationStatus.Failed,
                output: null,
                error: new SubAgentOperationError(
                    ex is OperationCanceledException ? "subagent_cancelled" : "subagent_execution_failed",
                    ex.Message),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
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
        var context = request.ParentContext
            ?? throw new InvalidOperationException("subagent_creation_requires_parent_context");
        var store = context.GetParentSessionStore()
            ?? throw new InvalidOperationException("subagent_creation_requires_session_store");
        if (context.SessionId is null || context.ThreadId is null || string.IsNullOrWhiteSpace(context.FunctionCallId))
            throw new InvalidOperationException("subagent_creation_requires_parent_identity");
        var creationStore = new JournalSubAgentCreationStore(store);
        var key = new SubAgentCreationKey(
            new ThreadKey(context.SessionId, context.ThreadId),
            context.FunctionCallId,
            request.CapabilityId);
        var reservation = await creationStore.TryReserveSubAgentCreationAsync(
            key,
            new SubAgentCreationRequest
            {
                RoleName = definition.Name,
                ChildAgentId = definition.AgentId,
                Context = ToCreationContext(contextPolicy),
                InputFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Input)))
            },
            cancellationToken).ConfigureAwait(false);
        var creation = reservation.Record;
        var plannedRoute = new SubAgentInvocationRoute(
            creation.ChildThread.SessionId,
            creation.ChildThread.ThreadId,
            creation.InvocationId);
        if (creation.Phase >= SubAgentCreationPhase.Registered)
            return new AdmittedSubAgentInvocation(plannedRoute, creation.LocalId, contextPolicy, creationStore, creation);
        await using var runtime = await AcquireRuntimeAsync(
            definition,
            request.ParentContext,
            plannedRoute,
            cancellationToken).ConfigureAwait(false);
        AttachParentCoordinator(runtime.Agent, request.ParentContext);
        var route = plannedRoute;
        if (creation.Phase == SubAgentCreationPhase.Reserved)
        {
            route = await EnsureInvocationRouteAsync(
                runtime.Agent,
                definition,
                request.ParentContext,
                creation.LocalId.Value,
                plannedRoute,
                contextPolicy,
                cancellationToken).ConfigureAwait(false);
            creation = await AdvanceCreationRecordAsync(
                creationStore, creation, SubAgentCreationPhase.ChildCreated, cancellationToken).ConfigureAwait(false);
        }
        if (creation.Phase == SubAgentCreationPhase.ChildCreated)
        {
            var child = new SubAgentChildReference
            {
                LocalId = creation.LocalId,
                RoleName = definition.Name,
                CapabilityId = request.CapabilityId,
                ChildAgentId = definition.AgentId,
                Availability = SubAgentChildAvailability.Available,
                ChildThread = creation.ChildThread,
                CreationContext = creation.Request.Context,
                CreationInvocationId = creation.InvocationId,
                ParentToolCallId = context.FunctionCallId,
                CreatedAt = creation.CreatedAt
            };
            await new SubAgentChildRegistry(store).RegisterAsync(key.Parent, child, cancellationToken)
                .ConfigureAwait(false);
            creation = await AdvanceCreationRecordAsync(
                creationStore, creation, SubAgentCreationPhase.Registered, cancellationToken).ConfigureAwait(false);
        }
        return new AdmittedSubAgentInvocation(route, creation.LocalId, contextPolicy, creationStore, creation);
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

    private sealed record AdmittedSubAgentInvocation(
        SubAgentInvocationRoute Route,
        SubAgentLocalId? LocalId,
        SubAgentContextPolicy ContextPolicy,
        ISubAgentCreationStore CreationStore,
        SubAgentCreationRecord Creation);

    private static SubAgentCreationContext ToCreationContext(SubAgentContextPolicy policy) => policy switch
    {
        SubAgentContextPolicy.Fork => SubAgentCreationContext.Fork,
        SubAgentContextPolicy.Fresh => SubAgentCreationContext.Fresh,
        SubAgentContextPolicy.Isolated => SubAgentCreationContext.Isolated,
        _ => throw new InvalidOperationException("subagent_creation_context_invalid")
    };

    private static async ValueTask<SubAgentCreationRecord> AdvanceCreationRecordAsync(
        ISubAgentCreationStore store,
        SubAgentCreationRecord current,
        SubAgentCreationPhase phase,
        CancellationToken cancellationToken)
    {
        var updated = current with { Phase = phase, Revision = current.Revision + 1 };
        await store.WriteSubAgentCreationAsync(
            updated,
            new SubAgentCreationWriteCondition(current.Revision),
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private static async ValueTask AdvanceCreationAsync(
        AdmittedSubAgentInvocation admission,
        SubAgentCreationPhase phase,
        SubAgentOperationStatus status,
        string? output,
        SubAgentOperationError? error,
        CancellationToken cancellationToken)
    {
        var latest = await admission.CreationStore.GetSubAgentCreationAsync(
            admission.Creation.Key, cancellationToken).ConfigureAwait(false) ?? admission.Creation;
        if (latest.Phase == SubAgentCreationPhase.Terminal) return;
        var updated = latest with
        {
            Phase = phase,
            Revision = latest.Revision + 1,
            TerminalStatus = status,
            TerminalOutput = output,
            Error = error
        };
        await admission.CreationStore.WriteSubAgentCreationAsync(
            updated,
            new SubAgentCreationWriteCondition(latest.Revision),
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask AdvanceCreationPhaseAsync(
        AdmittedSubAgentInvocation admission,
        SubAgentCreationPhase phase,
        CancellationToken cancellationToken)
    {
        var latest = await admission.CreationStore.GetSubAgentCreationAsync(
            admission.Creation.Key, cancellationToken).ConfigureAwait(false) ?? admission.Creation;
        if (latest.Phase >= phase) return;
        await admission.CreationStore.WriteSubAgentCreationAsync(
            latest with { Phase = phase, Revision = latest.Revision + 1 },
            new SubAgentCreationWriteCondition(latest.Revision),
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> CreateBackgroundDescriptorMetadata(
        SubAgent definition,
        string storageName)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invocation.kind"] = "subagent",
            ["invocation.mode"] = "background",
            ["subAgent.name"] = definition.Name,
            ["subAgent.localStorageName"] = storageName,
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

        var controller = ThreadExecutionControllerRegistry.For(store);
        if (string.Equals(action, "wait", StringComparison.Ordinal))
            return await WaitAsync(branch, projection, controller, store, cancellationToken).ConfigureAwait(false);

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
        var childDescriptor = await store.GetThreadAsync(child.ChildThread.Value, cancellationToken).ConfigureAwait(false);
        if (childDescriptor is null ||
            childDescriptor.Kind != ThreadKind.SubAgent ||
            !string.Equals(childDescriptor.DefaultAgent.AgentId, child.ChildAgentId, StringComparison.Ordinal) ||
            !string.Equals(childDescriptor.RuntimeChild?.SubAgentName, child.RoleName, StringComparison.Ordinal) ||
            !string.Equals(childDescriptor.RuntimeChild?.ParentToolCallId, child.ParentToolCallId, StringComparison.Ordinal))
            return Failure("subagent_route_mismatch", "The registered child route failed canonical identity validation.", child.LocalId.Value);
        var ownedByParent =
            string.Equals(childDescriptor.RuntimeChild?.ParentSessionId, projection.Parent.SessionId, StringComparison.Ordinal) &&
            string.Equals(childDescriptor.RuntimeChild?.ParentThreadId, projection.Parent.ThreadId, StringComparison.Ordinal);
        if (!ownedByParent && !await SubAgentControllerAuthority.IsGrantedAsync(
                store,
                child.ChildThread.Value,
                projection.Parent,
                child.LocalId,
                cancellationToken).ConfigureAwait(false))
            return Failure("subagent_controller_grant_required", "This parent has no durable child-keyed controller grant for the shared child.", child.LocalId.Value);

        if (string.Equals(action, "continue", StringComparison.Ordinal))
        {
            var resolver = functionContext.Services?.GetService<IAgentRuntimeResolver>()
                ?? throw new InvalidOperationException("subagent_unavailable: no agent runtime resolver is configured.");
            var route = child.ChildThread.Value;
            var input = branch.GetProperty("input").GetString() ?? string.Empty;
            var continueKey = $"{functionContext.SessionId}|{functionContext.ThreadId}|{functionContext.FunctionCallId}|{child.LocalId.Value}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))}";
            var continueDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(continueKey))).ToLowerInvariant();
            var executionId = $"continue-{continueDigest[..24]}";
            var invocationId = $"continue-{continueDigest[24..48]}";
            var operationId = $"subagent-continue-{continueDigest[..32]}";
            var active = await controller.FindActiveAsync(route, cancellationToken).ConfigureAwait(false);
            if (active.IsActive && !string.Equals(active.ThreadExecutionId, executionId, StringComparison.Ordinal))
                return Failure("subagent_busy", "This child already has an active execution.", child.LocalId.Value);
            var admissionKey = $"{route.SessionId}\u001f{route.ThreadId}\u001f{executionId}";
            var admissions = ContinuationAdmissions.GetValue(
                store, static _ => new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal));
            var candidateAdmission = new SemaphoreSlim(1, 1);
            var admission = admissions.GetOrAdd(admissionKey, candidateAdmission);
            var ownsAdmission = ReferenceEquals(candidateAdmission, admission);
            if (!ownsAdmission) candidateAdmission.Dispose();
            (bool Reserved, ThreadExecutionOutcome? Outcome, SubAgentOperationError? Error, string? Output, bool ReceiptPresent) durableReplay;
            var admissionAcquired = false;
            try
            {
                await admission.WaitAsync(cancellationToken).ConfigureAwait(false);
                admissionAcquired = true;
                durableReplay = await TryReserveExecutionAsync(
                        store, route, executionId, child.ChildAgentId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (ownsAdmission)
                    admissions.TryRemove(admissionKey, out _);
                throw;
            }
            finally
            {
                if (admissionAcquired) admission.Release();
            }
            if (!durableReplay.Reserved)
            {
                if (durableReplay.Outcome is null)
                {
                    if (!ownsAdmission && admissions.ContainsKey(admissionKey))
                    {
                        return new SubAgentOperationResult
                        {
                            Status = SubAgentOperationStatus.Running,
                            Child = child.LocalId.Value,
                            InvocationId = invocationId,
                            ThreadExecutionId = executionId,
                            AgentOperationId = operationId
                        };
                    }
                    if (ownsAdmission)
                        admissions.TryRemove(admissionKey, out _);
                    if (active.IsActive)
                    {
                        return new SubAgentOperationResult
                        {
                            Status = SubAgentOperationStatus.Running,
                            Child = child.LocalId.Value,
                            InvocationId = invocationId,
                            ThreadExecutionId = executionId,
                            AgentOperationId = operationId
                        };
                    }
                    await Task.Yield();
                    active = await controller.FindActiveAsync(route, cancellationToken).ConfigureAwait(false);
                    if (active.IsActive && string.Equals(active.ThreadExecutionId, executionId, StringComparison.Ordinal))
                    {
                        return new SubAgentOperationResult
                        {
                            Status = SubAgentOperationStatus.Running,
                            Child = child.LocalId.Value,
                            InvocationId = invocationId,
                            ThreadExecutionId = executionId,
                            AgentOperationId = operationId
                        };
                    }
                    return new SubAgentOperationResult
                    {
                        Status = SubAgentOperationStatus.Failed,
                        Child = child.LocalId.Value,
                        InvocationId = invocationId,
                        ThreadExecutionId = executionId,
                        AgentOperationId = operationId,
                        Error = new SubAgentOperationError(
                            "subagent_reconciliation_required",
                            "The continuation was durably admitted but has no live owner or terminal receipt; it will not be executed again automatically.")
                    };
                }
                if (ownsAdmission)
                    admissions.TryRemove(admissionKey, out _);
                if (durableReplay.Outcome != ThreadExecutionOutcome.Succeeded)
                {
                    return new SubAgentOperationResult
                    {
                        Status = SubAgentOperationStatus.Failed,
                        Child = child.LocalId.Value,
                        InvocationId = invocationId,
                        ThreadExecutionId = executionId,
                        AgentOperationId = operationId,
                        Error = durableReplay.Error ?? new SubAgentOperationError(
                            "subagent_continue_failed", "The prior continuation did not succeed.")
                    };
                }
                if (!durableReplay.ReceiptPresent)
                {
                    return new SubAgentOperationResult
                    {
                        Status = SubAgentOperationStatus.Failed,
                        Child = child.LocalId.Value,
                        InvocationId = invocationId,
                        ThreadExecutionId = executionId,
                        AgentOperationId = operationId,
                        Error = new SubAgentOperationError(
                            "subagent_reconciliation_required",
                            "The continuation finished before its execution-scoped result receipt committed.")
                    };
                }
                return new SubAgentOperationResult
                {
                    Status = SubAgentOperationStatus.Completed,
                    Child = child.LocalId.Value,
                    InvocationId = invocationId,
                    ThreadExecutionId = executionId,
                    AgentOperationId = operationId,
                    Output = durableReplay.Output
                };
            }
            var requestedMode = functionContext.ResolvedInvocationMode;
            if (requestedMode == AgentInvocationMode.Background)
            {
                try
                {
                    var receipt = await functionContext.StartOperationAsync(
                        $"continue-{child.LocalId.Value}",
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["invocation.kind"] = "subagent_continue",
                            ["subAgent.child"] = child.LocalId.Value,
                            ["subAgent.invocationId"] = invocationId
                        },
                        notification: null,
                        async (_, runtimeToken) =>
                        {
                            try
                            {
                                await ContinueChildAsync(
                                    resolver, store, child, route, input, executionId, runtimeToken).ConfigureAwait(false);
                                return new AgentOperationCompletion("Subagent continuation completed.");
                            }
                            finally
                            {
                                admissions.TryRemove(admissionKey, out SemaphoreSlim? _);
                            }
                        },
                        operationId: operationId).ConfigureAwait(false);
                    return new SubAgentOperationResult
                    {
                        Status = SubAgentOperationStatus.Running,
                        Child = child.LocalId.Value,
                        InvocationId = invocationId,
                        ThreadExecutionId = executionId,
                        AgentOperationId = receipt.OperationId
                    };
                }
                catch
                {
                    admissions.TryRemove(admissionKey, out _);
                    throw;
                }
            }
            try
            {
                var output = await ContinueChildAsync(
                    resolver, store, child, route, input, executionId, cancellationToken).ConfigureAwait(false);
                return new SubAgentOperationResult
                {
                    Status = SubAgentOperationStatus.Completed,
                    Child = child.LocalId.Value,
                    InvocationId = invocationId,
                    ThreadExecutionId = executionId,
                    Output = output
                };
            }
            finally
            {
                admissions.TryRemove(admissionKey, out _);
            }
        }

        if (string.Equals(action, "sendMessage", StringComparison.Ordinal))
        {
            var active = await controller.FindActiveAsync(child.ChildThread.Value, cancellationToken).ConfigureAwait(false);
            if (!active.IsActive || active.ThreadExecutionId is null)
                return Failure("subagent_not_running", "This child has no active execution to steer.", child.LocalId.Value);
            var input = branch.GetProperty("input").GetString() ?? string.Empty;
            var steered = await controller.SteerAsync(
                child.ChildThread.Value,
                active.ThreadExecutionId,
                new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, input)] },
                cancellationToken).ConfigureAwait(false);
            return new SubAgentOperationResult
            {
                Status = steered.Accepted ? SubAgentOperationStatus.Running : SubAgentOperationStatus.Unavailable,
                Child = child.LocalId.Value,
                ThreadExecutionId = steered.ActiveThreadExecutionId,
                Error = steered.Accepted ? null : new SubAgentOperationError("subagent_steer_rejected", steered.Disposition.ToString())
            };
        }

        if (string.Equals(action, "cancel", StringComparison.Ordinal))
        {
            var active = await controller.FindActiveAsync(child.ChildThread.Value, cancellationToken).ConfigureAwait(false);
            if (!active.IsActive || active.ThreadExecutionId is null)
                return Failure("subagent_not_running", "This child has no active execution to cancel.", child.LocalId.Value);
            var reason = branch.TryGetProperty("reason", out var reasonValue) ? reasonValue.GetString() : null;
            var cancelled = await controller.CancelAsync(
                child.ChildThread.Value, active.ThreadExecutionId, reason, cancellationToken).ConfigureAwait(false);
            return new SubAgentOperationResult
            {
                Status = cancelled.Accepted ? SubAgentOperationStatus.Cancelled : SubAgentOperationStatus.Unavailable,
                Child = child.LocalId.Value,
                ThreadExecutionId = cancelled.ActiveThreadExecutionId,
                Error = cancelled.Accepted ? null : new SubAgentOperationError("subagent_cancel_raced", cancelled.Disposition.ToString())
            };
        }

        return Failure(
            "subagent_unavailable",
            $"The '{action}' control is unavailable.",
            child.LocalId.Value);

        static SubAgentOperationResult Failure(string code, string message, string? child = null) => new()
        {
            Status = SubAgentOperationStatus.Unavailable,
            Child = child,
            Error = new SubAgentOperationError(code, message)
        };
    }

    private static async Task<string?> ContinueChildAsync(
        IAgentRuntimeResolver resolver,
        ISessionStore store,
        SubAgentChildReference child,
        ThreadKey route,
        string input,
        string executionId,
        CancellationToken cancellationToken)
    {
        await using var lease = await resolver.GetOrBuildAsync(
            child.ChildAgentId, route.SessionId, route.ThreadId, cancellationToken).ConfigureAwait(false);
        try
        {
            await lease.Agent.RunAsync(new UserMessagesInputEvent
            {
                Messages = [new ChatMessage(ChatRole.User, input)],
                SessionId = route.SessionId,
                ThreadId = route.ThreadId,
                ThreadExecutionId = executionId
            }, cancellationToken).ConfigureAwait(false);
            var output = await ReadExecutionTextAsync(store, route, executionId, CancellationToken.None)
                .ConfigureAwait(false);
            await store.AppendThreadEventsAsync(
                route,
                [new SubAgentContinuationReceiptEvent(executionId, output)],
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return output;
        }
        catch { throw; }
    }

    private static async ValueTask<string?> ReadExecutionTextAsync(
        ISessionStore store,
        ThreadKey route,
        string executionId,
        CancellationToken cancellationToken)
    {
        var head = await store.GetThreadEventHeadAsync(route, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("subagent_child_route_invalid");
        var deltas = new StringBuilder();
        var completed = new StringBuilder();
        string? replacement = null;
        await foreach (var batch in store.ReadThreadEventsAsync(
                           route,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
                           cancellationToken).ConfigureAwait(false))
            foreach (var evt in batch.Events)
                if (evt is TextDeltaEvent delta &&
                    string.Equals(evt.ThreadExecutionId, executionId, StringComparison.Ordinal))
                    deltas.Append(delta.Text);
                else if (evt is ContentAddedEvent { Role: "assistant", Content: TextContent content } &&
                         string.Equals(evt.ThreadExecutionId, executionId, StringComparison.Ordinal))
                    completed.Append(content.Text);
                else if (evt is ThreadMessageReplacedEvent replaced &&
                         replaced.Replacement.Role == ChatRole.Assistant &&
                         string.Equals(evt.ThreadExecutionId, executionId, StringComparison.Ordinal))
                    replacement = replaced.Replacement.Text;
        return replacement ??
            (completed.Length > 0 ? completed.ToString() : null) ??
            (deltas.Length > 0 ? deltas.ToString() : null);
    }

    private static async ValueTask<(bool Reserved, ThreadExecutionOutcome? Outcome, SubAgentOperationError? Error, string? Output, bool ReceiptPresent)>
        TryReserveExecutionAsync(
            ISessionStore store,
            ThreadKey route,
            string executionId,
            string agentId,
            CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var head = await store.GetThreadEventHeadAsync(route, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("subagent_child_route_invalid");
            var started = false;
            ThreadExecutionFinishedEvent? terminal = null;
            string? output = null;
            var receiptPresent = false;
            await foreach (var batch in store.ReadThreadEventsAsync(
                               route,
                               new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
                               cancellationToken).ConfigureAwait(false))
            {
                foreach (var evt in batch.Events)
                {
                    if (evt is ThreadExecutionStartedEvent start &&
                        string.Equals(start.ThreadExecutionId, executionId, StringComparison.Ordinal))
                        started = true;
                    else if (evt is ThreadExecutionFinishedEvent finished &&
                             string.Equals(finished.ThreadExecutionId, executionId, StringComparison.Ordinal))
                        terminal = finished;
                    else if (evt is SubAgentContinuationReceiptEvent receipt &&
                             string.Equals(receipt.ThreadExecutionId, executionId, StringComparison.Ordinal))
                    {
                        output = receipt.Output;
                        receiptPresent = true;
                    }
                }
            }
            if (started)
                return (
                    false,
                    terminal?.Outcome,
                    terminal?.Error is { } error
                        ? new SubAgentOperationError("subagent_continue_failed", error.Message)
                        : null,
                    output,
                    receiptPresent);
            try
            {
                await store.AppendThreadEventsAsync(
                    route,
                    [new ThreadExecutionStartedEvent(executionId, agentId, DateTimeOffset.UtcNow)
                    {
                        SessionId = route.SessionId,
                        ThreadId = route.ThreadId
                    }],
                    new ThreadAppendCondition(head.Cursor),
                    cancellationToken).ConfigureAwait(false);
                return (true, null, null, null, false);
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("subagent_continue_reservation_conflict");
    }

    private static async Task<SubAgentWaitResult> WaitAsync(
        JsonElement branch,
        SubAgentChildRegistryProjection projection,
        IThreadExecutionController controller,
        ISessionStore store,
        CancellationToken cancellationToken)
    {
        var requested = branch.TryGetProperty("children", out var childrenElement)
            ? childrenElement.EnumerateArray().Select(static value => value.GetString()!).ToHashSet(StringComparer.Ordinal)
            : projection.Children.Keys.Select(static key => key.Value).ToHashSet(StringComparer.Ordinal);
        var mode = branch.TryGetProperty("mode", out var modeElement) ? modeElement.GetString() : "all";
        var timeoutSeconds = branch.TryGetProperty("timeoutSeconds", out var timeoutElement)
            ? timeoutElement.GetInt32()
            : 30;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(0, timeoutSeconds)));
        var observations = new List<SubAgentWaitItem>();
        var tasks = new List<Task<SubAgentWaitItem>>();
        foreach (var localId in requested)
        {
            if (!projection.TryGet(new SubAgentLocalId(localId), out var child) || child.ChildThread is not { } route)
            {
                observations.Add(new SubAgentWaitItem(localId, null, "unavailable"));
                continue;
            }
            var snapshot = await ReadLatestExecutionAsync(store, route, cancellationToken).ConfigureAwait(false);
            if (snapshot.ExecutionId is null)
            {
                observations.Add(new SubAgentWaitItem(localId, null, "idle"));
                continue;
            }
            if (snapshot.TerminalStatus is not null)
            {
                observations.Add(new SubAgentWaitItem(localId, snapshot.ExecutionId, snapshot.TerminalStatus));
                continue;
            }
            tasks.Add(ObserveTerminalAsync(
                localId, route, snapshot.ExecutionId, snapshot.StartCursor, controller, timeout.Token));
        }
        try
        {
            if (tasks.Count > 0)
            {
                if (string.Equals(mode, "any", StringComparison.OrdinalIgnoreCase))
                    observations.Add(await Task.WhenAny(tasks).Unwrap().ConfigureAwait(false));
                else
                    observations.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
            }
            return new SubAgentWaitResult(false, observations);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SubAgentWaitResult(true, observations);
        }
    }

    private static async ValueTask<(string? ExecutionId, string? TerminalStatus, ThreadJournalCursor StartCursor)>
        ReadLatestExecutionAsync(ISessionStore store, ThreadKey route, CancellationToken cancellationToken)
    {
        var head = await store.GetThreadEventHeadAsync(route, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("subagent_child_route_invalid");
        string? executionId = null;
        string? terminal = null;
        var start = ThreadJournalCursor.Start(head.Generation);
        await foreach (var batch in store.ReadThreadEventsAsync(
                           route,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
                           cancellationToken).ConfigureAwait(false))
        {
            foreach (var evt in batch.Events)
            {
                if (evt is ThreadExecutionStartedEvent started)
                {
                    executionId = started.ThreadExecutionId;
                    terminal = null;
                    start = new ThreadJournalCursor(batch.Generation, Math.Max(0, evt.ThreadSequenceNumber - 1));
                }
                else if (evt is ThreadExecutionFinishedEvent finished && finished.ThreadExecutionId == executionId)
                {
                    terminal = finished.Outcome switch
                    {
                        ThreadExecutionOutcome.Succeeded => ThreadExecutionStatus.Succeeded,
                        ThreadExecutionOutcome.Cancelled => ThreadExecutionStatus.Cancelled,
                        _ => ThreadExecutionStatus.Failed
                    };
                }
            }
        }
        return (executionId, terminal, start);
    }

    private static async Task<SubAgentWaitItem> ObserveTerminalAsync(
        string localId,
        ThreadKey route,
        string executionId,
        ThreadJournalCursor cursor,
        IThreadExecutionController controller,
        CancellationToken cancellationToken)
    {
        await foreach (var observation in controller.ObserveAsync(route, executionId, cursor, cancellationToken)
            .ConfigureAwait(false))
        {
            if (observation.Status != ThreadExecutionStatus.Active)
                return new SubAgentWaitItem(localId, executionId, observation.Status);
        }
        return new SubAgentWaitItem(localId, executionId, "unavailable");
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
        string storageName,
        CancellationToken cancellationToken)
    {
        var contextPolicy = SubAgentContexts.Resolve(subAgent.ContextPolicy, requestedContext: null);
        var route = PlanInvocationRoute(subAgent, functionContext, storageName, contextPolicy);
        return await EnsureInvocationRouteAsync(
            agent,
            subAgent,
            functionContext,
            storageName,
            route,
            contextPolicy,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SubAgentInvocationRoute> EnsureInvocationRouteAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string storageName,
        SubAgentInvocationRoute route,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(subAgent);
        var runId = route.InvocationId;
        var sessionId = await ResolveSessionAsync(agent, subAgent, functionContext, storageName, runId, contextPolicy, cancellationToken)
            .ConfigureAwait(false);
        var threadId = await ResolveThreadAsync(agent, subAgent, functionContext, storageName, sessionId, runId, contextPolicy, cancellationToken)
            .ConfigureAwait(false);

        functionContext?.ResultMetadata.Set("subAgentStatus", "started");
        functionContext?.ResultMetadata.Set("subAgentSessionId", sessionId);
        functionContext?.ResultMetadata.Set("subAgentThreadId", threadId);
        functionContext?.ResultMetadata.Set("subAgentName", subAgent.Name);
        functionContext?.ResultMetadata.Set("subAgentLocalStorageName", storageName);
        functionContext?.ResultMetadata.Set("invocationId", runId);

        return new SubAgentInvocationRoute(sessionId, threadId, runId);
    }

    private static SubAgentInvocationRoute PlanInvocationRoute(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string storageName,
        SubAgentContextPolicy contextPolicy)
    {
        var runId = Guid.NewGuid().ToString("N");
        var sessionId = contextPolicy == SubAgentContextPolicy.Isolated
            ? BuildSessionId(subAgent, storageName, runId)
            : functionContext?.SessionId
                ?? throw new InvalidOperationException("Parent-session subagents require a parent SessionId.");
        var threadId = BuildThreadId(subAgent, storageName, runId);
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

        if (await store.GetThreadAsync(new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (!string.Equals(existing.DefaultAgent.AgentId, agent.AgentId, StringComparison.Ordinal) ||
                existing.Kind != ThreadKind.SubAgent)
                throw new InvalidOperationException("subagent_exact_route_collision");
            return threadId;
        }

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
        string storageName,
        string runId,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        if (contextPolicy != SubAgentContextPolicy.Isolated)
        {
            return functionContext?.SessionId
                ?? throw new InvalidOperationException("Parent-session subagents require a parent SessionId.");
        }

        var sessionId = BuildSessionId(subAgent, storageName, runId);
        var store = agent.Config.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        if (await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false) is null)
        {
            await agent.CreateSessionAsync(
                sessionId,
                BuildMetadata(subAgent, functionContext, storageName, runId, contextPolicy),
                cancellationToken).ConfigureAwait(false);
        }
        return sessionId;
    }

    private static async Task<string> ResolveThreadAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string storageName,
        string sessionId,
        string runId,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        var metadata = BuildMetadata(subAgent, functionContext, storageName, runId, contextPolicy);
        var exactThreadId = BuildThreadId(subAgent, storageName, runId);
        var exactStore = agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        if (await exactStore.GetThreadAsync(new ThreadKey(sessionId, exactThreadId), cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (!string.Equals(existing.DefaultAgent.AgentId, agent.AgentId, StringComparison.Ordinal) ||
                existing.Kind != ThreadKind.SubAgent)
                throw new InvalidOperationException("subagent_exact_route_collision");
            return exactThreadId;
        }

        switch (contextPolicy)
        {
            case SubAgentContextPolicy.Fresh:
            case SubAgentContextPolicy.Isolated:
            {
                var threadId = BuildThreadId(subAgent, storageName, runId);
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
                var threadId = BuildThreadId(subAgent, storageName, runId);
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

    private static string BuildThreadId(SubAgent subAgent, string storageName, string runId)
    {
        var prefix = $"subagent/{Normalize(subAgent.Name)}";
        return $"{prefix}/{Normalize(storageName)}/{runId[..Math.Min(12, runId.Length)]}";
    }

    private static string BuildSessionId(SubAgent subAgent, string storageName, string runId) =>
        $"subagent/{Normalize(subAgent.Name)}/{Normalize(storageName)}/{runId[..Math.Min(12, runId.Length)]}";

    private static Dictionary<string, object> BuildMetadata(
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string storageName,
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
        metadata["subAgentLocalStorageName"] = storageName;
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
