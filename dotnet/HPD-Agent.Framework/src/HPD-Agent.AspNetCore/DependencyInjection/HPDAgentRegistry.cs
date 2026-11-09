using System.Collections.Concurrent;
using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HostingAgentFactory = HPD.Agent.Hosting.Configuration.IAgentFactory;
using HPD.Agent.Serialization;

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
        var optionsMonitor = _serviceProvider.GetRequiredService<IOptionsMonitor<HPDAgentHostOptions>>();
        var options = optionsMonitor.Get(name);
        var applicationComposition = _serviceProvider.GetRequiredService<HPDApplicationComposition>();
        var eventComposition = options.EventComposition ?? applicationComposition.Events;
        var inputComposition = options.InputComposition ?? applicationComposition.Inputs;

        var sessionConfigured = options.SessionStore is not null || options.SessionStorePath is not null;
        var agentConfigured = options.AgentStore is not null;
        var contentConfigured = options.ContentStore is not null;

        ISessionStore sessionStore = options.SessionStore
            ?? (options.SessionStorePath != null
                ? new FileSessionStore(options.SessionStorePath, eventComposition.Codec)
                : new InMemorySessionStore(eventComposition.Codec));
        if (!ReferenceEquals(sessionStore.EventCodec, eventComposition.Codec))
            throw new InvalidOperationException(
                $"Hosted store codec '{sessionStore.EventCodec.Digest}' differs from application codec '{eventComposition.Digest}'.");
        IAgentStore agentStore = options.AgentStore ?? new InMemoryAgentStore();
        IContentStore contentStore = options.ContentStore ??= new InMemoryContentStore();
        var storage = new HPDAgentStorageInfo(
            new HPDAgentStoreInfo(
                sessionStore.PersistenceCapability,
                sessionConfigured,
                UsingDefaultFallback: !sessionConfigured),
            new HPDAgentStoreInfo(
                agentStore.PersistenceCapability,
                agentConfigured,
                UsingDefaultFallback: !agentConfigured),
            new HPDAgentStoreInfo(
                contentStore.PersistenceCapability == ContentStorePersistenceCapability.RestartDurable
                    ? StorePersistenceCapability.RestartDurable
                    : StorePersistenceCapability.Ephemeral,
                contentConfigured,
                UsingDefaultFallback: !contentConfigured));

        var agentFactory = _serviceProvider.GetService<HostingAgentFactory>();

        var sessionManager = new AspNetCoreSessionManager(sessionStore, optionsMonitor, name);
        var agentManager = new AspNetCoreAgentManager(
            agentStore,
            sessionManager,
            optionsMonitor,
            _serviceProvider,
            name,
            contentStore,
            agentFactory,
            eventComposition);

        var hostingServices = new HPDAgentHostingServices(
            new AgentSessionService(
                sessionManager,
                agentManager,
                string.IsNullOrWhiteSpace(options.DefaultAgentId)
                    ? string.IsNullOrWhiteSpace(name) ? "default" : name
                    : options.DefaultAgentId),
            new AgentThreadService(sessionManager, agentManager),
            new AgentThreadExecutionService(sessionManager, agentManager),
            new AgentContentService(sessionManager, contentStore),
            new AgentDefinitionService(agentManager),
            new AgentMiddlewareResponseService(sessionManager, agentManager),
            new AgentStreamingService(sessionManager, agentManager, inputComposition: inputComposition));

        return new HPDAgentPair(
            agentManager,
            sessionManager,
            hostingServices,
            eventComposition,
            inputComposition,
            storage);
    }
}

/// <summary>Holds the paired managers for one named agent registration.</summary>
internal record HPDAgentPair(
    AspNetCoreAgentManager AgentManager,
    AspNetCoreSessionManager SessionManager,
    HPDAgentHostingServices HostingServices,
    AgentEventComposition EventComposition,
    AgentInputComposition InputComposition,
    HPDAgentStorageInfo Storage);
