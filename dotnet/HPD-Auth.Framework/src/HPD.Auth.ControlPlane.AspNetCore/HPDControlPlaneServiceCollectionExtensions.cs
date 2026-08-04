using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authorization;
using HPD.Auth.Core.Audit;

namespace HPD.Auth.ControlPlane;

/// <summary>Registers HPD control-plane security composition.</summary>
public static class HPDControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddHPDControlPlane(
        this IServiceCollection services,
        Action<HPDControlPlaneOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HPDControlPlaneOptions();
        configure(options);
        ControlPlaneContractValidator.ValidateConfiguration(options);

        services.AddSingleton(options);
        services.AddSingleton(static provider =>
            new ControlPlaneRegistry(provider.GetRequiredService<HPDControlPlaneOptions>()));
        services.TryAddSingleton<IAuthenticatedActorProjector, DefaultAuthenticatedActorProjector>();
        services.AddScoped<ControlPlaneCorrelationContext>();
        services.AddScoped<IAuthCorrelationContext>(static provider =>
            provider.GetRequiredService<ControlPlaneCorrelationContext>());
        services.AddProblemDetails();
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler,
            ControlPlaneAuthorizationResultHandler>());
        services.AddHostedService<ControlPlaneStartupValidator>();
        return services;
    }
}
