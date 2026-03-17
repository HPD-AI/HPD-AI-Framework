using System.Collections.Concurrent;
using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.DependencyInjection;

/// <summary>
/// Registry for managing multiple named agent pairs (AgentManager + SessionManager).
/// Replaces the old <c>AgentSessionManagerRegistry</c>.
/// </summary>
internal sealed class HPDAgentRegistry
{
    private readonly ConcurrentDictionary<string, HPDAgentPair> _pairs = new();
    private readonly IServiceProvider _serviceProvider;

    public HPDAgentRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>Get or create the agent/session manager pair for the given name.</summary>
    public HPDAgentPair Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _pairs.GetOrAdd(name, CreatePair);
    }

    private HPDAgentPair CreatePair(string name)
    {
        var optionsMonitor = _serviceProvider.GetRequiredService<IOptionsMonitor<HPDAgentConfig>>();
        var options = optionsMonitor.Get(name);

        ISessionStore sessionStore = options.SessionStore
            ?? (options.SessionStorePath != null ? new JsonSessionStore(options.SessionStorePath) : new InMemorySessionStore());
        IAgentStore agentStore = options.AgentStore ?? new InMemoryAgentStore();

        var agentFactory = _serviceProvider.GetService<IAgentFactory>();

        var sessionManager = new AspNetCoreSessionManager(sessionStore, optionsMonitor, name);
        var agentManager = new AspNetCoreAgentManager(agentStore, sessionManager, optionsMonitor, name, agentFactory);

        // Seed the "default" agent definition so single-agent deployments work out of the box.
        // This is fire-and-forget at startup — failures surface on first stream request.
        _ = SeedDefaultAgentAsync(agentManager, agentStore, options, name);

        return new HPDAgentPair(agentManager, sessionManager);
    }

    private static async Task SeedDefaultAgentAsync(
        AspNetCoreAgentManager agentManager,
        IAgentStore agentStore,
        HPDAgentConfig options,
        string name)
    {
        // If a "default" entry already exists in the store, don't overwrite it.
        var existing = await agentStore.LoadAsync("default");
        if (existing != null)
            return;

        // Seed a minimal StoredAgent so GetOrBuildAgentAsync("default") can succeed.
        // The actual AgentConfig used at build time is determined by BuildAgentAsync priority logic.
        var seed = new StoredAgent
        {
            Id = "default",
            Name = name == Options.DefaultName ? "Default Agent" : name,
            Config = options.DefaultAgentConfig ?? new AgentConfig(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await agentStore.SaveAsync(seed);
    }
}

/// <summary>Holds the paired managers for one named agent registration.</summary>
internal record HPDAgentPair(
    AspNetCoreAgentManager AgentManager,
    AspNetCoreSessionManager SessionManager);
