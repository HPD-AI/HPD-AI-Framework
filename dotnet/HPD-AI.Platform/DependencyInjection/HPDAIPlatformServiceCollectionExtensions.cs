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
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(StudioShellContract)))
            services.AddSingleton(StudioShellContract.Current);
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(StudioShellAssetGraph)))
            services.AddSingleton<StudioShellAssetGraph>();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(StudioRuntimeLeaseRegistry)))
            services.AddSingleton<StudioRuntimeLeaseRegistry>();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(StudioLateWorkRegistry)))
            services.AddSingleton<StudioLateWorkRegistry>();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(StudioCommandAuthorityRegistry)))
            services.AddSingleton<StudioCommandAuthorityRegistry>();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(StudioFrameworkEndpointSurfaceCatalog)))
            services.AddSingleton<StudioFrameworkEndpointSurfaceCatalog>();
        StudioEditionAssetCatalog editionAssets = services
            .Where(static descriptor => descriptor.ServiceType == typeof(StudioEditionAssetCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<StudioEditionAssetCatalog>().SingleOrDefault() ?? new StudioEditionAssetCatalog();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(StudioEditionAssetCatalog)))
        {
            services.AddSingleton(editionAssets);
            services.AddSingleton<StudioEditionAssetCatalogProvider>();
        }
        StudioContributionCatalog catalog = services
            .Where(static descriptor => descriptor.ServiceType == typeof(StudioContributionCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<StudioContributionCatalog>().SingleOrDefault() ?? new StudioContributionCatalog();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(StudioContributionCatalog)))
        {
            services.AddSingleton(catalog);
            services.AddSingleton(serviceProvider => new StudioApplicationGraphProvider(serviceProvider, catalog));
            services.AddSingleton<StudioRuntimeCatalog>();
        }
        return new HPDAIPlatformBuilder(services, catalog, editionAssets);
    }
}

/// <summary>Builds one explicit immutable Studio application graph.</summary>
public sealed class HPDAIPlatformBuilder
{
    internal HPDAIPlatformBuilder(IServiceCollection services, StudioContributionCatalog studioContributions,
        StudioEditionAssetCatalog editionAssets)
    { Services = services ?? throw new ArgumentNullException(nameof(services)); StudioContributions = studioContributions ?? throw new ArgumentNullException(nameof(studioContributions));
      EditionAssets = editionAssets ?? throw new ArgumentNullException(nameof(editionAssets)); }

    /// <summary>Gets application services used for explicit framework registrations.</summary>
    public IServiceCollection Services { get; }
    internal StudioContributionCatalog StudioContributions { get; }
    internal StudioEditionAssetCatalog EditionAssets { get; }

    /// <summary>Adds one authorization-neutral first-party module to the host's explicit edition asset universe.</summary>
    public HPDAIPlatformBuilder AddStudioEditionModuleAsset(StudioEditionModuleAssetContribution contribution)
    { EditionAssets.Add(contribution); return this; }
}
