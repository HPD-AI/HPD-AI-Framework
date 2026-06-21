using HPD.Events;

namespace HPD.Agent.Middleware;

/// <summary>
/// Safe runtime context supplied to background work started by a function body.
/// </summary>
public sealed record FunctionBackgroundContext
{
    public required string TaskId { get; init; }

    public required string Name { get; init; }

    public required FunctionInvocationSnapshot Invocation { get; init; }

    public IEventCoordinator? EventCoordinator { get; init; }

    public IEventFlowRegistry? EventFlows => EventCoordinator?.EventFlows;

    public IServiceProvider? Services { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}
