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
    private readonly IChatClient? _parentChatClient;
    private readonly AgentMetadata? _parentAgentMetadata;
    private readonly ISessionStore? _parentSessionStore;
    private readonly IAgentStore? _parentAgentStore;
    private readonly IContentStore? _contentStore;

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
        StructEvents = request.StructEvents;
        BackgroundTasks = request.BackgroundTasks;
        Services = hookContext.Services;
        RuntimeCapabilities = hookContext.RuntimeCapabilities;
        _contentStore = hookContext.ContentStore;
        _parentChatClient = hookContext.GetParentChatClient();
        _parentAgentMetadata = hookContext.GetParentAgentMetadata();
        _parentSessionStore = hookContext.Session?.Store;
        _parentAgentStore = hookContext.GetParentAgentStore();
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

    public IEventFlowRegistry? EventFlows => EventCoordinator?.EventFlows;

    public IStructEventHub? StructEvents { get; }

    public IServiceProvider? Services { get; }

    public IRuntimeCapabilityRegistry RuntimeCapabilities { get; }

    public IContentStore? ContentStore => _contentStore;

    public IAgentBackgroundTaskRegistry? BackgroundTasks { get; }

    public bool CanRegisterBackgroundTasks => BackgroundTasks is not null;

    public T Analyze<T>(Func<AgentLoopState, T> analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        return analyzer(_stateSnapshot);
    }

    public void Emit(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (EventCoordinator is null)
            throw new InvalidOperationException("Function execution context does not have an event coordinator.");

        EventCoordinator.Emit(WithInvocationScope(evt));
    }

    public bool TryEmit(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (EventCoordinator is null)
            return false;

        EventCoordinator.Emit(WithInvocationScope(evt));
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
        return await EventCoordinator.RequestAsync<TRequest, TResponse>(
            tracedRequest,
            effectiveTimeout).ConfigureAwait(false);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public IEventCoordinator? GetParentEventCoordinator()
        => EventCoordinator;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public IChatClient? GetParentChatClient()
        => _parentChatClient;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public AgentMetadata? GetParentAgentMetadata()
        => _parentAgentMetadata ?? Agent.RootAgent?.AgentMetadata;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public ISessionStore? GetParentSessionStore()
        => _parentSessionStore;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public IAgentStore? GetParentAgentStore()
        => _parentAgentStore;

    public void RegisterBackgroundTask(
        string name,
        BackgroundTaskNotificationPolicy notificationPolicy,
        Func<BackgroundTaskContext, CancellationToken, Task> taskFactory)
    {
        if (BackgroundTasks is null)
            throw new InvalidOperationException(
                "Function background task registration requires an active agent runtime.");

        BackgroundTasks.RegisterBackgroundTask(
            new BackgroundTaskDescriptor
            {
                Name = name,
                SourceKind = BackgroundTaskSourceKind.ToolCall,
                SourceId = FunctionCallId,
                SessionId = InvocationSnapshot.SessionId,
                ThreadId = InvocationSnapshot.ThreadId,
                Invocation = InvocationSnapshot,
                NotificationPolicy = notificationPolicy
            },
            taskFactory);
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
