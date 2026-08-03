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
    private readonly Providers.ProviderComposition? _providerComposition;

    public AgentFactory(
        Action<AgentBuilder>? configureBuilder = null,
        Providers.ProviderComposition? providerComposition = null)
    {
        _configureBuilder = configureBuilder;
        _providerComposition = providerComposition;
    }

    public Task<Agent> CreateAsync(AgentConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var builder = _providerComposition is null
            ? new AgentBuilder(config)
            : new AgentBuilder(config, _providerComposition);
        _configureBuilder?.Invoke(builder);
        return builder.BuildAsync(cancellationToken);
    }
}
