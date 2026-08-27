using HPD.Events;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;
using HPD.Agent;
using System.ComponentModel;

namespace HPD.Agent.Middleware;

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

    public ToolResultMetadata ResultMetadata { get; }

    public AgentRunConfig RunConfig { get; }

    public string? BatchId => InvocationSnapshot.BatchId;

    public int? ToolCallIndex => InvocationSnapshot.ToolCallIndex;

    public IEventCoordinator? EventCoordinator { get; }
    public IThreadEventPublisher? ThreadEvents { get; }

    public IEventFlowRegistry? EventFlows => EventCoordinator?.EventFlows;

    public IStructEventHub? StructEvents { get; }

    public IServiceProvider? Services { get; }

    public IRuntimeCapabilityRegistry RuntimeCapabilities { get; }

    public IContentStore? ContentStore => _contentStore;

    internal AgentClientSet? ClientSet => _clientSet;

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
        CancellationToken cancellationToken = default)
    {
        var registry = OperationRegistry ?? throw new InvalidOperationException(
            "Function execution does not have an active operation registry.");
        if (string.IsNullOrWhiteSpace(SessionId) || string.IsNullOrWhiteSpace(ThreadId))
            throw new InvalidOperationException("Operations require a session and thread address.");
        return AgentLocalOperationScheduler.StartAsync(
            registry,
            AgentOperationSourceKind.LocalTool,
            name,
            new AgentExecutionAddress(AgentName, SessionId, ThreadId),
            ThreadExecutionId,
            InvocationSnapshot,
            metadata,
            notification,
            work,
            cancellationToken);
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
            return await ThreadEvents.CommitAndPublishAsync(
                new ThreadKey(scoped.SessionId, scoped.ThreadId),
                scoped,
                cancellationToken).ConfigureAwait(false);
        }

        await EventCoordinator.EmitAsync(scoped, cancellationToken).ConfigureAwait(false);
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
        var handle = EventCoordinator.RegisterRequest<TRequest, TResponse>(
            tracedRequest,
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
                await EventCoordinator.EmitAsync(tracedRequest, cancellationToken).ConfigureAwait(false);
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
