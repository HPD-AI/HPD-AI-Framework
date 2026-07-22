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
        BackgroundTasks = request.BackgroundTasks;
        BackgroundHandles = request.BackgroundHandles;
        Services = hookContext.Services;
        RuntimeCapabilities = hookContext.RuntimeCapabilities;
        _contentStore = hookContext.ContentStore;
        _effectiveChatClient = hookContext.Base.EffectiveChatClient;
        _parentAgentMetadata = hookContext.GetParentAgentMetadata();
        _parentSessionStore = hookContext.Session?.Store;
        _parentAgentStore = hookContext.GetParentAgentStore();
        _parentConfig = hookContext.Config;
    }

    public FunctionInvocationSnapshot InvocationSnapshot { get; }

    public string AgentName => InvocationSnapshot.AgentName;

    public string? ConversationId => InvocationSnapshot.ConversationId;

    public string? SessionId => InvocationSnapshot.SessionId;

    public string? ThreadId => InvocationSnapshot.ThreadId;

    public string? TraceId => InvocationSnapshot.TraceId;

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


    public IAgentBackgroundTaskRegistry? BackgroundTasks { get; }

    public bool CanRegisterBackgroundTasks => BackgroundTasks is not null;

    public IAgentBackgroundHandleRegistry? BackgroundHandles { get; }

    public bool CanRegisterBackgroundHandles => BackgroundHandles is not null;

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
        TimeSpan? timeout = null)
        where TRequest : AgentEvent, HPD.Events.IRequestEvent
        where TResponse : AgentEvent, HPD.Events.IResponseEvent
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EventCoordinator is null)
            throw new InvalidOperationException("Function execution context does not have an event coordinator.");

        var tracedRequest = WithInvocationScope(request);
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);
        var handle = EventCoordinator.RegisterRequest<TRequest, TResponse>(
            tracedRequest,
            new RequestOptions { Timeout = effectiveTimeout });

        try
        {
            if (!string.IsNullOrWhiteSpace(tracedRequest.SessionId) &&
                !string.IsNullOrWhiteSpace(tracedRequest.ThreadId) &&
                ThreadEvents is not null)
            {
                await ThreadEvents.CommitAndPublishAsync(
                    new ThreadKey(tracedRequest.SessionId, tracedRequest.ThreadId),
                    tracedRequest).ConfigureAwait(false);
            }
            else
            {
                await EventCoordinator.EmitAsync(tracedRequest).ConfigureAwait(false);
            }

            return (TResponse)await handle.Response.ConfigureAwait(false);
        }
        catch
        {
            handle.Cancel("Request publication or wait failed.");
            throw;
        }
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

    public BackgroundTaskRegistration RegisterBackgroundTask(
        string name,
        BackgroundTaskNotificationRule notification,
        Func<BackgroundTaskContext, CancellationToken, Task> taskFactory)
        => RegisterBackgroundTask(
            new BackgroundTaskDescriptor
            {
                Name = name,
                SourceKind = BackgroundTaskSourceKind.ToolCall,
                SourceId = FunctionCallId,
                SessionId = InvocationSnapshot.SessionId,
                ThreadId = InvocationSnapshot.ThreadId,
                Invocation = InvocationSnapshot,
                Notification = notification
            },
            taskFactory);

    /// <summary>
    /// Registers runtime-owned background work from a function invocation.
    /// </summary>
    /// <param name="descriptor">The background task descriptor.</param>
    /// <param name="taskFactory">The background task body.</param>
    /// <returns>The accepted background task registration.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active runtime can accept background tasks.</exception>
    public BackgroundTaskRegistration RegisterBackgroundTask(
        BackgroundTaskDescriptor descriptor,
        Func<BackgroundTaskContext, CancellationToken, Task> taskFactory)
    {
        if (BackgroundTasks is null)
            throw new InvalidOperationException(
                "Function background task registration requires an active agent runtime.");

        return BackgroundTasks.RegisterBackgroundTask(
            descriptor with
            {
                SourceId = descriptor.SourceId ?? FunctionCallId,
                SessionId = descriptor.SessionId ?? InvocationSnapshot.SessionId,
                ThreadId = descriptor.ThreadId ?? InvocationSnapshot.ThreadId,
                Invocation = descriptor.Invocation ?? InvocationSnapshot
            },
            taskFactory);
    }

    /// <summary>
    /// Registers a controllable background resource from a function invocation.
    /// </summary>
    /// <param name="descriptor">The background handle descriptor.</param>
    /// <param name="handle">The handle implementation.</param>
    /// <returns>The accepted background handle registration.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active runtime can accept background handles.</exception>
    public async ValueTask<BackgroundHandleRegistration> RegisterBackgroundHandleAsync(
        BackgroundHandleDescriptor descriptor,
        IBackgroundHandle handle,
        CancellationToken cancellationToken = default)
    {
        if (BackgroundHandles is null)
            throw new InvalidOperationException(
                "Function background handle registration requires an active agent runtime.");

        return await BackgroundHandles.RegisterHandleAsync(
            descriptor with
            {
                SourceId = descriptor.SourceId ?? descriptor.HandleId ?? FunctionCallId,
                SessionId = descriptor.SessionId ?? InvocationSnapshot.SessionId,
                ThreadId = descriptor.ThreadId ?? InvocationSnapshot.ThreadId,
                Invocation = descriptor.Invocation ?? InvocationSnapshot
            },
            handle,
            cancellationToken).ConfigureAwait(false);
    }

    private TEvent WithInvocationScope<TEvent>(TEvent evt)
        where TEvent : AgentEvent
    {
        if ((TraceId is null || evt.TraceId is not null) &&
            (SessionId is null || evt.SessionId is not null) &&
            (ThreadId is null || evt.ThreadId is not null))
        {
            return evt;
        }

        return (TEvent)(evt with
        {
            TraceId = evt.TraceId ?? TraceId,
            SessionId = evt.SessionId ?? SessionId,
            ThreadId = evt.ThreadId ?? ThreadId
        });
    }
}
