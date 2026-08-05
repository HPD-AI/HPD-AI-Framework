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
/// using <see cref="IOptionsMonitor{HPDAgentConfig}"/> for runtime configuration.
/// </summary>
internal class AspNetCoreAgentManager : AgentManager
{
    private readonly AspNetCoreSessionManager _sessionManager;
    private readonly IOptionsMonitor<HPDAgentConfig> _optionsMonitor;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _name;
    private readonly IContentStore _contentStore;
    private readonly HostingAgentFactory? _agentFactory;

    internal AspNetCoreAgentManager(
        IAgentStore agentStore,
        AspNetCoreSessionManager sessionManager,
        IOptionsMonitor<HPDAgentConfig> optionsMonitor,
        IServiceProvider serviceProvider,
        string name,
        IContentStore contentStore,
        HostingAgentFactory? agentFactory = null)
        : base(agentStore)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _agentFactory = agentFactory;
    }

    protected override async Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct)
    {
        var opts = _optionsMonitor.Get(_name);

        // Priority 1: IAgentFactory from DI
        if (_agentFactory != null)
            return await _agentFactory.CreateAgentAsync(agentId, _sessionManager.Store, ct);

        // Priority 2: stored.Config loaded from the hosted agent store
        // Priority 3: DefaultAgent object
        // Priority 4: deferred configuration document
        // Priority 5: DefaultAgentPath file
        // Priority 6: Empty builder (fallback)
        AgentBuilder builder;
        var providerComposition = _serviceProvider.GetService<ProviderComposition>();
        var stored = await AgentStore.LoadAsync(agentId, ct).ConfigureAwait(false);
        if (stored?.Config != null)
        {
            builder = providerComposition is null ? new AgentBuilder(stored.Config) : new AgentBuilder(stored.Config, providerComposition);
        }
        else if (opts.DefaultAgent != null)
        {
            builder = providerComposition is null ? new AgentBuilder(opts.DefaultAgent) : new AgentBuilder(opts.DefaultAgent, providerComposition);
        }
        else if (opts.DefaultAgentDocument != null)
        {
            var loaded = providerComposition is null
                ? HpdAgentConfigSerializer.Deserialize(opts.DefaultAgentDocument)
                : HpdAgentConfigSerializer.Deserialize(opts.DefaultAgentDocument, providerComposition);
            if (loaded is null)
                throw new InvalidOperationException("Failed to deserialize the configured default agent definition.");
            builder = providerComposition is null ? new AgentBuilder(loaded) : new AgentBuilder(loaded, providerComposition);
        }
        else if (opts.DefaultAgentPath != null)
        {
            var loaded = providerComposition is null
                ? await HpdAgentConfigSerializer.ReadFileAsync(opts.DefaultAgentPath, ct)
                : await HpdAgentConfigSerializer.ReadFileAsync(opts.DefaultAgentPath, providerComposition, ct);
            if (loaded is null)
                throw new InvalidOperationException(
                    $"Failed to deserialize default agent definition from {opts.DefaultAgentPath}");
            builder = providerComposition is null ? new AgentBuilder(loaded) : new AgentBuilder(loaded, providerComposition);
        }
        else
        {
            builder = providerComposition is null ? new AgentBuilder() : new AgentBuilder(providerComposition);
        }

        builder
            .WithServiceProvider(_serviceProvider)
            .WithAgentId(agentId)
            .WithAgentStore(AgentStore, opts.PersistAgentDefinitionsOnBuild)
            .WithSessionStore(_sessionManager.Store, opts.PersistAfterTurn)
            .WithContentStore(_contentStore);

        // ConfigureAgent always runs last — server runtime enrichment for all agents.
        opts.ConfigureAgent?.Invoke(builder);

        return await builder.BuildAsync(ct);
    }

    protected override TimeSpan GetIdleTimeout() =>
        _optionsMonitor.Get(_name).AgentIdleTimeout;
}
