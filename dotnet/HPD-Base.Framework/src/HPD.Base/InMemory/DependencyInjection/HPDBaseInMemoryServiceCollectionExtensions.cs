using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base;

/// <summary>
/// Adds HPD.BASE InMemory store services to a service collection.
/// </summary>
internal static class HPDBaseInMemoryServiceCollectionExtensions
{
    internal static IHPDBaseRuntimeBuilder AddHPDBaseInMemoryStore(
        this IHPDBaseRuntimeBuilder builder,
        Action<HPDBaseInMemoryStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseInMemoryStore(configure);
        return builder;
    }

    /// <summary>
    /// Registers the HPD.BASE InMemory store and its descriptor, health, and diagnostic contributors.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional options callback.</param>
    /// <returns>The same service collection.</returns>
    internal static IServiceCollection AddHPDBaseInMemoryStore(
        this IServiceCollection services,
        Action<HPDBaseInMemoryStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(HPDBaseInMemoryStoreOptions) || descriptor.ServiceType == typeof(IOptions<HPDBaseInMemoryStoreOptions>)))
            throw new InvalidOperationException("base.store.authorityAmbiguous");

        var options = new HPDBaseInMemoryStoreOptions();
        configure?.Invoke(options);
        Validate(options);
        options = Clone(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseInMemoryStoreOptions>>(Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOptions<HPDBaseTokenProtectionOptions>>(_ => Options.Create(
            new HPDBaseTokenProtectionOptions
            {
                ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 0,
                    Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
                    IssueNotBefore = DateTimeOffset.UnixEpoch
                }
            }));
        services.TryAddSingleton(new BaseTokenProtectionRegistration(false));
        services.TryAddSingleton<BaseOpaqueTokenProtector>();
        services.TryAddSingleton(provider => new InMemoryRecordStore(
            provider.GetRequiredService<IOptions<HPDBaseInMemoryStoreOptions>>(),
            provider.GetRequiredService<BaseOpaqueTokenProtector>(),
            provider.GetRequiredService<TimeProvider>()));
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

    private static void Validate(HPDBaseInMemoryStoreOptions options)
    {
        if (options.MaxVectorIndexedRecords is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(options.MaxVectorIndexedRecords));
        if (options.MaxVectorBytes is < 1_048_576 or > 2_147_483_648)
            throw new ArgumentOutOfRangeException(nameof(options.MaxVectorBytes));
        if (options.MaxVectorSourceRecordsPerCollection is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(options.MaxVectorSourceRecordsPerCollection));
    }

    private static HPDBaseInMemoryStoreOptions Clone(HPDBaseInMemoryStoreOptions value) => new()
    {
        StoreId = value.StoreId, ModuleId = value.ModuleId, ModuleName = value.ModuleName,
        StoreVersion = value.StoreVersion, CollectionIds = value.CollectionIds.ToArray(),
        Collections = value.Collections?.ToArray(), ExportedSubjects = value.ExportedSubjects.ToArray(), DefaultPageSize = value.DefaultPageSize,
        MaxPageSize = value.MaxPageSize, MaxFilterDepth = value.MaxFilterDepth,
        MaxFilterNodes = value.MaxFilterNodes, MaxSerializedQueryLength = value.MaxSerializedQueryLength,
        MaxInValues = value.MaxInValues, MaxSortFields = value.MaxSortFields,
        MaxSelectFields = value.MaxSelectFields, MaxStreamItems = value.MaxStreamItems,
        AllowClientRequestedIds = value.AllowClientRequestedIds,
        EnableStreamingCapability = value.EnableStreamingCapability,
        MaxVectorIndexedRecords = value.MaxVectorIndexedRecords, MaxVectorBytes = value.MaxVectorBytes,
        MaxVectorSourceRecordsPerCollection = value.MaxVectorSourceRecordsPerCollection,
        ContributeModuleDescriptor = value.ContributeModuleDescriptor,
        ContributeCapabilities = value.ContributeCapabilities, ContributeHealth = value.ContributeHealth,
        ContributeDiagnostics = value.ContributeDiagnostics, HealthRefId = value.HealthRefId,
        DiagnosticRefId = value.DiagnosticRefId,
    };
}
