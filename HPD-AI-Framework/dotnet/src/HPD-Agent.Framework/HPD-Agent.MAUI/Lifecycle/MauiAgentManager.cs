using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Maui;

/// <summary>
/// MAUI-specific implementation of <see cref="AgentManager"/>.
/// Builds <see cref="Agent"/> instances from stored definitions or fallback config.
/// </summary>
public class MauiAgentManager : AgentManager
{
    private readonly MauiSessionManager _sessionManager;
    private readonly IOptionsMonitor<HPDAgentConfig> _optionsMonitor;
    private readonly string _name;
    private readonly IAgentFactory? _agentFactory;

    internal MauiAgentManager(
        IAgentStore agentStore,
        MauiSessionManager sessionManager,
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

    /// <summary>
    /// If no stored definition exists for <paramref name="agentId"/>, synthesizes one from the
    /// current <see cref="HPDAgentConfig"/> so that <see cref="BuildAgentAsync"/> can apply its
    /// config-driven fallback chain. This supports the common MAUI pattern of configuring the
    /// agent via options rather than pre-seeding a definition in the store.
    /// </summary>
    public override async Task<Agent> GetOrBuildAgentAsync(string agentId, CancellationToken ct = default)
    {
        var existing = await GetDefinitionAsync(agentId, ct);
        if (existing == null)
        {
            var opts = _optionsMonitor.Get(_name);
            var syntheticConfig = opts.AgentConfig ?? new AgentConfig();
            await SeedDefinitionAsync(agentId, syntheticConfig, ct);
        }

        return await base.GetOrBuildAgentAsync(agentId, ct);
    }

    protected override async Task<Agent> BuildAgentAsync(StoredAgent stored, CancellationToken ct)
    {
        var opts = _optionsMonitor.Get(_name);

        // Priority 1: IAgentFactory from DI
        if (_agentFactory != null)
            return await _agentFactory.CreateAgentAsync(stored.Id, _sessionManager.Store, ct);

        // Priority 2: stored.Config (user-provided definition)
        // Priority 3: DefaultAgentConfig object
        // Priority 4: DefaultAgentConfigPath file
        // Priority 5: Empty builder (fallback)
        AgentBuilder builder;
        if (stored.Config is { Provider: not null } config)
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
        opts.ConfigureAgent?.Invoke(builder);

        return await builder.BuildAsync(ct);
    }

    protected override TimeSpan GetIdleTimeout() =>
        _optionsMonitor.Get(_name).AgentIdleTimeout;
}
