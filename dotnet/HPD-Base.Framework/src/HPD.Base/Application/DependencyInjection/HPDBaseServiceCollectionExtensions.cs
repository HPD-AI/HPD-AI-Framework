using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base;

/// <summary>
/// Registers the typed HPD.BASE application API.
/// </summary>
public static class HPDBaseServiceCollectionExtensions
{
    /// <summary>Registers a complete validated HPD.BASE application host.</summary>
    public static IServiceCollection AddHPDBase(
        this IServiceCollection services,
        Action<HPDBaseBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new HPDBaseBuilder(services);
        configure(builder);
        builder.Build();
        services.AddHPDBaseApplicationCore();
        return services;
    }

    /// <summary>
    /// Registers principal-bound application sessions over the canonical Runtime.
    /// </summary>
    private static IServiceCollection AddHPDBaseApplicationCore(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IBaseSessionFactory, DefaultBaseSessionFactory>();
        return services;
    }
}
