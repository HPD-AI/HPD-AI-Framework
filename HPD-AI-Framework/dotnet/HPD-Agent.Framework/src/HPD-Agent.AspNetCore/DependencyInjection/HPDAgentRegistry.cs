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

        return new HPDAgentPair(agentManager, sessionManager);
    }
}

/// <summary>Holds the paired managers for one named agent registration.</summary>
internal record HPDAgentPair(
    AspNetCoreAgentManager AgentManager,
    AspNetCoreSessionManager SessionManager);
