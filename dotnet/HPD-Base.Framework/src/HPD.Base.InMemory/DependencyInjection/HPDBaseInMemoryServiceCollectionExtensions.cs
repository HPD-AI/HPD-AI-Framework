using HPD.Base.InMemory;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.InMemory;

/// <summary>
/// Adds HPD.BASE InMemory store services to a service collection.
/// </summary>
public static class HPDBaseInMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the HPD.BASE InMemory store and its descriptor, health, and diagnostic contributors.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional options callback.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHPDBaseInMemoryStore(
        this IServiceCollection services,
        Action<HPDBaseInMemoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseInMemoryOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseInMemoryOptions>>(Options.Create(options));
        services.TryAddSingleton(provider => new InMemoryRecordStore(provider.GetRequiredService<IOptions<HPDBaseInMemoryOptions>>()));
        services.TryAddSingleton<IRecordStore>(provider => provider.GetRequiredService<InMemoryRecordStore>());
        services.TryAddSingleton<IRecordMutationStore>(provider => provider.GetRequiredService<InMemoryRecordStore>());
        services.TryAddSingleton<IAtomicRecordStore>(provider => provider.GetRequiredService<InMemoryRecordStore>());
        services.TryAddSingleton<IStreamingRecordStore>(provider => provider.GetRequiredService<InMemoryRecordStore>());

        if (options.ContributeModuleDescriptor || options.ContributeCapabilities || options.Collections is not null)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, InMemoryDescriptorContributor>());
        }

        if (options.ContributeHealth)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, InMemoryHealthContributor>());
        }

        if (options.ContributeDiagnostics)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, InMemoryDiagnosticContributor>());
        }

        return services;
    }
}
