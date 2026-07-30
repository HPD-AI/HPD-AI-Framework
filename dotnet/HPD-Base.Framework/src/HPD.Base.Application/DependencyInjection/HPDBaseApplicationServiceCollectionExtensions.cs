using HPD.Base.Application.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base.Application.DependencyInjection;

/// <summary>
/// Registers the typed HPD.BASE application API.
/// </summary>
public static class HPDBaseApplicationServiceCollectionExtensions
{
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
