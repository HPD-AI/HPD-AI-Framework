using Microsoft.Extensions.DependencyInjection;
namespace HPD.Agent;

using HPD.Agent.Serialization;

/// <summary>
/// Extension methods for registering session store services with DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds a session store to the DI container.
    /// </summary>
    public static IServiceCollection AddSessionStore(
        this IServiceCollection services,
        ISessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        services.AddSingleton<ISessionStore>(store);
        return services;
    }

    /// <summary>
    /// Adds a file-based session store to the DI container.
    /// </summary>
    public static IServiceCollection AddSessionStore(
        this IServiceCollection services,
        string storagePath,
        AgentEventCodec eventCodec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentNullException.ThrowIfNull(eventCodec);
        return services.AddSessionStore(new FileSessionStore(storagePath, eventCodec));
    }
}
