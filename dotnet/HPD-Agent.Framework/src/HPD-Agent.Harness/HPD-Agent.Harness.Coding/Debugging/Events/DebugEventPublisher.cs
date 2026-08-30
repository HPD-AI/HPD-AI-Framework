using HPD.Agent;
using HPD.Events;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public interface IDebugEventPublisher : IDebugLifecycleEventPublisher
{
    ITreeDebugEventPublisher Bind(DebugEventScope scope);

    ValueTask<AgentEvent> PublishDurableAsync(
        DebugEventScope scope,
        AgentEvent @event,
        CancellationToken cancellationToken = default);

    ValueTask PublishLiveAsync(
        DebugEventScope scope,
        AgentEvent @event,
        CancellationToken cancellationToken = default);
}

public interface ITreeDebugEventPublisher : IDebugLifecycleEventPublisher
{
    DebugEventScope Scope { get; }
    ValueTask<AgentEvent> PublishDurableAsync(AgentEvent @event, CancellationToken cancellationToken = default);
    ValueTask PublishLiveAsync(AgentEvent @event, CancellationToken cancellationToken = default);
}

/// <summary>A tree-owned publisher whose HPD scope cannot drift after the launching call returns.</summary>
internal sealed class DebugScopedEventPublisher(
    IDebugLifecycleEventPublisher inner,
    DebugEventScope scope) : ITreeDebugEventPublisher
{
    public DebugEventScope Scope { get; } = scope;

    public ValueTask<AgentEvent> PublishDurableAsync(AgentEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (inner is IDebugEventPublisher publisher)
            return publisher.PublishDurableAsync(Scope, @event, cancellationToken);
        return PublishDurableThroughLifecycleAsync(@event, cancellationToken);
    }

    public ValueTask PublishLiveAsync(AgentEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (inner is IDebugEventPublisher publisher)
            return publisher.PublishLiveAsync(Scope, @event, cancellationToken);
        return inner.PublishAsync(ApplyScope(@event), durable: false, cancellationToken);
    }

    public ValueTask PublishAsync(AgentEvent @event, bool durable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return durable ? IgnoreResult(PublishDurableAsync(@event, cancellationToken)) :
            PublishLiveAsync(@event, cancellationToken);
    }

    private async ValueTask<AgentEvent> PublishDurableThroughLifecycleAsync(
        AgentEvent @event, CancellationToken cancellationToken)
    {
        var scoped = ApplyScope(@event);
        await inner.PublishAsync(scoped, durable: true, cancellationToken).ConfigureAwait(false);
        return scoped;
    }

    private static async ValueTask IgnoreResult(ValueTask<AgentEvent> publication)
        => _ = await publication.ConfigureAwait(false);

    private AgentEvent ApplyScope(AgentEvent @event)
    {
        var scoped = @event with
        {
            SessionId = Scope.SessionId,
            ThreadId = Scope.ThreadId,
            TraceId = Scope.TraceId
        };
        return scoped is DebugLifecycleEvent debug
            ? debug with { ToolCallId = debug.ToolCallId ?? Scope.ToolCallId }
            : scoped;
    }
}

/// <summary>Background-safe debugger publisher retaining no invocation or event-flow state.</summary>
public sealed class DebugEventPublisher : IDebugEventPublisher
{
    private readonly IAgentEventPublisher? _threadEvents;

    public DebugEventPublisher(IEventCoordinator events, IAgentEventPublisher? threadEvents = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        _threadEvents = threadEvents;
    }

    public ITreeDebugEventPublisher Bind(DebugEventScope scope)
        => new DebugScopedEventPublisher(this, scope ?? throw new ArgumentNullException(nameof(scope)));

    public async ValueTask<AgentEvent> PublishDurableAsync(
        DebugEventScope scope,
        AgentEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(@event);
        if (_threadEvents is null)
            throw new InvalidOperationException("Durable debugger publication requires an IAgentEventPublisher.");
        return await _threadEvents.CommitAndPublishAsync(
            new(scope.SessionId, scope.ThreadId), Scope(scope, @event), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PublishLiveAsync(
        DebugEventScope scope,
        AgentEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(@event);
        if (_threadEvents is null)
            throw new InvalidOperationException("Live debugger publication requires an IAgentEventPublisher.");
        _ = await _threadEvents.PublishLiveAsync(Scope(scope, @event), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PublishAsync(
        AgentEvent @event,
        bool durable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (string.IsNullOrWhiteSpace(@event.SessionId) || string.IsNullOrWhiteSpace(@event.ThreadId))
            throw new InvalidOperationException("A debugger event requires HPD session and thread scope.");
        var scope = new DebugEventScope(
            @event.TraceId,
            @event.SessionId,
            @event.ThreadId,
            @event is DebugLifecycleEvent debug ? debug.ToolCallId : null);
        if (durable)
            _ = await PublishDurableAsync(scope, @event, cancellationToken).ConfigureAwait(false);
        else
            await PublishLiveAsync(scope, @event, cancellationToken).ConfigureAwait(false);
    }

    private static AgentEvent Scope(DebugEventScope scope, AgentEvent @event)
    {
        var scoped = @event with
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            TraceId = scope.TraceId
        };
        return scoped is DebugLifecycleEvent debug
            ? debug with { ToolCallId = debug.ToolCallId ?? scope.ToolCallId }
            : scoped;
    }
}
