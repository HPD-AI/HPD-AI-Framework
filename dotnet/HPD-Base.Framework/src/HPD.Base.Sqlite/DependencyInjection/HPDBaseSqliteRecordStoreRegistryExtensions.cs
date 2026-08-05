using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite;
/// <summary>Provides explicit HPD.BASE Runtime store-registry registration for the SQLite store.</summary>
public static class HPDBaseSqliteRecordStoreRegistryExtensions
{
    /// <summary>Performs add HPDBase Sqlite Store.</summary>
    public static void AddHPDBaseSqliteStore(this IRecordStoreRegistry registry, SqliteRecordStore store, HPDBaseSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        registry.Add(new RecordStoreRegistration { StoreId = options.StoreId, Store = store, CollectionIds = options.Collections.Select(static collection => collection.Id).ToArray(), HealthRefs = options.ContributeHealth ? [new HealthRefDescriptor { Id = options.HealthRefId, Scope = HealthScope.Store, TargetRef = options.StoreId, Visibility = VisibilityLevel.Admin }] : null, DiagnosticRefs = options.ContributeDiagnostics ? [new DiagnosticRefDescriptor { Id = options.DiagnosticRefId, Visibility = VisibilityLevel.Admin }] : null });
    }

    /// <summary>Performs add HPDBase Sqlite Store.</summary>
    public static void AddHPDBaseSqliteStore(this IRecordStoreRegistry registry, IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(provider);
        registry.AddHPDBaseSqliteStore(provider.GetRequiredService<SqliteRecordStore>(), provider.GetRequiredService<IOptions<HPDBaseSqliteOptions>>().Value);
    }
}
