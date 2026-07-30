using HPD.Base.Relational.Providers;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Health;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.Descriptors;
using HPD.Base.Sqlite.Health;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite.DependencyInjection;

/// <summary>Adds HPD.BASE SQLite store services to a service collection.</summary>
public static class HPDBaseSqliteServiceCollectionExtensions
{
    /// <summary>Adds one durable SQLite record store and its advertised optional capabilities.</summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configure">An optional direct SQLite options callback.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddHPDBaseSqliteStore(this IServiceCollection services, Action<HPDBaseSqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new HPDBaseSqliteOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseSqliteOptions>>(Options.Create(options));
        services.TryAddSingleton(provider => new SqliteRecordStore(
            provider.GetRequiredService<IOptions<HPDBaseSqliteOptions>>().Value,
            provider.GetRequiredService<ILoggerFactory>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddSingleton<IRecordStore>(provider => provider.GetRequiredService<SqliteRecordStore>());
        services.TryAddSingleton<IRecordMutationStore>(provider => provider.GetRequiredService<SqliteRecordStore>());
        services.TryAddSingleton<IAtomicRecordStore>(provider => provider.GetRequiredService<SqliteRecordStore>());
        services.TryAddSingleton<ITransactionalMutationJournalStore>(provider => provider.GetRequiredService<SqliteRecordStore>());
        if (options.ContributeRelationalDescriptors)
        {
            services.TryAddSingleton<SqliteRelationalDescriptorProvider>();
            services.TryAddSingleton<IRelationalMetadataProvider>(provider => provider.GetRequiredService<SqliteRelationalDescriptorProvider>());
            services.TryAddSingleton<IRelationalCollectionMappingProvider>(provider => provider.GetRequiredService<SqliteRelationalDescriptorProvider>());
            services.TryAddSingleton<IRelationalQueryPlanExplainer>(provider => provider.GetRequiredService<SqliteRelationalDescriptorProvider>());
        }

        if (options.ContributeModuleDescriptor || options.ContributeCapabilities || options.Collections is not null)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, SqliteDescriptorContributor>());
        }

        if (options.ContributeHealth)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, SqliteHealthContributor>());
        }

        if (options.ContributeDiagnostics)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, SqliteDiagnosticContributor>());
        }

        return services;
    }
}
