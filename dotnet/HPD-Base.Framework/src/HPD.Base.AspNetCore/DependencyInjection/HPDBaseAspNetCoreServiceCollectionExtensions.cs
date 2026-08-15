using HPD.Base.AspNetCore;
using HPD.Base;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

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
        HPDBaseHttpAuthOptionsValidator.ValidateAndFreeze(options.Auth);
        HPDBaseAspNetCoreSnapshot snapshot = HPDBaseAspNetCoreSnapshot.Create(options);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IOptions<HPDBaseAspNetCoreOptions>) ||
            descriptor.ServiceType == typeof(HPDBaseAspNetCoreOptions) || descriptor.ServiceType == typeof(HPDBaseAspNetCoreSnapshot)))
            throw new InvalidOperationException("base.http.options.ambiguous");

        services.AddOptions();
        services.AddSingleton(snapshot);
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, HPDBaseAspNetCoreJsonOptionsSetup>());
        services.TryAddScoped<IBaseHttpPrincipalContextFactory, BaseHttpPrincipalContextFactory>();
        services.TryAddSingleton<DefaultBaseHttpPrincipalMapper>();
        services.TryAddSingleton<IBaseHttpPrincipalMapper>(static provider => provider.GetRequiredService<DefaultBaseHttpPrincipalMapper>());
        services.TryAddScoped<IBaseHttpOperationContextFactory, BaseHttpOperationContextFactory>();
        services.TryAddSingleton<IBaseHttpCorrelationProvider, DefaultBaseHttpCorrelationProvider>();
        services.TryAddSingleton<IBaseHttpResultMapper, BaseHttpResultMapper>();
        services.TryAddSingleton<HPDBaseEndpointFamilySelectionState>();
        services.TryAddSingleton<BaseProblemDetailsFactory>();
        services.TryAddSingleton<IBaseHttpQueryBinder, BaseHttpQueryBinder>();
        services.Replace(ServiceDescriptor.Singleton<IBaseApplicationLifetime, AspNetCoreBaseApplicationLifetime>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, AspNetCoreProjectionDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, HPDBaseApplicationHostedService>());
        services.TryAddSingleton<HPDBaseEndpointInventoryValidator>();
        services.TryAddSingleton<BaseClientGenerationSnapshotBuilder>();
        services.TryAddSingleton<BaseAdministrationStagingCoordinator>();
        services.AddSingleton<IBaseHealthContributor>(provider => provider.GetRequiredService<BaseAdministrationStagingCoordinator>());
        services.AddSingleton<IBaseDiagnosticContributor>(provider => provider.GetRequiredService<BaseAdministrationStagingCoordinator>());
        services.TryAddScoped<BaseRealtimeLiveQueryTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, HPDBaseEndpointInventoryStartupFilter>());

        return services;
    }
}

internal sealed class AspNetCoreBaseApplicationLifetime(
    Microsoft.Extensions.Hosting.IHostApplicationLifetime lifetime) : IBaseApplicationLifetime
{
    /// <summary>Gets the stopping.</summary>
    public CancellationToken Stopping => lifetime.ApplicationStopping;
}

internal sealed class HPDBaseApplicationHostedService(IServiceProvider services)
    : Microsoft.Extensions.Hosting.IHostedService
{
    /// <summary>Executes the start async operation.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IHPDBaseApplication? application = services.GetService<IHPDBaseApplication>();
        if (application is null)
            return;
        OperationResult<BaseApplicationReadiness> result = await application
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess())
            throw new InvalidOperationException("HPD.BASE application initialization failed.");
    }

    /// <summary>Executes the stop async operation.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
