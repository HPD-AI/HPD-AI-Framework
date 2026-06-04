using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.Options;

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
    private readonly IAgentFactory? _agentFactory;

    internal AspNetCoreAgentManager(
        IAgentRepository agentRepository,
        AspNetCoreSessionManager sessionManager,
        IOptionsMonitor<HPDAgentConfig> optionsMonitor,
        IServiceProvider serviceProvider,
        string name,
        IAgentFactory? agentFactory = null)
        : base(agentRepository)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _agentFactory = agentFactory;
    }

    protected override async Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct)
    {
        var opts = _optionsMonitor.Get(_name);

        // Priority 1: IAgentFactory from DI
        if (_agentFactory != null)
            return await _agentFactory.CreateAgentAsync(agentId, _sessionManager.Repository, ct);

        // Priority 2: stored.Config loaded by AgentBuilder through IAgentRepository
        // Priority 3: DefaultAgentConfig object
        // Priority 4: DefaultAgentConfigPath file
        // Priority 5: Empty builder (fallback)
        AgentBuilder builder;
        if (opts.DefaultAgentConfig != null)
        {
            builder = new AgentBuilder(opts.DefaultAgentConfig);
        }
        else if (opts.DefaultAgentConfigPath != null)
        {
            var json = await File.ReadAllTextAsync(opts.DefaultAgentConfigPath, ct);
            var loaded = JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize AgentConfig from {opts.DefaultAgentConfigPath}");
            builder = new AgentBuilder(loaded);
        }
        else
        {
            builder = new AgentBuilder();
        }

        builder
            .WithServiceProvider(_serviceProvider)
            .WithAgentId(agentId)
            .WithAgentRepository(Repository, opts.PersistAgentDefinitionsOnBuild)
            .WithSessionRepository(_sessionManager.Repository, opts.PersistAfterTurn);

        // ConfigureAgent always runs last — server enrichment for all agents
        opts.ConfigureAgent?.Invoke(builder);

        return await builder.BuildAsync(ct);
    }

    protected override TimeSpan GetIdleTimeout() =>
        _optionsMonitor.Get(_name).AgentIdleTimeout;
}
