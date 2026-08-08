using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base;

/// <summary>Installs provider-neutral vector execution into the unified BASE application builder.</summary>
public static class HPDBaseVectorBuilderExtensions
{
    /// <summary>Installs vector execution exactly once.</summary>
    public static HPDBaseBuilder AddVector(this HPDBaseBuilder builder, Action<HPDBaseVectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(new VectorRuntimeInstaller(configure));
    }

    private sealed class VectorRuntimeInstaller(Action<HPDBaseVectorOptions>? configure) : IHPDBaseBuilderExtension
    {
        public string Id => "vector";
        public bool IsRecordProvider => false;
        public bool SupportsRequiredIndexes => false;

        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
        {
            var configured = new HPDBaseVectorOptions();
            configure?.Invoke(configured);
            configured.Validate();
            var snapshot = new HPDBaseVectorSnapshot(configured.MaxDimensions, configured.MaxTopK, configured.MaxFilterFields, configured.ProviderTimeout, configured.ConsistencyWaitTimeout, configured.ConsistencyTokenLifetime, configured.MaxActiveAndQuarantinedOperations, configured.ShutdownDrainTimeout, configured.AdministrationTimeout, configured.MaxConcurrentRebuilds);
            VectorIndexDefinition[] indexes = collections.SelectMany(static collection => collection.VectorIndexes ?? []).ToArray();
            if (indexes.Any(index => index.Dimensions > snapshot.MaxDimensions || index.FilterFieldIds.Length > snapshot.MaxFilterFields)) throw new InvalidOperationException("A declared vector index exceeds the configured vector limits.");
            services.AddSingleton(snapshot);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IBaseVectorRuntime, DefaultBaseVectorRuntime>();
            services.AddSingleton<IBaseVectorAdministration, DefaultBaseVectorAdministration>();
            services.AddSingleton<IBaseVectorRebuildService>(static provider => (DefaultBaseVectorAdministration)provider.GetRequiredService<IBaseVectorAdministration>());
            services.AddSingleton<IBaseDescriptorContributor, BaseVectorDescriptorContributor>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseVectorHealthContributor>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, BaseVectorHealthContributor>());
        }

        public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!services.GetRequiredService<BaseTokenProtectionRegistration>().ExplicitlyConfigured) throw new InvalidOperationException("base.vector.tokenProtectionRequired: vector execution requires explicitly configured persistent token protection.");
            if (services.GetServices<IBaseVectorProvider>().Count() != 1 || services.GetServices<IBaseVectorAuthority>().Count() != 1) throw new InvalidOperationException("base.vector.providerUnavailable: vector execution requires exactly one provider and one authority implementation.");
            return ValueTask.CompletedTask;
        }
    }
}
