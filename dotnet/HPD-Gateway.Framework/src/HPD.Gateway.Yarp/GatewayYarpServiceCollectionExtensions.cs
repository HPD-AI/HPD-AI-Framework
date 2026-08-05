using System.Collections.Immutable;
using HPD.Gateway.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace HPD.Gateway.Yarp;

public static class GatewayYarpServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayYarpPublication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(static _ => new HpdProxyConfigProvider());
        services.AddSingleton<IProxyConfigProvider>(static provider => provider.GetRequiredService<HpdProxyConfigProvider>());
        services.AddSingleton(static provider => new HpdConfigChangeListener(provider.GetRequiredService<HpdProxyConfigProvider>()));
        services.AddSingleton<IConfigChangeListener>(static provider => provider.GetRequiredService<HpdConfigChangeListener>());
        services.AddSingleton(static provider => new GatewayYarpPublisher(
            provider.GetRequiredService<HpdProxyConfigProvider>(),
            provider.GetRequiredService<HpdConfigChangeListener>(),
            provider.GetServices<IProxyConfigProvider>()));
        services.AddSingleton<IGatewayPublicationObservationReader>(static provider =>
            provider.GetRequiredService<GatewayYarpPublisher>());
        services.AddSingleton<IHostedService>(static provider =>
            new HpdYarpOwnershipGuard(provider.GetRequiredService<GatewayYarpPublisher>()));
        return services;
    }

    public static IServiceCollection AddHpdGatewayYarpMaterialization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var descriptor in services
            .Where(static descriptor => descriptor.ServiceType == typeof(IForwarderHttpClientFactory) && descriptor.ImplementationType == typeof(ForwarderHttpClientFactory))
            .ToArray())
            services.Remove(descriptor);
        services.AddSingleton<IForwarderHttpClientFactory, HpdForwarderHttpClientFactory>();
        services.AddSingleton<GatewayNativeMaterializer>();
        services.AddSingleton<IHostedService>(static provider =>
            new HpdMaterializationOwnershipGuard(provider.GetServices<IForwarderHttpClientFactory>()));
        return services;
    }

    public static ImmutableArray<UpstreamResilienceCapability> GetHpdGatewayResilienceCapabilities(
        this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<GatewayUpstreamResilienceProvider>().Capabilities;
    }
}

internal sealed class HpdYarpOwnershipGuard(GatewayYarpPublisher publisher) : IHostedService
{
    private readonly GatewayYarpPublisher _publisher = publisher;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _publisher;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class HpdMaterializationOwnershipGuard(IEnumerable<IForwarderHttpClientFactory> configuredClientFactories) : IHostedService
{
    private readonly IForwarderHttpClientFactory[] _clientFactories = configuredClientFactories.ToArray();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_clientFactories.Length != 1 || _clientFactories[0] is not HpdForwarderHttpClientFactory)
            throw new InvalidOperationException("Managed materialization requires exactly one HPD-owned IForwarderHttpClientFactory.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
