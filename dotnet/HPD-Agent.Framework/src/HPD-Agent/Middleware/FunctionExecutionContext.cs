using HPD.Events;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Permissions;
using System.ComponentModel;

namespace HPD.Agent.Middleware;

internal sealed class FunctionOperationCommitGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AgentOperationReceipt? _committed;

    internal AgentOperationReceipt? CommittedReceipt => Volatile.Read(ref _committed);

    internal async ValueTask<AgentOperationReceipt> StartOperationAsync(
        Func<ValueTask<AgentOperationReceipt>> start,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_committed is not null)
                throw new InvalidOperationException("tool_body_operation_already_committed");
            var receipt = await start().ConfigureAwait(false);
            Volatile.Write(ref _committed, receipt);
            return receipt;
        }
        finally { _gate.Release(); }
    }
}

/// <summary>Exposes the immutable permission authority admitted for one function invocation.</summary>
public sealed class FunctionExecutionPermission
{
    internal FunctionExecutionPermission(bool isRequired, FunctionPermissionGrant? grant)
    {
        IsRequired = isRequired;
        Grant = grant;
    }

    /// <summary>Gets whether the effective function/action declaration required permission.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets whether an invocation-bound approval grant is present.</summary>
    public bool IsApproved => Grant is not null;

    /// <summary>Gets the invocation-bound grant, when approval was required and issued.</summary>
    public FunctionPermissionGrant? Grant { get; }

    /// <summary>Returns the grant or throws when this invocation was not approved.</summary>
    public FunctionPermissionGrant DemandApproved() => Grant ??
        throw new InvalidOperationException("This invocation does not carry an approved permission grant.");

    /// <summary>Returns the grant only when it authorizes the exact expected authority.</summary>
    public FunctionPermissionGrant DemandAuthority(string expectedAuthority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAuthority);
        var grant = DemandApproved();
        return string.Equals(grant.Key.Authority, expectedAuthority, StringComparison.Ordinal)
            ? grant
            : throw new InvalidOperationException($"The permission grant does not authorize authority '{expectedAuthority}'.");
    }

    /// <summary>Returns the grant only when it was issued by the exact expected policy and revision.</summary>
    public FunctionPermissionGrant DemandPolicy(string expectedPolicyId, string? expectedRevision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPolicyId);
        var grant = DemandApproved();
        if (!string.Equals(grant.Key.PolicyId, expectedPolicyId, StringComparison.Ordinal) ||
            expectedRevision is not null && !string.Equals(grant.Key.PolicyRevision, expectedRevision, StringComparison.Ordinal))
            throw new InvalidOperationException($"The permission grant was not issued by policy '{expectedPolicyId}'.");
        return grant;
    }

    /// <summary>Returns the grant only when it was issued by the exact expected policy type.</summary>
    /// <typeparam name="TPolicy">The generated permission policy type.</typeparam>
    public FunctionPermissionGrant DemandPolicy<TPolicy>() where TPolicy : IPermissionPolicy
    {
        var fullName = typeof(TPolicy).FullName ?? typeof(TPolicy).Name;
        var grant = DemandApproved();
        var descriptorId = grant.Authority.Declaration.PolicyDescriptorId;
        return string.Equals(descriptorId, fullName, StringComparison.Ordinal) ||
            string.Equals(descriptorId, "global::" + fullName, StringComparison.Ordinal)
            ? grant
            : throw new InvalidOperationException($"The permission grant was not activated by policy type '{fullName}'.");
    }
}

/// <summary>
/// Narrow context exposed to AIFunction bodies during function execution.
/// </summary>
/// <remarks>
/// This context intentionally does not expose AgentContext, HookContext,
/// UpdateState, UpdateMiddlewareState, or raw mutable Session/Thread objects.
/// Function bodies may use runtime services, but live state mutation belongs to
/// scheduler-owned middleware phases.
/// </remarks>
public sealed class FunctionExecutionContext
{
    private readonly AgentLoopState _stateSnapshot;
    private readonly AgentChatClientHandle? _effectiveChatClient;
    private readonly AgentMetadata? _parentAgentMetadata;
    private readonly ISessionStore? _parentSessionStore;
    private readonly IAgentStore? _parentAgentStore;
    private readonly IContentStore? _contentStore;
    private readonly AgentConfig? _parentConfig;
    private readonly AgentClientSet? _clientSet;
    private readonly ToolHarnessExecutionScope? _toolHarnessExecutionScope;
    private readonly FunctionOperationCommitGate? _operationCommitGate;

    internal FunctionExecutionContext(
        HookContext hookContext,
        FunctionRequest request)
    {
        ArgumentNullException.ThrowIfNull(hookContext);
        ArgumentNullException.ThrowIfNull(request);

        InvocationSnapshot = new FunctionInvocationSnapshot
        {
            AgentName = hookContext.AgentName,
            ConversationId = hookContext.ConversationId,
            SessionId = hookContext.SessionId,
            ThreadId = hookContext.ThreadId,
            TraceId = hookContext.TraceId,
            ThreadExecutionId = hookContext.ThreadExecutionId,
            FunctionCallId = request.CallId,
            FunctionName = request.FunctionName,
            Invocation = request.Invocation
        };
        _stateSnapshot = request.State;
        RunConfig = request.RunConfig;
        InvocationMode = request.InvocationMode;
        Permission = new FunctionExecutionPermission(request.PermissionRequired, request.PermissionGrant);
        ResultMetadata = request.ResultMetadata;
        EventCoordinator = request.EventCoordinator;
        ThreadEvents = hookContext.Base.ThreadEvents;
        StructEvents = request.StructEvents;
        Services = hookContext.Services;
        RuntimeCapabilities = hookContext.RuntimeCapabilities;
        _contentStore = hookContext.ContentStore;
        _effectiveChatClient = hookContext.Base.EffectiveChatClient;
        _parentAgentMetadata = hookContext.GetParentAgentMetadata();
        _parentSessionStore = hookContext.Session?.Store;
        _parentAgentStore = hookContext.GetParentAgentStore();
        _parentConfig = hookContext.Config;
        _clientSet = hookContext.Base.ClientSet;
        _toolHarnessExecutionScope = hookContext.Base.ToolHarnessExecutionScope;
        _operationCommitGate = request.OperationCommitGate;
    }

    private FunctionExecutionContext(FunctionExecutionContext source)
    {
        InvocationSnapshot = source.InvocationSnapshot;
        InvocationMode = source.InvocationMode;
        Permission = source.Permission;
        _stateSnapshot = source._stateSnapshot;
        RunConfig = source.RunConfig;
        ResultMetadata = new ToolResultMetadata();
        EventCoordinator = source.EventCoordinator;
        ThreadEvents = source.ThreadEvents;
        StructEvents = source.StructEvents;
        Services = source._toolHarnessExecutionScope?.Services;
        RuntimeCapabilities = source.RuntimeCapabilities;
        _contentStore = null;
        _parentAgentMetadata = null;
        _parentSessionStore = null;
        _parentAgentStore = null;
        _parentConfig = source._parentConfig;
        _clientSet = source._clientSet;
        _effectiveChatClient = source._effectiveChatClient;
        _toolHarnessExecutionScope = null;
        _operationCommitGate = source._operationCommitGate;
    }

    /// <summary>Creates an operation-owned context projection and acquires its client lifetime lease.</summary>
    /// <param name="executionOwner">Receives the lease that must be owned by the operation.</param>
    internal FunctionExecutionContext CreateOperationProjection(out IAsyncDisposable? executionOwner)
    {
        executionOwner = AcquireOperationExecutionOwner();
        return new(this);
    }

    private IAsyncDisposable? AcquireOperationExecutionOwner()
    {
        var clientSetLease = _clientSet?.AcquireBorrowedLease();
        AgentChatClientLease? chatLease = null;
        try
        {
            chatLease = _effectiveChatClient?.AcquireLease();
            return (clientSetLease, chatLease) switch
            {
                (null, null) => null,
                (not null, null) => clientSetLease,
                (null, not null) => chatLease,
                _ => new CompositeExecutionOwner(clientSetLease!, chatLease!)
            };
        }
        catch
        {
            if (clientSetLease is not null)
                clientSetLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public FunctionInvocationSnapshot InvocationSnapshot { get; }

    public string AgentName => InvocationSnapshot.AgentName;

    public string? ConversationId => InvocationSnapshot.ConversationId;

    public string? SessionId => InvocationSnapshot.SessionId;

    public string? ThreadId => InvocationSnapshot.ThreadId;

    public string? TraceId => InvocationSnapshot.TraceId;

    public string? ThreadExecutionId => InvocationSnapshot.ThreadExecutionId;

    public string FunctionCallId => InvocationSnapshot.FunctionCallId;

    public string FunctionName => InvocationSnapshot.FunctionName;

    public ToolInvocationInfo? Invocation => InvocationSnapshot.Invocation;

    /// <summary>Gets the immutable action and invocation-mode facts resolved for this call.</summary>
    public ResolvedFunctionInvocation? InvocationMode { get; }

    /// <summary>Gets the defense-in-depth permission view for this invocation.</summary>
    public FunctionExecutionPermission Permission { get; }

    /// <summary>Gets the resolved execution mode, or synchronous for an uncontracted legacy call.</summary>
    public AgentInvocationMode ResolvedInvocationMode => InvocationMode?.Mode ?? AgentInvocationMode.Synchronous;

    /// <summary>Gets the resolved action discriminator, when this is a compound function.</summary>
    public string? ResolvedAction => InvocationMode?.Action;

    public ToolResultMetadata ResultMetadata { get; }

    public AgentRunConfig RunConfig { get; }

    public string? BatchId => InvocationSnapshot.BatchId;

    public int? ToolCallIndex => InvocationSnapshot.ToolCallIndex;

    public IEventCoordinator? EventCoordinator { get; }
    public IAgentEventPublisher? ThreadEvents { get; }

    public IEventFlowRegistry? EventFlows => EventCoordinator?.EventFlows;

    public IStructEventHub? StructEvents { get; }

    public IServiceProvider? Services { get; }

    public IRuntimeCapabilityRegistry RuntimeCapabilities { get; }

    public IContentStore? ContentStore => _contentStore;

    internal AgentClientSet? ClientSet => _clientSet;
    internal ToolHarnessExecutionScope? ToolHarnessExecutionScope => _toolHarnessExecutionScope;

    /// <summary>Gets the unified operation registry owned by the active runtime.</summary>
    internal AgentOperationRegistry? OperationRegistry =>
        RuntimeCapabilities.TryGet<AgentOperationRegistry>(out var registry) ? registry : null;

    /// <summary>Gets whether this invocation can register unified operations.</summary>
    public bool CanStartOperations => OperationRegistry is not null &&
        !string.IsNullOrWhiteSpace(SessionId) && !string.IsNullOrWhiteSpace(ThreadId);

    /// <summary>Gets immutable snapshots of operations owned by the active agent runtime.</summary>
    public IReadOnlyList<AgentOperationSnapshot> ListOperations() =>
        OperationRegistry?.Snapshot() ?? [];

    /// <summary>Requests cancellation of an operation owned by the active agent runtime.</summary>
    /// <param name="operationId">The HPD-authoritative operation identifier.</param>
    /// <param name="cancellationToken">A token that cancels the control request.</param>
    public ValueTask CancelOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default) =>
        (OperationRegistry ?? throw new InvalidOperationException(
            "Function execution does not have an active operation registry."))
        .RequestCancellationAsync(operationId, cancellationToken);

    /// <summary>Starts runtime-owned work as one unified local-tool operation.</summary>
    /// <param name="name">The stable operation name.</param>
    /// <param name="metadata">Bounded, non-secret operation metadata.</param>
    /// <param name="notification">The semantic notification policy.</param>
    /// <param name="work">The work body, which receives the operation ID and its lifetime token.</param>
    /// <param name="cancellationToken">A token linked to the operation lifetime.</param>
    /// <returns>The authoritative operation receipt.</returns>
    public ValueTask<AgentOperationReceipt> StartOperationAsync(
        string name,
        IReadOnlyDictionary<string, string>? metadata,
        AgentOperationNotificationPolicy notification,
        Func<string, CancellationToken, ValueTask<AgentOperationCompletion>> work,
        CancellationToken cancellationToken = default,
        string? operationId = null)
    {
        var registry = OperationRegistry ?? throw new InvalidOperationException(
            "Function execution does not have an active operation registry.");
        if (string.IsNullOrWhiteSpace(SessionId) || string.IsNullOrWhiteSpace(ThreadId))
            throw new InvalidOperationException("Operations require a session and thread address.");
        async ValueTask<AgentOperationReceipt> StartAsync()
        {
            var clientExecutionOwner = AcquireOperationExecutionOwner();
            try
            {
                return await AgentLocalOperationScheduler.StartAsync(
                    registry,
                    AgentOperationSourceKind.LocalTool,
                    name,
                    new AgentExecutionAddress(AgentName, SessionId, ThreadId),
                    ThreadExecutionId,
                    InvocationSnapshot,
                    metadata,
                    notification,
                    work,
                    _toolHarnessExecutionScope,
                    clientExecutionOwner,
                    cancellationToken,
                    operationId).ConfigureAwait(false);
            }
            catch
            {
                if (clientExecutionOwner is not null)
                    await clientExecutionOwner.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        return _operationCommitGate is null
            ? StartAsync()
            : _operationCommitGate.StartOperationAsync(StartAsync, cancellationToken);
    }

    private sealed class CompositeExecutionOwner(
        IAsyncDisposable clientSetLease,
        IAsyncDisposable chatLease) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await chatLease.DisposeAsync().ConfigureAwait(false);
            await clientSetLease.DisposeAsync().ConfigureAwait(false);
        }
    }



    public T Analyze<T>(Func<AgentLoopState, T> analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        return analyzer(_stateSnapshot);
    }

    public async ValueTask<AgentEvent> PublishAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (EventCoordinator is null)
            throw new InvalidOperationException("Function execution context does not have an event coordinator.");

        var scoped = WithInvocationScope(evt);
        if (!string.IsNullOrWhiteSpace(scoped.SessionId) &&
            !string.IsNullOrWhiteSpace(scoped.ThreadId) &&
            ThreadEvents is not null)
        {
            return await ThreadEvents.PublishAsync(
                new ThreadKey(scoped.SessionId, scoped.ThreadId),
                scoped,
                cancellationToken).ConfigureAwait(false);
        }

        return await PublishLiveAsync(scoped, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AgentEvent> PublishLiveAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (ThreadEvents is not null)
            return await ThreadEvents.PublishLiveAsync(WithInvocationScope(evt), cancellationToken).ConfigureAwait(false);
        if (EventCoordinator is null)
            throw new InvalidOperationException("Function execution context does not have an event coordinator.");
        var codec = _parentConfig?.EventComposition?.Codec
            ?? throw new InvalidOperationException("Function execution context has no event composition authority.");
        var scoped = WithInvocationScope(evt);
        if (!codec.TryGetByType(scoped.GetType(), out _))
            throw new InvalidOperationException($"Agent event type '{scoped.GetType().FullName}' is not present in codec '{codec.Digest}'.");
        scoped = scoped with { ThreadSequenceNumber = 0 };
        await EventCoordinator.EmitAsync(scoped, AgentEventRoutes.Create(scoped), cancellationToken).ConfigureAwait(false);
        return scoped;
    }

    public async ValueTask<bool> TryPublishAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (EventCoordinator is null)
            return false;

        await PublishAsync(evt, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
        where TRequest : AgentEvent, IAgentRequestEvent<TResponse>
        where TResponse : AgentEvent, IAgentResponseEvent
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EventCoordinator is null)
            throw new InvalidOperationException("Function execution context does not have an event coordinator.");

        var tracedRequest = WithInvocationScope(request);
        var route = AgentEventRoutes.Create(tracedRequest);
        var handle = EventCoordinator.RegisterRequest<TRequest, TResponse>(
            tracedRequest,
            route,
            new RequestOptions
            {
                Timeout = timeout,
                CancellationToken = cancellationToken
            });

        var durableRequestPublished = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(tracedRequest.SessionId) &&
                !string.IsNullOrWhiteSpace(tracedRequest.ThreadId) &&
                ThreadEvents is not null)
            {
                await ThreadEvents.CommitAndPublishAsync(
                    new ThreadKey(tracedRequest.SessionId, tracedRequest.ThreadId),
                    tracedRequest,
                    cancellationToken).ConfigureAwait(false);
                durableRequestPublished = true;
            }
            else
            {
                await EventCoordinator.EmitAsync(tracedRequest, route, cancellationToken).ConfigureAwait(false);
            }

            return (TResponse)await handle.Response.ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (durableRequestPublished)
                await CommitRequestTerminalAsync(tracedRequest, AgentRequestTerminalKind.Expired, "The request deadline elapsed.").ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            if (durableRequestPublished)
                await CommitRequestTerminalAsync(tracedRequest, AgentRequestTerminalKind.Cancelled, "The owning function execution was cancelled.").ConfigureAwait(false);
            throw;
        }
        catch
        {
            handle.Cancel("Request publication or wait failed.");
            if (durableRequestPublished)
                await CommitRequestTerminalAsync(tracedRequest, AgentRequestTerminalKind.Cancelled, "Request publication or wait failed.").ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask CommitRequestTerminalAsync(
        AgentEvent requestEvent,
        AgentRequestTerminalKind terminalKind,
        string reason)
    {
        if (ThreadEvents is null ||
            string.IsNullOrWhiteSpace(requestEvent.SessionId) ||
            string.IsNullOrWhiteSpace(requestEvent.ThreadId) ||
            requestEvent is not IAgentRequestEvent request)
        {
            return;
        }

        await ThreadEvents.CommitAndPublishAsync(
            new ThreadKey(requestEvent.SessionId, requestEvent.ThreadId),
            new AgentRequestTerminatedEvent(
                request.RequestId,
                request.SourceName,
                terminalKind,
                reason,
                DateTimeOffset.UtcNow)
            {
                ThreadExecutionId = requestEvent.ThreadExecutionId,
                TraceId = requestEvent.TraceId
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public IEventCoordinator? GetParentEventCoordinator()
        => EventCoordinator;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public IChatClient? GetParentChatClient()
        => _effectiveChatClient?.Client;

    internal AgentChatClientHandle? GetEffectiveChatClientHandle()
        => _effectiveChatClient;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public AgentMetadata? GetParentAgentMetadata()
        => _parentAgentMetadata ?? Agent.RootAgent?.AgentMetadata;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public ISessionStore? GetParentSessionStore()
        => _parentSessionStore;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public IAgentStore? GetParentAgentStore()
        => _parentAgentStore;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public AgentConfig? GetParentAgentConfigSnapshot()
        => _parentConfig is null ? null : AgentConfigSnapshot.Create(_parentConfig);

    private TEvent WithInvocationScope<TEvent>(TEvent evt)
        where TEvent : AgentEvent
    {
        if ((TraceId is null || evt.TraceId is not null) &&
            (ThreadExecutionId is null || evt.ThreadExecutionId is not null) &&
            (SessionId is null || evt.SessionId is not null) &&
            (ThreadId is null || evt.ThreadId is not null))
        {
            return evt;
        }

        return (TEvent)(evt with
        {
            TraceId = evt.TraceId ?? TraceId,
            ThreadExecutionId = evt.ThreadExecutionId ?? ThreadExecutionId,
            SessionId = evt.SessionId ?? SessionId,
            ThreadId = evt.ThreadId ?? ThreadId
        });
    }
}
