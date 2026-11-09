// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Base;
using HPD.Agent.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent.Stores.HpdBase;

/// <summary>Registration helpers for HPD Base-backed Agent stores.</summary>
public static class HpdBaseAgentStoreExtensions
{
    /// <summary>Adds the Agent-definition collection to the HPD Base application graph.</summary>
    public static HPDBaseBuilder AddHpdAgentStores(this HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddCollection(HpdBaseAgentDefinition.Collection)
            .AddCollection(HpdBaseSessionRecord.Collection)
            .AddCollection(HpdBaseThreadRecord.Collection)
            .AddCollection(HpdBaseThreadEventBlock.Collection)
            .AddCollection(HpdBaseThreadEventIdentity.Collection)
            .AddCollection(HpdBaseThreadDelta.Collection);
    }

    /// <summary>Registers the HPD Base-backed <see cref="IAgentStore"/>.</summary>
    public static IServiceCollection AddHpdBaseAgentStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAgentStore, HpdBaseAgentStore>();
        return services;
    }

    /// <summary>Registers the HPD Base-backed <see cref="ISessionStore"/>.</summary>
    public static IServiceCollection AddHpdBaseSessionStore(
        this IServiceCollection services,
        AgentEventCodec eventCodec)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(eventCodec);
        services.TryAddSingleton<ISessionStore>(
            provider => new HpdBaseSessionStore(
                provider.GetRequiredService<IBaseSessionFactory>(),
                eventCodec));
        return services;
    }
}
