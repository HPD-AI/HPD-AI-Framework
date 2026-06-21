namespace HPD.Agent.TUI.Interactions;

public interface IAgentTuiInteractionHandler
{
    bool CanHandle(AgentEvent request);

    Task<AgentEvent?> HandleAsync(
        AgentTuiInteractionContext context,
        CancellationToken cancellationToken = default);
}

public abstract class AgentTuiInteractionHandler<TRequest> : IAgentTuiInteractionHandler
    where TRequest : AgentEvent
{
    public bool CanHandle(AgentEvent request) => request is TRequest;

    public Task<AgentEvent?> HandleAsync(
        AgentTuiInteractionContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Request is not TRequest typed)
        {
            return Task.FromResult<AgentEvent?>(null);
        }

        return HandleAsync(
            new AgentTuiInteractionContext<TRequest>(
                context.Scope,
                context.Shell,
                context.Navigation,
                context.Runtime,
                context.Dialogs,
                typed),
            cancellationToken);
    }

    protected abstract Task<AgentEvent?> HandleAsync(
        AgentTuiInteractionContext<TRequest> context,
        CancellationToken cancellationToken);
}
