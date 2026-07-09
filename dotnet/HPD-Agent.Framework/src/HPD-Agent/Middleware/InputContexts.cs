namespace HPD.Agent.Middleware;

public sealed class BeforeInputContext
{
    internal BeforeInputContext(
        AgentInputEvent input,
        AgentInputHandlingContext handling)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Handling = handling ?? throw new ArgumentNullException(nameof(handling));
    }

    public AgentInputEvent Input { get; private set; }
    internal AgentInputHandlingContext Handling { get; }
    public bool Cancelled { get; private set; }
    public string? CancelReason { get; private set; }

    public string AgentName => Handling.AgentName;
    public AgentConfig Config => Handling.Config;
    public IServiceProvider? Services => Handling.Services;

    public void ReplaceInput(AgentInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Cancelled)
            throw new InvalidOperationException("Cannot replace an input after it has been cancelled.");

        Input = input;
    }

    public void CancelInput(string? reason = null)
    {
        Cancelled = true;
        CancelReason = reason;
    }
}

public sealed class AfterInputContext
{
    internal AfterInputContext(
        AgentInputEvent input,
        AgentInputHandlingContext handling,
        AgentTurnResult result,
        Exception? error,
        bool cancelled,
        TimeSpan duration)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Handling = handling ?? throw new ArgumentNullException(nameof(handling));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Error = error;
        Cancelled = cancelled;
        Duration = duration;
    }

    public AgentInputEvent Input { get; }
    internal AgentInputHandlingContext Handling { get; }
    public AgentTurnResult Result { get; }
    public Exception? Error { get; }
    public bool Cancelled { get; }
    public TimeSpan Duration { get; }

    public string AgentName => Handling.AgentName;
    public AgentConfig Config => Handling.Config;
    public IServiceProvider? Services => Handling.Services;
}
