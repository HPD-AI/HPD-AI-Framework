using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite;

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
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(HPDBaseSqliteOptions) || descriptor.ServiceType == typeof(IOptions<HPDBaseSqliteOptions>)))
            throw new InvalidOperationException("base.store.authorityAmbiguous");
        var options = new HPDBaseSqliteOptions();
        configure?.Invoke(options);
        options = Clone(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseSqliteOptions>>(Options.Create(options));
        services.TryAddSingleton(provider =>
        {
            BaseTokenProtectionRegistration? tokenRegistration = provider.GetService<BaseTokenProtectionRegistration>();
            return new SqliteRecordStore(
                provider.GetRequiredService<IOptions<HPDBaseSqliteOptions>>().Value,
                provider.GetRequiredService<ILoggerFactory>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                tokenProtector: tokenRegistration?.ExplicitlyConfigured == true
                    ? provider.GetRequiredService<BaseOpaqueTokenProtector>()
                    : null,
                mutationProjectionContributors: provider.GetServices<ISqliteAtomicMutationProjection>());
        });
        services.TryAddSingleton<IRecordStore>(provider => provider.GetRequiredService<SqliteRecordStore>());
        services.TryAddSingleton<IRecordMutationStore>(provider => provider.GetRequiredService<SqliteRecordStore>());
        services.TryAddSingleton<IAtomicRecordStore>(provider => provider.GetRequiredService<SqliteRecordStore>());
        services.TryAddSingleton<IBaseSubjectLifecycleStore>(provider => provider.GetRequiredService<SqliteRecordStore>());
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

    private static HPDBaseSqliteOptions Clone(HPDBaseSqliteOptions value) => new()
    {
        StoreId = new string(value.StoreId.AsSpan()), ModuleId = new string(value.ModuleId.AsSpan()),
        ModuleName = new string(value.ModuleName.AsSpan()), StoreVersion = new string(value.StoreVersion.AsSpan()),
        ConnectionString = value.ConnectionString is null ? null : new string(value.ConnectionString.AsSpan()),
        DataSource = value.DataSource is null ? null : new string(value.DataSource.AsSpan()),
        SchemaPrefix = new string(value.SchemaPrefix.AsSpan()), EnableWal = value.EnableWal,
        BusyTimeout = value.BusyTimeout, CommandTimeout = value.CommandTimeout,
        DefaultPageSize = value.DefaultPageSize, MaxPageSize = value.MaxPageSize,
        MaxFilterDepth = value.MaxFilterDepth, MaxFilterNodes = value.MaxFilterNodes,
        MaxInValues = value.MaxInValues, MaxSortFields = value.MaxSortFields,
        MaxSelectFields = value.MaxSelectFields, AllowClientRequestedIds = value.AllowClientRequestedIds,
        ContributeModuleDescriptor = value.ContributeModuleDescriptor, ContributeCapabilities = value.ContributeCapabilities,
        ContributeHealth = value.ContributeHealth, ContributeDiagnostics = value.ContributeDiagnostics,
        ContributeRelationalDescriptors = value.ContributeRelationalDescriptors, InitializeSQLitePCLRaw = value.InitializeSQLitePCLRaw,
        MutationJournalRetention = value.MutationJournalRetention, MutationJournalMaxEntries = value.MutationJournalMaxEntries,
        MutationJournalMaxReadSize = value.MutationJournalMaxReadSize, MaxTrackedMutationExecutions = value.MaxTrackedMutationExecutions,
        QuarantinedMutationDrainTimeout = value.QuarantinedMutationDrainTimeout, AdministrationEnabled = value.AdministrationEnabled,
        MaxBackupArtifactBytes = value.MaxBackupArtifactBytes, AdministrationAcquisitionTimeout = value.AdministrationAcquisitionTimeout,
        NativeBackupCompletionWait = value.NativeBackupCompletionWait, RestoreStagingTimeout = value.RestoreStagingTimeout,
        IntegrityCheckTimeout = value.IntegrityCheckTimeout, MaxQuarantinedAdministrationExecutions = value.MaxQuarantinedAdministrationExecutions,
        HealthRefId = new string(value.HealthRefId.AsSpan()), DiagnosticRefId = new string(value.DiagnosticRefId.AsSpan()),
        Collections = value.Collections.ToArray(), ExportedSubjects = value.ExportedSubjects.ToArray(),
        ModuleMutations = value.ModuleMutations.ToArray(), ModuleGenerationCells = value.ModuleGenerationCells.ToArray(),
        SubjectLifecycleConsumers = value.SubjectLifecycleConsumers.Select(static item => BaseSubjectLifecycleRegistry.Normalize(item)).ToArray(),
        SubjectLifecycleInspectionAuthorities = value.SubjectLifecycleInspectionAuthorities.Select(static item => item with { }).ToArray(),
    };
}
