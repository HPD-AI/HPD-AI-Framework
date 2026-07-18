using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Session;

/// <summary>
/// Registers platform session routing with the stable agent identity supplied by an adapter.
/// </summary>
public static class PlatformSessionMapperServiceCollectionExtensions
{
    /// <summary>
    /// Registers one mapper whose default thread agent is resolved from the adapter configuration.
    /// </summary>
    public static IServiceCollection TryAddPlatformSessionMapper<TConfig>(
        this IServiceCollection services,
        Func<TConfig, string> resolveAgentId)
        where TConfig : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resolveAgentId);
        services.TryAddSingleton(sp => new PlatformSessionMapper(
            sp.GetRequiredService<global::HPD.Agent.Hosting.Lifecycle.SessionManager>(),
            resolveAgentId(sp.GetRequiredService<IOptions<TConfig>>().Value)));
        return services;
    }
}
