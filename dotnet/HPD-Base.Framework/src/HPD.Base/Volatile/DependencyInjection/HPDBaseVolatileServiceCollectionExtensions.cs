using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base;

/// <summary>
/// Adds HPD.BASE Volatile store services to a service collection.
/// </summary>
internal static class HPDBaseVolatileServiceCollectionExtensions
{
    internal static IHPDBaseRuntimeBuilder AddHPDBaseVolatileStore(
        this IHPDBaseRuntimeBuilder builder,
        Action<HPDBaseVolatileStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseVolatileStore(configure);
        return builder;
    }

    /// <summary>
    /// Registers the HPD.BASE Volatile store and its descriptor, health, and diagnostic contributors.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional options callback.</param>
    /// <returns>The same service collection.</returns>
    internal static IServiceCollection AddHPDBaseVolatileStore(
        this IServiceCollection services,
        Action<HPDBaseVolatileStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseVolatileStoreOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseVolatileStoreOptions>>(Options.Create(options));
        services.TryAddSingleton(provider => new VolatileRecordStore(provider.GetRequiredService<IOptions<HPDBaseVolatileStoreOptions>>()));
        services.TryAddSingleton<IRecordStore>(provider => provider.GetRequiredService<VolatileRecordStore>());
        services.TryAddSingleton<IRecordMutationStore>(provider => provider.GetRequiredService<VolatileRecordStore>());
        services.TryAddSingleton<IAtomicRecordStore>(provider => provider.GetRequiredService<VolatileRecordStore>());
        services.TryAddSingleton<IStreamingRecordStore>(provider => provider.GetRequiredService<VolatileRecordStore>());

        if (options.ContributeModuleDescriptor || options.ContributeCapabilities || options.Collections is not null)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, VolatileDescriptorContributor>());
        }

        if (options.ContributeHealth)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, VolatileHealthContributor>());
        }

        if (options.ContributeDiagnostics)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, VolatileDiagnosticContributor>());
        }

        return services;
    }
}
