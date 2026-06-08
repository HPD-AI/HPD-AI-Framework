namespace HPD.Agent.TUI.Runtime;

public sealed class HostedAgentTuiRuntimeOptions
{
    public required Uri BaseAddress { get; init; }

    public AgentTuiRuntimeScope? DefaultScope { get; init; }

    public HttpMessageHandler? MessageHandler { get; init; }

    public TimeSpan RequestTimeout { get; init; } = Timeout.InfiniteTimeSpan;
}
