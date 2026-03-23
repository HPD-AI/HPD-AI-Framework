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
    private readonly string _name;
    private readonly IAgentFactory? _agentFactory;

    internal AspNetCoreAgentManager(
        IAgentStore agentStore,
        AspNetCoreSessionManager sessionManager,
        IOptionsMonitor<HPDAgentConfig> optionsMonitor,
        string name,
        IAgentFactory? agentFactory = null)
        : base(agentStore)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _agentFactory = agentFactory;
    }

    protected override async Task<Agent> BuildAgentAsync(StoredAgent stored, CancellationToken ct)
    {
        var opts = _optionsMonitor.Get(_name);

        // Priority 1: IAgentFactory from DI
        if (_agentFactory != null)
            return await _agentFactory.CreateAgentAsync(stored.Id, _sessionManager.Store, ct);

        // Priority 2: stored.Config (user-provided definition via API)
        // Priority 3: DefaultAgentConfig object
        // Priority 4: DefaultAgentConfigPath file
        // Priority 5: Empty builder (fallback)
        AgentBuilder builder;
        if (stored.Config is { } config && config != new AgentConfig())
        {
            builder = new AgentBuilder(config);
        }
        else if (opts.DefaultAgentConfig != null)
        {
            builder = new AgentBuilder(opts.DefaultAgentConfig);
        }
        else if (opts.DefaultAgentConfigPath != null)
        {
            var json = await File.ReadAllTextAsync(opts.DefaultAgentConfigPath, ct);
            var loaded = JsonSerializer.Deserialize<AgentConfig>(json)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize AgentConfig from {opts.DefaultAgentConfigPath}");
            builder = new AgentBuilder(loaded);
        }
        else
        {
            builder = new AgentBuilder();
        }

        builder.WithSessionStore(_sessionManager.Store, opts.PersistAfterTurn);

        // ConfigureAgent always runs last — server enrichment for all agents
        opts.ConfigureAgent?.Invoke(builder);

        return await builder.BuildAsync(ct);
    }

    protected override TimeSpan GetIdleTimeout() =>
        _optionsMonitor.Get(_name).AgentIdleTimeout;
}
