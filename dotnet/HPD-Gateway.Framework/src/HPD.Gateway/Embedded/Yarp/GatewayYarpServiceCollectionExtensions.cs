using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.ServiceDiscovery;

namespace HPD.Gateway;

internal static class GatewayYarpServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayYarpPublication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(static _ => new HpdProxyConfigProvider());
        services.AddSingleton<IProxyConfigProvider>(static provider => provider.GetRequiredService<HpdProxyConfigProvider>());
        services.AddSingleton(static provider => new GatewayDiscoveryProfileRegistry(provider.GetServices<IGatewayDiscoveryRuntimeProfile>()));
        services.AddSingleton(static provider => new GatewayDestinationResolver(
            provider.GetRequiredService<GatewayDiscoveryProfileRegistry>(),
            provider.GetRequiredService<IConfigValidator>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton(static provider => new GatewayRuntimeApplicationObserver(
            provider.GetRequiredService<GatewayDestinationResolver>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetService<GatewayTrafficAdmissionRegistry>()));
        services.AddSingleton<IGatewayNodeAppliedRuntimeReader>(static provider =>
            provider.GetRequiredService<GatewayRuntimeApplicationObserver>());
        services.AddSingleton(static provider => new HpdConfigChangeListener(
            provider.GetRequiredService<HpdProxyConfigProvider>(),
            provider.GetRequiredService<GatewayRuntimeApplicationObserver>()));
        services.AddSingleton<IConfigChangeListener>(static provider => provider.GetRequiredService<HpdConfigChangeListener>());
        foreach (ServiceDescriptor descriptor in services
            .Where(static descriptor => descriptor.ServiceType == typeof(IDestinationResolver) &&
                descriptor.ImplementationType?.FullName == "Yarp.ReverseProxy.ServiceDiscovery.NoOpDestinationResolver")
            .ToArray())
            services.Remove(descriptor);
        services.AddSingleton<IDestinationResolver>(static provider => provider.GetRequiredService<GatewayDestinationResolver>());
        services.AddSingleton(static provider => new GatewayRuntimePublisher(
            provider.GetRequiredService<HpdProxyConfigProvider>(),
            provider.GetRequiredService<HpdConfigChangeListener>(),
            provider.GetServices<IProxyConfigProvider>(),
            provider.GetRequiredService<GatewayDestinationResolver>(),
            provider.GetRequiredService<GatewayRuntimeApplicationObserver>()));
        services.AddSingleton<IGatewayPublicationObservationReader>(static provider =>
            provider.GetRequiredService<GatewayRuntimePublisher>());
        services.AddSingleton<IHostedService>(static provider =>
            new HpdYarpOwnershipGuard(
                provider.GetRequiredService<GatewayRuntimePublisher>(),
                provider.GetServices<IDestinationResolver>()));
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
        services.AddSingleton<GatewayRuntimePlanner>();
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

internal static class GatewayDiscoveryRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayDiscoveryRuntimeProfile(
        this IServiceCollection services,
        Func<IServiceProvider, IGatewayDiscoveryRuntimeProfile> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddSingleton(factory);
        return services;
    }

    public static IServiceCollection AddHpdGatewayDiscoveryRuntimeProfile(
        this IServiceCollection services,
        IGatewayDiscoveryRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(profile);
        services.AddSingleton<IGatewayDiscoveryRuntimeProfile>(profile);
        return services;
    }
}

internal sealed class HpdYarpOwnershipGuard(
    GatewayRuntimePublisher publisher,
    IEnumerable<IDestinationResolver> destinationResolvers) : IHostedService
{
    private readonly GatewayRuntimePublisher _publisher = publisher;
    private readonly IDestinationResolver[] _destinationResolvers = destinationResolvers.ToArray();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _publisher;
        if (_destinationResolvers.Length != 1 || _destinationResolvers[0] is not GatewayDestinationResolver)
            throw new InvalidOperationException("Managed publication requires exactly one HPD-owned IDestinationResolver.");
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
