namespace HPD.Agent;

/// <summary>
/// Creates runtime <see cref="Agent"/> instances from declarative <see cref="AgentConfig"/> data.
/// </summary>
public interface IAgentConfigFactory
{
    /// <summary>Create an agent from serializable configuration.</summary>
    /// <param name="config">The declarative agent configuration to snapshot.</param>
    /// <param name="cancellationToken">A token that cancels construction.</param>
    /// <returns>The configured runtime agent.</returns>
    Task<Agent> CreateAsync(AgentConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default factory that turns <see cref="AgentConfig"/> data into a configured runtime agent.
/// </summary>
public sealed class AgentFactory : IAgentConfigFactory
{
    private readonly Action<AgentBuilder>? _configureBuilder;
    private readonly Providers.ProviderComposition? _providerComposition;

    /// <summary>Initializes a configuration-backed agent factory.</summary>
    /// <param name="configureBuilder">Optional runtime-only builder enrichment applied after declarative configuration.</param>
    /// <param name="providerComposition">The consuming host's generated provider composition.</param>
    public AgentFactory(
        Action<AgentBuilder>? configureBuilder = null,
        Providers.ProviderComposition? providerComposition = null)
    {
        _configureBuilder = configureBuilder;
        _providerComposition = providerComposition;
    }

    /// <inheritdoc />
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
