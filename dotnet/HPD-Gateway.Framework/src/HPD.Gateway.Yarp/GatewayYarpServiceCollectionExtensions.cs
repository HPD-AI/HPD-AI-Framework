using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

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
        services.AddSingleton<IHostedService>(static provider =>
            new HpdYarpOwnershipGuard(provider.GetRequiredService<GatewayYarpPublisher>()));
        return services;
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
