namespace HPD.Agent;

/// <summary>
/// Creates runtime <see cref="Agent"/> instances from declarative <see cref="AgentConfig"/> data.
/// </summary>
public interface IAgentConfigFactory
{
    /// <summary>Create an agent from serializable configuration.</summary>
    Task<Agent> CreateAsync(AgentConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default factory that turns <see cref="AgentConfig"/> data into a configured runtime agent.
/// </summary>
public sealed class AgentFactory : IAgentConfigFactory
{
    private readonly Action<AgentBuilder>? _configureBuilder;

    public AgentFactory(Action<AgentBuilder>? configureBuilder = null)
    {
        _configureBuilder = configureBuilder;
    }

    public Task<Agent> CreateAsync(AgentConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var builder = new AgentBuilder(config);
        _configureBuilder?.Invoke(builder);
        return builder.BuildAsync(cancellationToken);
    }
}
