using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HPD.Agent.AspNetCore.RuntimeExtensions;

/// <summary>Hydrates persisted runtime schemas before the host begins serving traffic.</summary>
public sealed class RuntimeAgentExtensionHydrationHostedService(
    RuntimeAgentExtensionRegistry registry,
    IRuntimeAgentExtensionSchemaStore store) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await registry.HydrateAsync(store, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Registration helpers for runtime extension startup.</summary>
public static class RuntimeAgentExtensionHostingExtensions
{
    public static IServiceCollection AddHpdRuntimeAgentExtensionHydration(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<RuntimeAgentExtensionHydrationHostedService>();
        return services;
    }
}
