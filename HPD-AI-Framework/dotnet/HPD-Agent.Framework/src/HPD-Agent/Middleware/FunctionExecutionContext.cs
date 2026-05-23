using HPD.Events;
using Microsoft.Extensions.AI;
using HPD.Agent;
using System.ComponentModel;

namespace HPD.Agent.Middleware;

/// <summary>
/// Narrow context exposed to AIFunction bodies during function execution.
/// </summary>
/// <remarks>
/// This context intentionally does not expose AgentContext, HookContext,
/// UpdateState, UpdateMiddlewareState, or raw mutable Session/Branch objects.
/// Function bodies may use runtime services, but live state mutation belongs to
/// scheduler-owned middleware phases.
/// </remarks>
public sealed class FunctionExecutionContext
{
    private readonly AgentLoopState _stateSnapshot;
    private readonly IChatClient? _parentChatClient;
    private readonly AgentExecutionContext? _parentExecutionContext;
    private readonly ISessionStore? _parentSessionStore;

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
            BranchId = hookContext.BranchId,
            TraceId = hookContext.TraceId,
            FunctionCallId = request.CallId,
            FunctionName = request.FunctionName,
            Invocation = request.Invocation
        };
        _stateSnapshot = request.State;
        RunConfig = request.RunConfig;
        ResultMetadata = request.ResultMetadata;
        EventCoordinator = request.EventCoordinator;
        BackgroundTasks = request.BackgroundTasks;
        Services = hookContext.Services;
        RuntimeCapabilities = hookContext.RuntimeCapabilities;
        _parentChatClient = hookContext.GetParentChatClient();
        _parentExecutionContext = hookContext.GetParentExecutionContext();
        _parentSessionStore = hookContext.Session?.Store;
    }

    public FunctionInvocationSnapshot InvocationSnapshot { get; }

    public string AgentName => InvocationSnapshot.AgentName;

    public string? ConversationId => InvocationSnapshot.ConversationId;

    public string? SessionId => InvocationSnapshot.SessionId;

    public string? BranchId => InvocationSnapshot.BranchId;

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

    public IServiceProvider? Services { get; }

    public IRuntimeCapabilityRegistry RuntimeCapabilities { get; }

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

        EventCoordinator.Emit(WithTraceId(evt));
    }

    public bool TryEmit(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (EventCoordinator is null)
            return false;

        EventCoordinator.Emit(WithTraceId(evt));
        return true;
    }

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan? timeout = null)
        where TRequest : AgentEvent, HPD.Events.IBidirectionalEvent
        where TResponse : AgentEvent
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EventCoordinator is null)
            throw new InvalidOperationException("Function execution context does not have an event coordinator.");

        var tracedRequest = WithTraceId(request);
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
    public AgentExecutionContext? GetParentExecutionContext()
        => _parentExecutionContext;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public ISessionStore? GetParentSessionStore()
        => _parentSessionStore;

    public void RegisterBackgroundTask(
        string name,
        Func<FunctionBackgroundContext, CancellationToken, Task> taskFactory)
    {
        if (BackgroundTasks is null)
            throw new InvalidOperationException(
                "Function background task registration requires an active agent runtime.");

        BackgroundTasks.RegisterBackgroundTask(name, InvocationSnapshot, taskFactory);
    }

    private TEvent WithTraceId<TEvent>(TEvent evt)
        where TEvent : AgentEvent
    {
        if (TraceId is not null && evt.TraceId is null)
            return (TEvent)(evt with { TraceId = TraceId });

        return evt;
    }
}
