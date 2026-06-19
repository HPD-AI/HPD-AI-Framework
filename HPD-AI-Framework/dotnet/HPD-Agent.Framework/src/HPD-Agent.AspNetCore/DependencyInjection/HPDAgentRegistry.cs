using System.Collections.Concurrent;
using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HostingAgentFactory = HPD.Agent.Hosting.Configuration.IAgentFactory;

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
        IContentStore contentStore = options.ContentStore ??= new InMemoryContentStore();

        var agentFactory = _serviceProvider.GetService<HostingAgentFactory>();

        var sessionManager = new AspNetCoreSessionManager(sessionStore, optionsMonitor, name);
        var agentManager = new AspNetCoreAgentManager(
            agentStore,
            sessionManager,
            optionsMonitor,
            _serviceProvider,
            name,
            contentStore,
            agentFactory);

        var hostingServices = new HPDAgentHostingServices(
            new AgentSessionService(sessionManager),
            new AgentThreadService(sessionManager, agentManager),
            new AgentThreadRunService(sessionManager),
            new AgentContentService(sessionManager, contentStore),
            new AgentDefinitionService(agentManager),
            new AgentMiddlewareResponseService(sessionManager, agentManager),
            new AgentStreamingService(sessionManager, agentManager));

        return new HPDAgentPair(
            agentManager,
            sessionManager,
            hostingServices);
    }
}

/// <summary>Holds the paired managers for one named agent registration.</summary>
internal record HPDAgentPair(
    AspNetCoreAgentManager AgentManager,
    AspNetCoreSessionManager SessionManager,
    HPDAgentHostingServices HostingServices);
