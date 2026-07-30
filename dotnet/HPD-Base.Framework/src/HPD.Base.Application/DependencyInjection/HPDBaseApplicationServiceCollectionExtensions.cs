using HPD.Base.Application.Sessions;
using HPD.Base.Application.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base.Application.DependencyInjection;

/// <summary>
/// Registers the typed HPD.BASE application API.
/// </summary>
public static class HPDBaseApplicationServiceCollectionExtensions
{
    /// <summary>Registers a complete validated HPD.BASE application host.</summary>
    public static IServiceCollection AddHPDBase(
        this IServiceCollection services,
        Action<HPDBaseApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new HPDBaseApplicationBuilder(services);
        configure(builder);
        builder.Build();
        services.AddHPDBaseApplication();
        return services;
    }

    /// <summary>
    /// Registers principal-bound application sessions over the canonical Runtime.
    /// </summary>
    public static IServiceCollection AddHPDBaseApplication(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IBaseSessionFactory, DefaultBaseSessionFactory>();
        return services;
    }
}
