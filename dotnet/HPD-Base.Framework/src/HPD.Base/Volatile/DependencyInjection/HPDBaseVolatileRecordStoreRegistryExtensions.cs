using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base;

/// <summary>
/// Provides explicit HPD.BASE Runtime store-registry registration for the Volatile store.
/// </summary>
internal static class HPDBaseVolatileRecordStoreRegistryExtensions
{
    /// <summary>
    /// Adds an Volatile store registration to the runtime store registry.
    /// </summary>
    /// <param name="registry">The runtime store registry.</param>
    /// <param name="store">The Volatile store instance.</param>
    /// <param name="options">The Volatile options used to shape registration metadata.</param>
    public static void AddHPDBaseVolatileStore(
        this IRecordStoreRegistry registry,
        VolatileRecordStore store,
        HPDBaseVolatileStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        registry.Add(new RecordStoreRegistration
        {
            StoreId = options.StoreId,
            Store = store,
            CollectionIds = options.CollectionIds,
            HealthRefs = options.ContributeHealth
                ? [new HealthRefDescriptor { Id = options.HealthRefId, Scope = HealthScope.Store, TargetRef = options.StoreId, Visibility = VisibilityLevel.Admin }]
                : null,
            DiagnosticRefs = options.ContributeDiagnostics
                ? [new DiagnosticRefDescriptor { Id = options.DiagnosticRefId, Visibility = VisibilityLevel.Admin }]
                : null
        });
    }

    /// <summary>
    /// Resolves the configured Volatile store and options from a service provider and registers them with the runtime store registry.
    /// </summary>
    /// <param name="registry">The runtime store registry.</param>
    /// <param name="provider">The service provider containing Volatile services.</param>
    public static void AddHPDBaseVolatileStore(this IRecordStoreRegistry registry, IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(provider);

        var store = provider.GetRequiredService<VolatileRecordStore>();
        var options = provider.GetRequiredService<IOptions<HPDBaseVolatileStoreOptions>>().Value;
        registry.AddHPDBaseVolatileStore(store, options);
    }
}
