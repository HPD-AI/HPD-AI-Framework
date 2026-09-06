using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Providers;
using HPD.Agent.Security;
using HPD.Environment.Contracts;
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

/// <summary>Describes admission of a controller-routed child input.</summary>
/// <param name="Disposition">The authoritative input disposition.</param>
/// <param name="ThreadExecutionId">The active or newly reserved child execution identifier.</param>
public sealed record SubAgentContinuationSubmission(
    AgentInputDisposition Disposition,
    string? ThreadExecutionId);

/// <summary>
/// Runtime services for invoking thread-native subagents.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SubAgentRuntime
{
    private static readonly ConditionalWeakTable<ISessionStore, ConcurrentDictionary<string, ContinuationAdmission>>
        ContinuationAdmissions = new();

    private sealed class ContinuationAdmission
    {
        internal TaskCompletionSource Reserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
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
        AttachParentCoordinator(agent, request.ParentContext, admission.Route);

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

            await ExecuteChildAsync(
                agent,
                new ThreadKey(route.SessionId, route.ThreadId),
                admission.Creation.Request.ExecutionPolicy,
                request.ParentContext,
                request.Input,
                threadExecutionId,
                cancellationToken).ConfigureAwait(false);

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
        var existingCreation = await creationStore.GetSubAgentCreationAsync(key, cancellationToken)
            .ConfigureAwait(false);
        var executionPolicy = existingCreation?.Request.ExecutionPolicy
            ?? ResolveExecutionPolicy(definition, context);
        var contextCursor = existingCreation?.Request.ContextSourceCursor;
        if (existingCreation is null && contextPolicy == SubAgentContextPolicy.Handoff)
        {
            var head = await store.GetThreadAsync(key.Parent, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("subagent_parent_missing");
            contextCursor = new ThreadJournalCursor(head.Generation, head.Head);
        }
        var reservation = await creationStore.TryReserveSubAgentCreationAsync(
            key,
            new SubAgentCreationRequest
            {
                RoleName = definition.Name,
                ChildAgentId = definition.AgentId,
                Context = ToCreationContext(contextPolicy),
                ContextSourceCursor = contextCursor,
                InputFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Input))),
                ExecutionPolicy = executionPolicy
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
        AttachParentCoordinator(runtime.Agent, request.ParentContext, plannedRoute);
        if (creation.Phase == SubAgentCreationPhase.Reserved)
        {
            await EnsureInvocationRouteAsync(
                runtime.Agent,
                definition,
                request.ParentContext,
                creation.LocalId.Value,
                creation.ChildThread,
                creation.InvocationId,
                contextPolicy,
                cancellationToken, creation.Request.ContextSourceCursor, creation.Request.ExecutionPolicy.HandoffCompaction).ConfigureAwait(false);
            creation = await AdvanceCreationRecordAsync(
                creationStore, creation, SubAgentCreationPhase.ChildCreated, cancellationToken).ConfigureAwait(false);
        }
        if (creation.Phase == SubAgentCreationPhase.ChildCreated)
        {
            await ValidateCreatedChildRouteAsync(
                store, creation, definition, contextPolicy, cancellationToken).ConfigureAwait(false);
            var child = new SubAgentChildReference
            {
                LocalId = creation.LocalId,
                RoleName = definition.Name,
                CapabilityId = request.CapabilityId,
                ChildAgentId = definition.AgentId,
                ChildThread = creation.ChildThread,
                CreationContext = creation.Request.Context,
                CreationInvocationId = creation.InvocationId,
                ParentToolCallId = context.FunctionCallId,
                ExecutionPolicy = creation.Request.ExecutionPolicy,
                CreatedAt = creation.CreatedAt
            };
            await new SubAgentChildRegistry(store).RegisterAsync(key.Parent, child, cancellationToken)
                .ConfigureAwait(false);
            creation = await AdvanceCreationRecordAsync(
                creationStore, creation, SubAgentCreationPhase.Registered, cancellationToken).ConfigureAwait(false);
        }
        return new AdmittedSubAgentInvocation(plannedRoute, creation.LocalId, contextPolicy, creationStore, creation);
    }

    private static async ValueTask ValidateCreatedChildRouteAsync(
        ISessionStore store,
        SubAgentCreationRecord creation,
        SubAgent definition,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        var descriptor = await store.GetThreadAsync(creation.ChildThread, cancellationToken).ConfigureAwait(false);
        var runtimeChild = descriptor?.RuntimeChild;
        if (descriptor is null)
            throw new InvalidOperationException("subagent_reserved_route_missing");
        if (descriptor.Kind != ThreadKind.SubAgent ||
            !string.Equals(descriptor.DefaultAgent.AgentId, definition.AgentId, StringComparison.Ordinal) ||
            runtimeChild is null ||
            !string.Equals(runtimeChild.ParentSessionId, creation.Key.Parent.SessionId, StringComparison.Ordinal) ||
            !string.Equals(runtimeChild.ParentThreadId, creation.Key.Parent.ThreadId, StringComparison.Ordinal) ||
            !string.Equals(runtimeChild.SubAgentName, definition.Name, StringComparison.Ordinal) ||
            !string.Equals(runtimeChild.ParentToolCallId, creation.Key.ParentToolCallId, StringComparison.Ordinal) ||
            !string.Equals(runtimeChild.InvocationId, creation.InvocationId, StringComparison.Ordinal) ||
            !string.Equals(runtimeChild.ContextPolicy, contextPolicy.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("subagent_exact_route_collision");
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
        SubAgentContextPolicy.Handoff => SubAgentCreationContext.Handoff,
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
            return new SubAgentListResult(projection.Entries.Values
                .OrderBy(static entry => entry.LocalId.Value, StringComparer.Ordinal)
                .Select(static entry => entry switch
                {
                    SubAgentAvailableChild available => new SubAgentListItem(
                        available.LocalId.Value, available.RoleName, available.Availability,
                        available.Child.CreatedAt, null),
                    SubAgentChildTombstone tombstone => new SubAgentListItem(
                        tombstone.LocalId.Value, tombstone.RoleName, tombstone.Availability,
                        tombstone.CreatedAt, tombstone.Reason),
                    _ => throw new InvalidOperationException("subagent_registry_entry_invalid")
                })
                .ToArray());
        }

        var controller = ThreadExecutionControllerRegistry.For(store);
        if (string.Equals(action, "wait", StringComparison.Ordinal))
            return await WaitAsync(branch, projection, controller, store, cancellationToken).ConfigureAwait(false);

        var localValue = branch.TryGetProperty("child", out var childProperty)
            ? childProperty.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(localValue) ||
            !projection.Entries.TryGetValue(new SubAgentLocalId(localValue), out var entry))
            return Failure("subagent_unknown", "This child is not registered under the current parent. Use list to inspect available children.");
        if (entry is SubAgentChildTombstone tombstone)
            return Failure(
                tombstone.Availability == SubAgentChildAvailability.Detached
                    ? "subagent_detached_by_fork"
                    : "subagent_unavailable",
                tombstone.Reason,
                tombstone.LocalId.Value);
        var child = ((SubAgentAvailableChild)entry).Child;
        var childDescriptor = await store.GetThreadAsync(child.ChildThread, cancellationToken).ConfigureAwait(false);
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
                child.ChildThread,
                projection.Parent,
                child.LocalId,
                cancellationToken).ConfigureAwait(false))
            return Failure("subagent_controller_grant_required", "This parent has no durable child-keyed controller grant for the shared child.", child.LocalId.Value);

        if (string.Equals(action, "continue", StringComparison.Ordinal))
        {
            var resolver = functionContext.Services?.GetService<IAgentRuntimeResolver>()
                ?? throw new InvalidOperationException("subagent_unavailable: no agent runtime resolver is configured.");
            var route = child.ChildThread;
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
                store, static _ => new ConcurrentDictionary<string, ContinuationAdmission>(StringComparer.Ordinal));
            var candidateAdmission = new ContinuationAdmission();
            var admission = admissions.GetOrAdd(admissionKey, candidateAdmission);
            var ownsAdmission = ReferenceEquals(candidateAdmission, admission);
            (bool Reserved, ThreadExecutionOutcome? Outcome, SubAgentOperationError? Error, string? Output, bool ReceiptPresent) durableReplay;
            try
            {
                if (!ownsAdmission)
                    await admission.Reserved.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                durableReplay = await TryReserveExecutionAsync(
                        store, route, executionId, child.ChildAgentId, cancellationToken)
                    .ConfigureAwait(false);
                if (ownsAdmission)
                    admission.Reserved.TrySetResult();
            }
            catch (Exception exception)
            {
                if (ownsAdmission)
                {
                    admission.Reserved.TrySetException(exception);
                    admissions.TryRemove(admissionKey, out _);
                }
                throw;
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
                        notification: new AgentOperationNotificationPolicy(),
                        async (_, runtimeToken) =>
                        {
                            try
                            {
                                await ContinueChildAsync(
                                    resolver, store, child, route, input, executionId,
                                    functionContext, runtimeToken).ConfigureAwait(false);
                                return new AgentOperationCompletion("Subagent continuation completed.");
                            }
                            finally
                            {
                                admissions.TryRemove(admissionKey, out ContinuationAdmission? _);
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
                    resolver, store, child, route, input, executionId,
                    functionContext, cancellationToken).ConfigureAwait(false);
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
            var active = await controller.FindActiveAsync(child.ChildThread, cancellationToken).ConfigureAwait(false);
            if (!active.IsActive || active.ThreadExecutionId is null)
                return Failure("subagent_not_running", "This child has no active execution to steer.", child.LocalId.Value);
            var input = branch.GetProperty("input").GetString() ?? string.Empty;
            var steered = await controller.SteerAsync(
                child.ChildThread,
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
            var active = await controller.FindActiveAsync(child.ChildThread, cancellationToken).ConfigureAwait(false);
            if (!active.IsActive || active.ThreadExecutionId is null)
                return Failure("subagent_not_running", "This child has no active execution to cancel.", child.LocalId.Value);
            var reason = branch.TryGetProperty("reason", out var reasonValue) ? reasonValue.GetString() : null;
            var cancelled = await controller.CancelAsync(
                child.ChildThread, active.ThreadExecutionId, reason, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Submits an ordinary input to an existing child through its owning parent's durable registry entry.
    /// </summary>
    /// <remarks>
    /// The claimed child route is validated against durable registry and thread metadata. The child's
    /// admitted chat client and descendant propagation policy remain authoritative; callers cannot replace
    /// either selection while continuing the child.
    /// </remarks>
    public static async Task<SubAgentContinuationSubmission> SubmitControlledInputAsync(
        ISessionStore store,
        IAgentRuntimeResolver resolver,
        ThreadKey controllerThread,
        SubAgentLocalId localId,
        string childAgentId,
        ThreadKey childThread,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(childAgentId);

        var projection = await new SubAgentChildRegistry(store)
            .ProjectAsync(controllerThread, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!projection.Entries.TryGetValue(localId, out var entry) || entry is not SubAgentAvailableChild available)
            throw new InvalidOperationException("subagent_unknown");

        var child = available.Child;
        if (!string.Equals(child.ChildAgentId, childAgentId, StringComparison.Ordinal) ||
            child.ChildThread != childThread)
            throw new InvalidOperationException("subagent_route_mismatch");

        var descriptor = await store.GetThreadAsync(childThread, cancellationToken).ConfigureAwait(false);
        if (descriptor is null || descriptor.Kind != ThreadKind.SubAgent ||
            !string.Equals(descriptor.DefaultAgent.AgentId, child.ChildAgentId, StringComparison.Ordinal) ||
            !string.Equals(descriptor.RuntimeChild?.SubAgentName, child.RoleName, StringComparison.Ordinal) ||
            !string.Equals(descriptor.RuntimeChild?.ParentToolCallId, child.ParentToolCallId, StringComparison.Ordinal))
            throw new InvalidOperationException("subagent_route_mismatch");

        var ownedByController =
            string.Equals(descriptor.RuntimeChild?.ParentSessionId, controllerThread.SessionId, StringComparison.Ordinal) &&
            string.Equals(descriptor.RuntimeChild?.ParentThreadId, controllerThread.ThreadId, StringComparison.Ordinal);
        if (!ownedByController && !await SubAgentControllerAuthority.IsGrantedAsync(
                store, childThread, controllerThread, localId, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("subagent_controller_grant_required");

        var controller = ThreadExecutionControllerRegistry.For(store);
        var active = await controller.FindActiveAsync(childThread, cancellationToken).ConfigureAwait(false);
        if (input is UserMessagesInputEvent { Delivery: AgentInputDelivery.Steer } steering)
        {
            if (!active.IsActive || active.ThreadExecutionId is null)
                return new SubAgentContinuationSubmission(AgentInputDisposition.NoActiveExecution, null);
            var steered = await controller.SteerAsync(
                childThread,
                active.ThreadExecutionId,
                steering with { ThreadExecutionId = active.ThreadExecutionId },
                cancellationToken).ConfigureAwait(false);
            return new SubAgentContinuationSubmission(steered.Disposition, steered.ActiveThreadExecutionId);
        }

        if (active.IsActive)
            return new SubAgentContinuationSubmission(
                AgentInputDisposition.ActiveExecutionMismatch,
                active.ThreadExecutionId);

        ValidateContinuationInput(input, child.ExecutionPolicy);
        var executionId = input.ThreadExecutionId ?? Guid.NewGuid().ToString("N");
        var reservation = await TryReserveExecutionAsync(
            store, childThread, executionId, childAgentId, cancellationToken).ConfigureAwait(false);
        if (!reservation.Reserved)
            return new SubAgentContinuationSubmission(
                AgentInputDisposition.ActiveExecutionMismatch,
                executionId);

        _ = RunControlledContinuationAsync(
            resolver, store, controllerThread, child, input, executionId, CancellationToken.None);
        return new SubAgentContinuationSubmission(AgentInputDisposition.Queued, executionId);
    }

    private static async Task RunControlledContinuationAsync(
        IAgentRuntimeResolver resolver,
        ISessionStore store,
        ThreadKey parent,
        SubAgentChildReference child,
        AgentInputEvent input,
        string executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendThreadEventAsync(parent.SessionId, parent.ThreadId,
                ContinuationStarted(child, executionId, input.ClientInputId ?? executionId, AgentInvocationMode.Background),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await using var lease = await resolver.GetOrBuildAsync(
                child.ChildAgentId,
                child.ChildThread.SessionId,
                child.ChildThread.ThreadId,
                cancellationToken).ConfigureAwait(false);
            await ExecuteChildInputAsync(
                lease.Agent,
                child.ChildThread,
                child.ExecutionPolicy,
                input,
                executionId,
                store,
                cancellationToken).ConfigureAwait(false);
            await store.AppendThreadEventAsync(parent.SessionId, parent.ThreadId,
                new SubAgentInvocationCompletedEvent(executionId, null),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await store.AppendThreadEventAsync(parent.SessionId, parent.ThreadId,
                ContinuationFailed(executionId, exception), cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void ValidateContinuationInput(AgentInputEvent input, SubAgentExecutionPolicy policy)
    {
        policy.Validate();
        if (input.RunConfig is { } runConfig)
            _ = policy.ApplyLockedSelections(runConfig.Clients);
    }

    private static async Task<string?> ContinueChildAsync(
        IAgentRuntimeResolver resolver,
        ISessionStore store,
        SubAgentChildReference child,
        ThreadKey route,
        string input,
        string executionId,
        FunctionExecutionContext controllerContext,
        CancellationToken cancellationToken)
    {
        await using var lease = await resolver.GetOrBuildAsync(
            child.ChildAgentId, route.SessionId, route.ThreadId, cancellationToken).ConfigureAwait(false);
        await controllerContext.PublishAsync(ContinuationStarted(child, executionId,
            controllerContext.FunctionCallId, controllerContext.ResolvedInvocationMode), cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteChildAsync(
                lease.Agent,
                route,
                child.ExecutionPolicy,
                controllerContext,
                input,
                executionId,
                cancellationToken).ConfigureAwait(false);
            var output = await ReadExecutionTextAsync(store, route, executionId, CancellationToken.None)
                .ConfigureAwait(false);
            await store.AppendThreadEventsAsync(
                route,
                [new SubAgentContinuationReceiptEvent(executionId, output)],
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            await controllerContext.PublishAsync(new SubAgentInvocationCompletedEvent(executionId, output),
                CancellationToken.None).ConfigureAwait(false);
            return output;
        }
        catch (Exception exception)
        {
            await controllerContext.PublishAsync(ContinuationFailed(executionId, exception),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static SubAgentInvocationStartedEvent ContinuationStarted(
        SubAgentChildReference child, string executionId, string parentCallId, AgentInvocationMode mode)
        => new(executionId, parentCallId, child.ChildAgentId, child.ChildThread.SessionId,
            child.ChildThread.ThreadId, child.RoleName, child.CreationContext switch
            {
                SubAgentCreationContext.Handoff => SubAgentContextPolicy.Handoff,
                SubAgentCreationContext.Fresh => SubAgentContextPolicy.Fresh,
                SubAgentCreationContext.Isolated => SubAgentContextPolicy.Isolated,
                _ => throw new InvalidOperationException("subagent_context_invalid")
            }, mode);

    private static AgentEvent ContinuationFailed(string executionId, Exception exception)
        => exception is OperationCanceledException
            ? new SubAgentInvocationCancelledEvent(executionId, exception.Message)
            : new SubAgentInvocationFailedEvent(executionId, exception.GetType().Name, exception.Message);

    private static async ValueTask ExecuteChildAsync(
        Agent childAgent,
        ThreadKey childThread,
        SubAgentExecutionPolicy policy,
        FunctionExecutionContext? controllingContext,
        string input,
        string threadExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(childAgent);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        var runConfig = AgentRunConfigSnapshot.Capture(policy.InitialRunConfig, childAgent.ProviderComposition)
            ?? new AgentRunConfig();
        runConfig.Compaction ??= new CompactionRunPolicy();
        SubAgentCompactionConfiguration.Validate(runConfig.Compaction.Automatic?.Compaction);
        runConfig.Clients = policy.ApplyLockedSelections(runConfig.Clients);
        runConfig.Security = policy.Authority;
        var childInput = new UserMessagesInputEvent
        {
            Messages = [new ChatMessage(ChatRole.User, input)],
            SessionId = childThread.SessionId,
            ThreadId = childThread.ThreadId,
            AgentId = childAgent.AgentId,
            ThreadExecutionId = threadExecutionId,
            RunConfig = runConfig,
            SubAgentRunConfig = CreateDescendantRunConfig(policy)
        };
        var reservation = new CoordinatorWorkReservation(
            childAgent.AgentId, childThread.SessionId, childThread.ThreadId, threadExecutionId);
        var executionStore = controllingContext?.GetParentSessionStore() ?? childAgent.Config.SessionStore
            ?? throw new InvalidOperationException("subagent_unavailable: no durable child session store is configured.");
        reservation.BindPromotion(
            static _ => ValueTask.CompletedTask,
            (outcome, error, finishCancellationToken) => FinishReservedExecutionAsync(
                executionStore, childThread, threadExecutionId, childAgent.AgentId,
                outcome, error, finishCancellationToken));
        await childAgent.RunAsync(
            childAgent.AuthorizeCoordinatorAssignedWork(childInput, reservation),
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ExecuteChildInputAsync(
        Agent childAgent,
        ThreadKey childThread,
        SubAgentExecutionPolicy policy,
        AgentInputEvent input,
        string threadExecutionId,
        ISessionStore store,
        CancellationToken cancellationToken)
    {
        var runConfig = AgentRunConfigSnapshot.Capture(input.RunConfig, childAgent.ProviderComposition)
            ?? new AgentRunConfig();
        runConfig.Compaction ??= AgentRunConfigSnapshot.Capture(policy.InitialRunConfig, childAgent.ProviderComposition)?.Compaction
            ?? new CompactionRunPolicy();
        SubAgentCompactionConfiguration.Validate(runConfig.Compaction.Automatic?.Compaction);
        runConfig.Clients = policy.ApplyLockedSelections(runConfig.Clients);
        runConfig.Security = IntersectAuthority(policy.Authority, runConfig.Security);
        var childInput = input with
        {
            SessionId = childThread.SessionId,
            ThreadId = childThread.ThreadId,
            AgentId = childAgent.AgentId,
            ThreadExecutionId = threadExecutionId,
            RunConfig = runConfig,
            SubAgentRunConfig = ResolveContinuationDescendantRunConfig(
                AgentRunConfigSnapshot.Capture(input.SubAgentRunConfig, childAgent.ProviderComposition), policy)
        };
        var reservation = new CoordinatorWorkReservation(
            childAgent.AgentId, childThread.SessionId, childThread.ThreadId, threadExecutionId);
        reservation.BindPromotion(
            static _ => ValueTask.CompletedTask,
            (outcome, error, finishCancellationToken) => FinishReservedExecutionAsync(
                store, childThread, threadExecutionId, childAgent.AgentId,
                outcome, error, finishCancellationToken));
        await childAgent.RunAsync(
            childAgent.AuthorizeCoordinatorAssignedWork(childInput, reservation),
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask FinishReservedExecutionAsync(
        ISessionStore store,
        ThreadKey route,
        string executionId,
        string agentId,
        ThreadExecutionOutcome outcome,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var head = await store.GetThreadEventHeadAsync(route, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("subagent_child_route_invalid");
            var error = outcome == ThreadExecutionOutcome.Failed
                ? new ThreadExecutionError(
                    exception?.GetType().Name ?? "SubAgentExecutionFailed",
                    exception?.Message ?? "Subagent execution failed.")
                : null;
            try
            {
                await store.AppendThreadEventsAsync(
                    route,
                    [new ThreadExecutionFinishedEvent(
                        executionId, agentId, outcome, DateTimeOffset.UtcNow, error)],
                    new ThreadAppendCondition(head.Cursor),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("subagent_execution_finish_conflict");
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
            : projection.Entries.Keys.Select(static key => key.Value).ToHashSet(StringComparer.Ordinal);
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
            if (!projection.TryGetAvailable(new SubAgentLocalId(localId), out var child))
            {
                observations.Add(new SubAgentWaitItem(localId, null, "unavailable"));
                continue;
            }
            var route = child.ChildThread;
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
        await EnsureInvocationRouteAsync(
            agent,
            subAgent,
            functionContext,
            storageName,
            new ThreadKey(route.SessionId, route.ThreadId),
            route.InvocationId,
            contextPolicy,
            cancellationToken).ConfigureAwait(false);
        return route;
    }

    private static async Task EnsureInvocationRouteAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string storageName,
        ThreadKey route,
        string invocationId,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken,
        ThreadJournalCursor? sourceCursor = null,
        CompactionSpecification? handoffCompaction = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(subAgent);
        await EnsureSessionAsync(
            agent, subAgent, functionContext, storageName, route.SessionId, invocationId,
            contextPolicy, cancellationToken).ConfigureAwait(false);
        await EnsureThreadAsync(
            agent, subAgent, functionContext, storageName, route, invocationId,
            contextPolicy, cancellationToken, sourceCursor, handoffCompaction).ConfigureAwait(false);

        functionContext?.ResultMetadata.Set("subAgentStatus", "started");
        functionContext?.ResultMetadata.Set("subAgentSessionId", route.SessionId);
        functionContext?.ResultMetadata.Set("subAgentThreadId", route.ThreadId);
        functionContext?.ResultMetadata.Set("subAgentName", subAgent.Name);
        functionContext?.ResultMetadata.Set("subAgentLocalStorageName", storageName);
        functionContext?.ResultMetadata.Set("invocationId", invocationId);
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
        CancellationToken cancellationToken,
        SubAgentContextReceivedEvent? handoff = null)
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
        if (handoff is null)
            await store.SaveInitialThreadAsync(sessionId, thread, cancellationToken).ConfigureAwait(false);
        else
            await store.AppendThreadEventsAsync(new ThreadKey(sessionId, threadId),
                [ThreadEventFactory.ThreadCreated(thread), handoff with { SessionId = sessionId, ThreadId = threadId }],
                new ThreadAppendCondition(ThreadJournalCursor.Start(1)), cancellationToken).ConfigureAwait(false);
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
        FunctionExecutionContext? functionContext,
        SubAgentInvocationRoute route)
    {
        var parentCoordinator = functionContext?.GetParentEventCoordinator();
        if (parentCoordinator != null)
        {
            AgentEventRoutes.AttachCoordinator(agent.EventCoordinator, parentCoordinator);
            agent.EventCoordinator.SetParent(parentCoordinator);
        }
        if (functionContext?.SessionId is { Length: > 0 } parentSessionId &&
            functionContext.ThreadId is { Length: > 0 } parentThreadId)
        {
            AgentEventRoutes.RegisterChild(
                parentCoordinator ?? agent.EventCoordinator,
                new ThreadKey(route.SessionId, route.ThreadId),
                new ThreadKey(parentSessionId, parentThreadId));
        }
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

    private static async Task EnsureSessionAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string storageName,
        string sessionId,
        string invocationId,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken)
    {
        if (contextPolicy != SubAgentContextPolicy.Isolated)
        {
            var parentSessionId = functionContext?.SessionId
                ?? throw new InvalidOperationException("Parent-session subagents require a parent SessionId.");
            if (!string.Equals(sessionId, parentSessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("subagent_reserved_route_invalid");
            return;
        }

        var store = agent.Config.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        if (await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false) is null)
        {
            await agent.CreateSessionAsync(
                sessionId,
                BuildMetadata(subAgent, functionContext, storageName, invocationId, contextPolicy),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureThreadAsync(
        Agent agent,
        SubAgent subAgent,
        FunctionExecutionContext? functionContext,
        string storageName,
        ThreadKey route,
        string invocationId,
        SubAgentContextPolicy contextPolicy,
        CancellationToken cancellationToken,
        ThreadJournalCursor? sourceCursor = null,
        CompactionSpecification? handoffCompaction = null)
    {
        var metadata = BuildMetadata(subAgent, functionContext, storageName, invocationId, contextPolicy);
        var exactStore = agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        if (await exactStore.GetThreadAsync(route, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (!string.Equals(existing.DefaultAgent.AgentId, agent.AgentId, StringComparison.Ordinal) ||
                existing.Kind != ThreadKind.SubAgent)
                throw new InvalidOperationException("subagent_exact_route_collision");
            return;
        }

        switch (contextPolicy)
        {
            case SubAgentContextPolicy.Fresh:
            case SubAgentContextPolicy.Isolated:
            {
                await CreateEmptyThreadAsync(
                    agent, route.SessionId, route.ThreadId, metadata, cancellationToken).ConfigureAwait(false);
                return;
            }

            case SubAgentContextPolicy.Handoff:
            {
                var parent = new ThreadKey(
                    functionContext?.SessionId ?? throw new InvalidOperationException("Parent session is required for context handoff."),
                    functionContext.ThreadId ?? throw new InvalidOperationException("Parent thread is required for context handoff."));
                var handoff = await agent.PrepareSubAgentContextAsync(
                    parent, handoffCompaction, cancellationToken, sourceCursor).ConfigureAwait(false);
                await CreateEmptyThreadAsync(agent, route.SessionId, route.ThreadId, metadata,
                    cancellationToken, handoff).ConfigureAwait(false);
                return;
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

    private static SubAgentExecutionPolicy ResolveExecutionPolicy(
        SubAgent definition,
        FunctionExecutionContext context)
    {
        var childRun = context.SubAgentRunConfig;
        var childConfig = (definition.Configuration as SuppliedAgentConfiguration)?.Config;
        var lockedClients = new AgentClientsConfig
        {
            Transport = childRun?.Clients.Transport is { } explicitTransport and not AgentModelTransportMode.Auto
                ? explicitTransport
                : childConfig?.Clients.Transport is { } childTransport and not AgentModelTransportMode.Auto
                    ? childTransport
                    : context.RunConfig?.Clients.Transport is { } runTransport and not AgentModelTransportMode.Auto
                        ? runTransport
                        : context.ParentConfig?.Clients.Transport ?? AgentModelTransportMode.Auto
        };
        var sources = new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource>();
        foreach (var family in Enum.GetValues<ProviderClientFamily>())
        {
            var explicitSelection = childRun?.Clients.GetFamilyConfig(family);
            var childSelection = childConfig?.Clients.GetFamilyConfig(family);
            var controllerSelection = family == ProviderClientFamily.Chat
                ? context.GetEffectiveChatClientHandle()?.ResolvedConfig
                : context.ClientSet?.GetResolvedConfig(family);
            var selected = explicitSelection ?? childSelection ?? controllerSelection;
            if (selected is null)
                continue;
            if (SubAgentExecutionPolicy.HasRuntimeOverride(selected) ||
                SubAgentExecutionPolicy.HasProviderPayload(selected))
                throw new InvalidOperationException("subagent_client_selection_not_portable");
            lockedClients.SetFamilyConfig(family, ProviderClientConfigSnapshot.Clone(selected));
            sources[family] = explicitSelection is not null
                ? SubAgentClientSelectionSource.InputSubAgentRun
                : childSelection is not null
                    ? SubAgentClientSelectionSource.ChildAgentConfig
                    : SubAgentClientSelectionSource.ControllerResolved;
        }
        if (lockedClients.Chat is null)
            throw new InvalidOperationException("subagent_client_selection_not_portable");
        var initialRun = childRun;
        var authority = IntersectAuthority(
            context.RunConfig?.Security ?? new AgentSecurityRunConfig(),
            childRun?.Security ?? new AgentSecurityRunConfig());
        if (initialRun is not null)
            initialRun.Security = authority;
        var policy = SubAgentExecutionPolicy.Create(
            initialRun,
            lockedClients,
            sources,
            authority,
            ResolvePropagation(childRun, childRun?.Clients.Chat is not null),
            childRun?.DescendantDefaults, childRun?.HandoffCompaction);
        policy.Validate();
        return policy;
    }

    private static AgentSecurityRunConfig IntersectAuthority(
        AgentSecurityRunConfig controller,
        AgentSecurityRunConfig requested)
    {
        if (IsDefaultSecurity(requested))
            return controller with
            {
                PermissionOverrides = controller.PermissionOverrides?.Select(static value => value with
                {
                    Selector = value.Selector with { }
                }).ToArray(),
                Sandbox = controller.Sandbox with
                {
                    Capabilities = controller.Sandbox.Capabilities with
                    {
                        Filesystem = controller.Sandbox.Capabilities.Filesystem
                            .Select(static value => value with { }).ToArray()
                    }
                }
            };
        var controllerPermissions = (controller.PermissionOverrides ?? [])
            .GroupBy(static value => (value.Selector.FunctionName, value.Selector.Action, value.Selector.Authority))
            .ToDictionary(static group => group.Key, static group => group.Any(static value => value.RequiresPermission));
        var permissions = (controller.PermissionOverrides ?? [])
            .Concat(requested.PermissionOverrides ?? [])
            .GroupBy(static value => (value.Selector.FunctionName, value.Selector.Action, value.Selector.Authority))
            .Select(group => new PermissionOverride(
                group.First().Selector with { },
                group.Any(static value => value.RequiresPermission) ||
                !controllerPermissions.TryGetValue(group.Key, out var controllerRequiresPermission) ||
                controllerRequiresPermission))
            .ToArray();
        var requestedPaths = requested.Sandbox.Capabilities.Filesystem
            .Select(static value => (value.Path, value.Access))
            .ToHashSet();
        var filesystem = controller.Sandbox.Capabilities.Filesystem
            .Where(value => requestedPaths.Contains((value.Path, value.Access)))
            .Select(static value => value with { })
            .ToArray();
        var controllerInteractive = controller.Sandbox.Capabilities.Interactive;
        var requestedInteractive = requested.Sandbox.Capabilities.Interactive;
        return new AgentSecurityRunConfig
        {
            Approval = controller.Approval == AgentApprovalPolicy.ReviewProtectedActions ||
                       requested.Approval == AgentApprovalPolicy.ReviewProtectedActions
                ? AgentApprovalPolicy.ReviewProtectedActions
                : AgentApprovalPolicy.AutoApprove,
            PermissionOverrides = permissions.Length == 0 ? null : permissions,
            Sandbox = new AgentSandboxRunConfig
            {
                Mode = controller.Sandbox.Mode == AgentSandboxPolicy.Enforced ||
                       requested.Sandbox.Mode == AgentSandboxPolicy.Enforced
                    ? AgentSandboxPolicy.Enforced
                    : AgentSandboxPolicy.Disabled,
                Escape = controller.Sandbox.Escape == AgentSandboxEscapePolicy.Deny ||
                         requested.Sandbox.Escape == AgentSandboxEscapePolicy.Deny
                    ? AgentSandboxEscapePolicy.Deny
                    : AgentSandboxEscapePolicy.Ask,
                Capabilities = new AgentSandboxConfiguration
                {
                    Filesystem = filesystem,
                    Network = NetworkEgressPolicy.Blocked,
                    Interactive = new ProcessInteractivePolicy
                    {
                        AllowPty = controllerInteractive.AllowPty && requestedInteractive.AllowPty,
                        AllowStdin = controllerInteractive.AllowStdin && requestedInteractive.AllowStdin,
                        AllowLocalBinding = controllerInteractive.AllowLocalBinding && requestedInteractive.AllowLocalBinding,
                        AllowedMachLookups = controllerInteractive.AllowedMachLookups
                            .Intersect(requestedInteractive.AllowedMachLookups, StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                            .ToArray()
                    }
                }
            }
        };
    }

    private static bool IsDefaultSecurity(AgentSecurityRunConfig value) =>
        value.Approval == AgentApprovalPolicy.ReviewProtectedActions &&
        value.PermissionOverrides is null or { Count: 0 } &&
        value.Sandbox.Mode == AgentSandboxPolicy.Enforced &&
        value.Sandbox.Escape == AgentSandboxEscapePolicy.Ask &&
        value.Sandbox.Capabilities.Filesystem.Count == 0 &&
        value.Sandbox.Capabilities.Network.Mode == NetworkEgressMode.Blocked &&
        !value.Sandbox.Capabilities.Interactive.AllowPty &&
        value.Sandbox.Capabilities.Interactive.AllowStdin &&
        !value.Sandbox.Capabilities.Interactive.AllowLocalBinding &&
        value.Sandbox.Capabilities.Interactive.AllowedMachLookups.Count == 0;

    private static SubAgentClientPropagationState ResolvePropagation(
        SubAgentRunConfig? runConfig,
        bool hasExplicitChat)
    {
        if (!hasExplicitChat && runConfig?.ClientPropagation is not null and not DirectSubAgentClientPropagation)
            throw new InvalidOperationException("subagent_client_propagation_requires_explicit_chat");
        return runConfig?.ClientPropagation switch
        {
            BoundedSubAgentClientPropagation bounded when bounded.Depth > 1 =>
                new RemainingSubAgentClientPropagation(bounded.Depth - 1),
            UnboundedSubAgentClientPropagation => new UnboundedRemainingSubAgentClientPropagation(),
            _ => new NoSubAgentClientPropagation()
        };
    }

    internal static SubAgentRunConfig? ResolveContinuationDescendantRunConfig(
        SubAgentRunConfig? supplied, SubAgentExecutionPolicy policy)
    {
        var admitted = CreateDescendantRunConfig(policy);
        if (supplied is null) return admitted;
        if (supplied.Clients.Chat is null && admitted?.Clients.Chat is { } chat)
        {
            supplied.Clients.Chat = chat;
            supplied.ClientPropagation = admitted.ClientPropagation;
        }
        if (supplied.DescendantDefaults is null && admitted is not null)
        {
            supplied.Compaction ??= admitted.Compaction;
            supplied.HandoffCompaction ??= admitted.HandoffCompaction;
            supplied.DescendantDefaults = admitted.DescendantDefaults;
        }
        return supplied;
    }

    internal static SubAgentRunConfig? CreateDescendantRunConfig(SubAgentExecutionPolicy policy)
    {
        var result = policy.DescendantDefaults?.CreateRun();
        if (policy.Propagation is NoSubAgentClientPropagation) return result;
        result ??= new SubAgentRunConfig();
        result.Clients = new AgentClientsConfig
        {
            Chat = (ChatClientConfig)ProviderClientConfigSnapshot.Clone(policy.LockedClients.Chat!)
        };
        result.ClientPropagation = policy.Propagation switch
        {
            RemainingSubAgentClientPropagation bounded => SubAgentClientPropagation.ThroughDepth(bounded.RemainingDepth),
            UnboundedRemainingSubAgentClientPropagation => SubAgentClientPropagation.EntireTree,
            _ => throw new InvalidOperationException("subagent_execution_policy_invalid")
        };
        return result;
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
