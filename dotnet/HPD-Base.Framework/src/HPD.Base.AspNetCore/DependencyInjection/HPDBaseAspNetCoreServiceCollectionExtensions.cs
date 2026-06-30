using HPD.Base.AspNetCore.Configuration;
using HPD.Base.AspNetCore.Descriptors;
using HPD.Base.AspNetCore.Http;
using HPD.Base.AspNetCore.QueryBinding;
using HPD.Base.AspNetCore.Results;
using HPD.Base.Runtime.Descriptors;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.DependencyInjection;

/// <summary>
/// Extension methods for registering HPD.BASE ASP.NET Core projection services.
/// </summary>
public static class HPDBaseAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds HPD.BASE ASP.NET Core projection services.
    /// </summary>
    /// <remarks>
    /// This method registers HTTP projection services only. Hosts must also register HPD.BASE Runtime and stores explicitly.
    /// </remarks>
    public static IServiceCollection AddHPDBaseAspNetCore(
        this IServiceCollection services,
        Action<HPDBaseAspNetCoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseAspNetCoreOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseAspNetCoreOptions>>(Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, HPDBaseAspNetCoreJsonOptionsSetup>());
        services.TryAddSingleton<IBaseHttpPrincipalContextFactory, BaseHttpPrincipalContextFactory>();
        services.TryAddSingleton<IBaseHttpOperationContextFactory, BaseHttpOperationContextFactory>();
        services.TryAddSingleton<IBaseHttpResultMapper, BaseHttpResultMapper>();
        services.TryAddSingleton<BaseProblemDetailsFactory>();
        services.TryAddSingleton<IBaseHttpQueryBinder, BaseHttpQueryBinder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, AspNetCoreProjectionDescriptorContributor>());

        return services;
    }
}
