using HPD.Agent;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent.Providers;
using HostingAgentFactory = HPD.Agent.Hosting.Configuration.IAgentFactory;

namespace HPD.Agent.AspNetCore.Lifecycle;

/// <summary>
/// ASP.NET Core-specific implementation of <see cref="AgentManager"/>.
/// Builds <see cref="Agent"/> instances from stored definitions or fallback config,
/// using <see cref="IOptionsMonitor{HPDAgentHostOptions}"/> for runtime configuration.
/// </summary>
internal class AspNetCoreAgentManager : AgentManager
{
    private readonly AspNetCoreSessionManager _sessionManager;
    private readonly IOptionsMonitor<HPDAgentHostOptions> _optionsMonitor;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _name;
    private readonly IContentStore _contentStore;
    private readonly HostingAgentFactory? _agentFactory;
    private readonly AgentEventComposition _eventComposition;

    internal AspNetCoreAgentManager(
        IAgentStore agentStore,
        AspNetCoreSessionManager sessionManager,
        IOptionsMonitor<HPDAgentHostOptions> optionsMonitor,
        IServiceProvider serviceProvider,
        string name,
        IContentStore contentStore,
        HostingAgentFactory? agentFactory,
        AgentEventComposition eventComposition)
        : base(agentStore)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _agentFactory = agentFactory;
        _eventComposition = eventComposition ?? throw new ArgumentNullException(nameof(eventComposition));
    }

    protected override async Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct)
    {
        var opts = _optionsMonitor.Get(_name);

        // Priority 1: IAgentFactory from DI
        if (_agentFactory != null)
            return await _agentFactory.CreateAgentAsync(agentId, _sessionManager.Store, ct);

        // Definitions are the only agent identities in the default hosting model.
        var providerComposition = _serviceProvider.GetService<ProviderComposition>();
        var stored = await AgentStore.LoadAsync(agentId, ct).ConfigureAwait(false);
        if (stored?.Config is null)
            throw new KeyNotFoundException($"Agent definition '{agentId}' was not found.");

        var builder = providerComposition is null
            ? new AgentBuilder(stored.Config)
            : new AgentBuilder(stored.Config, providerComposition);

        builder
            .WithEventComposition(_eventComposition)
            .WithServiceProvider(_serviceProvider)
            .WithAgentId(agentId)
            .WithAgentStore(AgentStore, persistOnBuild: false)
            .WithSessionStore(_sessionManager.Store, opts.PersistAfterTurn)
            .WithContentStore(_contentStore);

        // The per-agent customization hook always runs last.
        opts.ConfigureEachAgentBuilder?.Invoke(builder);

        return await builder.BuildAsync(ct);
    }

    public override Task<bool> CanResolveAgentAsync(
        string agentId,
        CancellationToken ct = default)
        => _agentFactory is null
            ? base.CanResolveAgentAsync(agentId, ct)
            : Task.FromResult(true);

    /// <summary>
    /// Materializes the definition explicitly declared by this hosting profile when one
    /// does not already exist. This runs during host startup, before requests are served.
    /// </summary>
    internal async Task EnsureConfiguredDefinitionAsync(
        string agentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (await AgentStore.LoadAsync(agentId, cancellationToken).ConfigureAwait(false) is not null)
            return;

        var opts = _optionsMonitor.Get(_name);
        var providerComposition = _serviceProvider.GetService<ProviderComposition>();
        var config = await LoadConfiguredDefinitionAsync(opts, providerComposition, cancellationToken)
            .ConfigureAwait(false)
            ?? new AgentConfig();
        config = CloneConfig(config, providerComposition);
        config.AgentId = null;
        if (config.Name == new AgentConfig().Name)
            config.Name = agentId;

        var now = DateTime.UtcNow;
        await AgentStore.SaveAsync(new StoredAgent
        {
            Id = agentId,
            Name = config.Name,
            Config = config,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AgentConfig?> LoadConfiguredDefinitionAsync(
        HPDAgentHostOptions opts,
        ProviderComposition? providerComposition,
        CancellationToken cancellationToken)
    {
        if (opts.DefaultAgent is not null)
            return opts.DefaultAgent;
        if (opts.DefaultAgentDocument is not null)
        {
            return providerComposition is null
                ? HpdAgentConfigSerializer.Deserialize(opts.DefaultAgentDocument)
                : HpdAgentConfigSerializer.Deserialize(opts.DefaultAgentDocument, providerComposition);
        }
        if (opts.DefaultAgentPath is not null)
        {
            return providerComposition is null
                ? await HpdAgentConfigSerializer.ReadFileAsync(opts.DefaultAgentPath, cancellationToken)
                    .ConfigureAwait(false)
                : await HpdAgentConfigSerializer.ReadFileAsync(
                        opts.DefaultAgentPath,
                        providerComposition,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        return null;
    }

    private static AgentConfig CloneConfig(
        AgentConfig config,
        ProviderComposition? providerComposition)
    {
        var json = providerComposition is null
            ? HpdAgentConfigSerializer.Serialize(config)
            : HpdAgentConfigSerializer.Serialize(config, providerComposition);
        return (providerComposition is null
                ? HpdAgentConfigSerializer.Deserialize(json)
                : HpdAgentConfigSerializer.Deserialize(json, providerComposition))
            ?? throw new InvalidOperationException("Configured agent definition did not round-trip.");
    }

    protected override TimeSpan GetIdleTimeout() =>
        _optionsMonitor.Get(_name).AgentIdleTimeout;
}
