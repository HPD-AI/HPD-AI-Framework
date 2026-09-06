namespace HPD.Agent.TUI.Interactions;

public enum AgentTuiInteractionResultKind
{
    NoOp,
    AnswerRequest,
    InterruptTurn,
    Dismiss,
    Error,
    Defer
}

public sealed record AgentTuiInteractionResult(
    AgentTuiInteractionResultKind Kind,
    AgentEvent? Response = null,
    string? Reason = null)
{
    public static AgentTuiInteractionResult NoOp { get; } =
        new(AgentTuiInteractionResultKind.NoOp);

    /// <summary>Hide the presentation while leaving its request pending and answerable.</summary>
    public static AgentTuiInteractionResult Defer { get; } = new(AgentTuiInteractionResultKind.Defer);

    public static AgentTuiInteractionResult Dismiss { get; } =
        new(AgentTuiInteractionResultKind.Dismiss);

    public static AgentTuiInteractionResult AnswerRequest(AgentEvent response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response is not IAgentResponseEvent)
        {
            throw new ArgumentException(
                $"Event '{response.GetType().Name}' must implement IAgentResponseEvent.",
                nameof(response));
        }

        return new AgentTuiInteractionResult(
            AgentTuiInteractionResultKind.AnswerRequest,
            Response: response);
    }

    public static AgentTuiInteractionResult InterruptTurn(string reason)
        => new(
            AgentTuiInteractionResultKind.InterruptTurn,
            Reason: string.IsNullOrWhiteSpace(reason)
                ? "Interrupted by TUI interaction."
                : reason);

    public static AgentTuiInteractionResult Error(string message)
        => new(
            AgentTuiInteractionResultKind.Error,
            Reason: string.IsNullOrWhiteSpace(message)
                ? "Interaction failed."
                : message);
}

public interface IAgentTuiInteractionHandler
{
    bool CanHandle(AgentEvent request);

    Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext context,
        CancellationToken cancellationToken = default);
}

public abstract class AgentTuiInteractionHandler<TRequest> : IAgentTuiInteractionHandler
    where TRequest : AgentEvent
{
    public bool CanHandle(AgentEvent request) => request is TRequest;

    public Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Request is not TRequest typed)
        {
            return Task.FromResult(AgentTuiInteractionResult.NoOp);
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

    protected abstract Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<TRequest> context,
        CancellationToken cancellationToken);
}
