using HPD.Agent.Providers;
using HPD.Agent.Serialization;

namespace HPD.Agent.TUI.Runtime;

/// <summary>Configures a hosted HTTP-backed TUI runtime.</summary>
public sealed class HostedAgentTuiRuntimeOptions
{
    /// <summary>Gets the hosted agent API base address.</summary>
    public required Uri BaseAddress { get; init; }

    /// <summary>Gets the initial agent, session, and thread scope.</summary>
    public AgentTuiRuntimeScope? DefaultScope { get; init; }

    /// <summary>Gets an optional HTTP handler owned by the runtime's generated client.</summary>
    public HttpMessageHandler? MessageHandler { get; init; }

    /// <summary>Gets the HTTP request timeout.</summary>
    public TimeSpan RequestTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets the generated provider composition used to serialize typed provider configuration
    /// in agent create and update requests.
    /// </summary>
    public required ProviderComposition ProviderComposition { get; init; }

    /// <summary>Gets the immutable output-event composition exposed by the target application.</summary>
    public required AgentEventComposition EventComposition { get; init; }
}
