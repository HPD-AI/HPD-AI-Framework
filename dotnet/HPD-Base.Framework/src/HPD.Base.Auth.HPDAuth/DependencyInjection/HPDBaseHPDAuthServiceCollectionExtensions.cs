using HPD.Base.Auth.HPDAuth.Configuration;
using HPD.Base.Auth.HPDAuth.Descriptors;
using HPD.Base.Auth.HPDAuth.Health;
using HPD.Base.Auth.HPDAuth.Policy;
using HPD.Base.Policy;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Auth.HPDAuth.DependencyInjection;

/// <summary>
/// Registers HPD.Auth adapter services for HPD.BASE.
/// </summary>
public static class HPDBaseHPDAuthServiceCollectionExtensions
{
    /// <summary>
    /// Adds HPD.Auth adapter services for BASE principal, policy, descriptor, health, and diagnostic integration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional adapter configuration callback.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddHPDBaseHPDAuth(
        this IServiceCollection services,
        Action<HPDBaseHPDAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseHPDAuthOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseHPDAuthOptions>>(Options.Create(options));

        services.TryAddSingleton<HPDAuthBaseSubjectMapper>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPolicyEvaluator, HPDAuthBasePolicyEvaluator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, HPDAuthBaseDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, HPDAuthBaseHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, HPDAuthBaseDiagnosticContributor>());

        return services;
    }
}
