using HPD.Base.AspNetCore.Http;
using HPD.Base.Auth.HPDAuth.AspNetCore.Configuration;
using HPD.Base.Auth.HPDAuth.AspNetCore.Health;
using HPD.Base.Auth.HPDAuth.AspNetCore.Http;
using HPD.Base.Auth.HPDAuth.Configuration;
using HPD.Base.Auth.HPDAuth.DependencyInjection;
using HPD.Base.Auth.HPDAuth.Health;
using HPD.Base.Runtime.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Auth.HPDAuth.AspNetCore.DependencyInjection;

/// <summary>
/// Registers ASP.NET Core services for the HPD.Auth BASE adapter.
/// </summary>
public static class HPDBaseHPDAuthAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds ASP.NET Core principal mapping services for the HPD.Auth BASE adapter.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional ASP.NET adapter configuration callback.</param>
    /// <param name="configureCore">An optional core adapter configuration callback.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddHPDBaseHPDAuthAspNetCore(
        this IServiceCollection services,
        Action<HPDBaseHPDAuthAspNetCoreOptions>? configure = null,
        Action<HPDBaseHPDAuthOptions>? configureCore = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHPDBaseHPDAuth(configureCore);

        var options = new HPDBaseHPDAuthAspNetCoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseHPDAuthAspNetCoreOptions>>(Options.Create(options));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHPDAuthBaseHostIntegrationStatus, HPDAuthBaseAspNetCoreHostIntegrationStatus>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHPDAuthBaseHttpPrincipalEnricher, HPDAuthBaseUserManagerPrincipalEnricher>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHttpPrincipalMapper, HPDAuthBaseHttpPrincipalMapper>());

        return services;
    }

    /// <summary>
    /// Adds ASP.NET Core principal mapping services for the HPD.Auth BASE adapter to a runtime builder.
    /// </summary>
    /// <param name="builder">The BASE runtime builder.</param>
    /// <param name="configure">An optional ASP.NET adapter configuration callback.</param>
    /// <param name="configureCore">An optional core adapter configuration callback.</param>
    /// <returns>The same runtime builder for chaining.</returns>
    public static IHPDBaseRuntimeBuilder AddHPDBaseHPDAuthAspNetCore(
        this IHPDBaseRuntimeBuilder builder,
        Action<HPDBaseHPDAuthAspNetCoreOptions>? configure = null,
        Action<HPDBaseHPDAuthOptions>? configureCore = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseHPDAuthAspNetCore(configure, configureCore);
        return builder;
    }
}
