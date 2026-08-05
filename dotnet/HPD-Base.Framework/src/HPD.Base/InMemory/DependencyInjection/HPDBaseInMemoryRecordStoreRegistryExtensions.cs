using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base;

/// <summary>
/// Provides explicit HPD.BASE Runtime store-registry registration for the InMemory store.
/// </summary>
internal static class HPDBaseInMemoryRecordStoreRegistryExtensions
{
    /// <summary>
    /// Adds an InMemory store registration to the runtime store registry.
    /// </summary>
    /// <param name="registry">The runtime store registry.</param>
    /// <param name="store">The InMemory store instance.</param>
    /// <param name="options">The InMemory options used to shape registration metadata.</param>
    public static void AddHPDBaseInMemoryStore(
        this IRecordStoreRegistry registry,
        InMemoryRecordStore store,
        HPDBaseInMemoryStoreOptions options)
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
    /// Resolves the configured InMemory store and options from a service provider and registers them with the runtime store registry.
    /// </summary>
    /// <param name="registry">The runtime store registry.</param>
    /// <param name="provider">The service provider containing InMemory services.</param>
    public static void AddHPDBaseInMemoryStore(this IRecordStoreRegistry registry, IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(provider);

        var store = provider.GetRequiredService<InMemoryRecordStore>();
        var options = provider.GetRequiredService<IOptions<HPDBaseInMemoryStoreOptions>>().Value;
        registry.AddHPDBaseInMemoryStore(store, options);
    }
}
