using HPD.Agent.Hosting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Packages;

public static class HpdPackageManagementServiceCollectionExtensions
{
    public static IServiceCollection AddHPDAgentPackageManagement(
        this IServiceCollection services)
        => services.AddHPDAgentPackageManagement(Options.DefaultName);

    public static IServiceCollection AddHPDAgentPackageManagement(
        this IServiceCollection services,
        string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);

        services.AddOptions<HPDAgentConfig>(name);
        services.TryAddSingleton(sp =>
            new HpdAspNetCorePackageRuntime(
                services,
                sp.GetRequiredService<IOptionsMonitor<HPDAgentConfig>>(),
                sp,
                name));

        return services;
    }
}
