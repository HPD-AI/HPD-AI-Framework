using Microsoft.Extensions.DependencyInjection;
namespace HPD.Agent;

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
        string storagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        return services.AddSessionStore(new JsonSessionStore(storagePath));
    }
}
