using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform;

/// <summary>Registers the shared HPD Studio platform.</summary>
public static class HPDAIPlatformServiceCollectionExtensions
{
    /// <summary>Adds the graph-owned Studio shell and contribution catalog.</summary>
    public static HPDAIPlatformBuilder AddHPDAIPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(BaseStudioShellContract)))
            services.AddSingleton(BaseStudioShellContract.Current);
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(BaseStudioShellAssetGraph)))
            services.AddSingleton<BaseStudioShellAssetGraph>();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(BaseStudioRuntimeLeaseRegistry)))
            services.AddSingleton<BaseStudioRuntimeLeaseRegistry>();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(BaseStudioLateWorkRegistry)))
            services.AddSingleton<BaseStudioLateWorkRegistry>();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(BaseStudioCommandAuthorityRegistry)))
            services.AddSingleton<BaseStudioCommandAuthorityRegistry>();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(BaseStudioFrameworkEndpointSurfaceCatalog)))
            services.AddSingleton<BaseStudioFrameworkEndpointSurfaceCatalog>();
        BaseStudioEditionAssetCatalog editionAssets = services
            .Where(static descriptor => descriptor.ServiceType == typeof(BaseStudioEditionAssetCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<BaseStudioEditionAssetCatalog>().SingleOrDefault() ?? new BaseStudioEditionAssetCatalog();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(BaseStudioEditionAssetCatalog)))
        {
            services.AddSingleton(editionAssets);
            services.AddSingleton<BaseStudioEditionAssetCatalogProvider>();
        }
        BaseStudioContributionCatalog catalog = services
            .Where(static descriptor => descriptor.ServiceType == typeof(BaseStudioContributionCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<BaseStudioContributionCatalog>().SingleOrDefault() ?? new BaseStudioContributionCatalog();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(BaseStudioContributionCatalog)))
        {
            services.AddSingleton(catalog);
            services.AddSingleton(serviceProvider => new BaseStudioApplicationGraphProvider(serviceProvider, catalog));
            services.AddSingleton<BaseStudioRuntimeCatalog>();
        }
        return new HPDAIPlatformBuilder(services, catalog, editionAssets);
    }
}

/// <summary>Builds one explicit immutable Studio application graph.</summary>
public sealed class HPDAIPlatformBuilder
{
    internal HPDAIPlatformBuilder(IServiceCollection services, BaseStudioContributionCatalog studioContributions,
        BaseStudioEditionAssetCatalog editionAssets)
    { Services = services ?? throw new ArgumentNullException(nameof(services)); StudioContributions = studioContributions ?? throw new ArgumentNullException(nameof(studioContributions));
      EditionAssets = editionAssets ?? throw new ArgumentNullException(nameof(editionAssets)); }

    /// <summary>Gets application services used for explicit framework registrations.</summary>
    public IServiceCollection Services { get; }
    internal BaseStudioContributionCatalog StudioContributions { get; }
    internal BaseStudioEditionAssetCatalog EditionAssets { get; }

    /// <summary>Adds one authorization-neutral first-party module to the host's explicit edition asset universe.</summary>
    public HPDAIPlatformBuilder AddStudioEditionModuleAsset(BaseStudioEditionModuleAssetContribution contribution)
    { EditionAssets.Add(contribution); return this; }
}
