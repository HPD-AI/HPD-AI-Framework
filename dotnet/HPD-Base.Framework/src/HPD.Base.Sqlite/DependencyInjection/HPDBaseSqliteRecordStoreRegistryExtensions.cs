using HPD.Base.Descriptors;
using HPD.Base.Runtime.Stores;
using HPD.Base.Sqlite.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite.DependencyInjection;

/// <summary>Provides explicit HPD.BASE Runtime store-registry registration for the SQLite store.</summary>
public static class HPDBaseSqliteRecordStoreRegistryExtensions
{
    public static void AddHPDBaseSqliteStore(this IRecordStoreRegistry registry, SqliteRecordStore store, HPDBaseSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        registry.Add(new RecordStoreRegistration
        {
            StoreId = options.StoreId,
            Store = store,
            CollectionIds = options.CollectionIds,
            HealthRefs = options.ContributeHealth ? [new HealthRefDescriptor { Id = options.HealthRefId, Scope = HPD.Base.Health.HealthScope.Store, TargetRef = options.StoreId, Visibility = VisibilityLevel.Admin }] : null,
            DiagnosticRefs = options.ContributeDiagnostics ? [new DiagnosticRefDescriptor { Id = options.DiagnosticRefId, Visibility = VisibilityLevel.Admin }] : null
        });
    }

    public static void AddHPDBaseSqliteStore(this IRecordStoreRegistry registry, IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(provider);
        registry.AddHPDBaseSqliteStore(
            provider.GetRequiredService<SqliteRecordStore>(),
            provider.GetRequiredService<IOptions<HPDBaseSqliteOptions>>().Value);
    }
}
