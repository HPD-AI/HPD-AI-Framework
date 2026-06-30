using HPD.Base.Events;
using HPD.Base.Runtime.Builder;
using HPD.Base.Runtime.Capabilities;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Events;
using HPD.Base.Runtime.Health;
using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Policy;
using HPD.Base.Runtime.Policy.Admin;
using HPD.Base.Runtime.Query;
using HPD.Base.Runtime.Results;
using HPD.Base.Runtime.Schema;
using HPD.Base.Runtime.Serialization;
using HPD.Base.Runtime.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Runtime.DependencyInjection;

public static class HPDBaseRuntimeServiceCollectionExtensions
{
    public static IHPDBaseRuntimeBuilder AddHPDBaseRuntime(
        this IServiceCollection services,
        Action<HPDBaseRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = HPDBaseRuntimeOptions.CreateDefault();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseRuntimeOptions>>(Options.Create(options));
        services.TryAddSingleton<IOptions<HPDBasePolicyAdminOptions>>(Options.Create(new HPDBasePolicyAdminOptions()));
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorValidator, DefaultBaseDescriptorValidator>());
        services.TryAddSingleton<IBaseDescriptorRegistry, DefaultBaseDescriptorRegistry>();
        services.TryAddSingleton<IBaseDescriptorProvider, DefaultBaseDescriptorProvider>();
        services.TryAddSingleton<IBaseSchemaProvider, DefaultBaseSchemaProvider>();
        services.TryAddSingleton<IBaseSchemaValidator, DefaultBaseSchemaValidator>();
        services.TryAddSingleton<IBaseCapabilityProvider, DefaultBaseCapabilityProvider>();
        services.TryAddSingleton<IBaseCapabilityValidator, DefaultBaseCapabilityValidator>();
        services.TryAddSingleton<IRecordStoreRegistry, DefaultRecordStoreRegistry>();
        services.TryAddSingleton<IRecordStoreResolver, DefaultRecordStoreResolver>();
        services.TryAddSingleton<IBaseQueryValidator, DefaultBaseQueryValidator>();
        services.TryAddSingleton<IBasePolicyOrchestrator, DefaultBasePolicyOrchestrator>();
        services.TryAddSingleton<BasePolicyExplainRedactor>();
        services.TryAddSingleton<IBasePolicyExplainService, DefaultBasePolicyExplainService>();
        services.TryAddSingleton<IBaseRecordRedactor, DefaultBaseRecordRedactor>();
        services.TryAddSingleton<IBaseRecordRuntime, DefaultBaseRecordRuntime>();
        services.TryAddSingleton<IBaseHealthProvider, DefaultBaseHealthProvider>();
        services.TryAddSingleton<IBaseDiagnosticProvider, DefaultBaseDiagnosticProvider>();
        services.TryAddSingleton<IBaseJsonTypeInfoResolverComposer, DefaultBaseJsonTypeInfoResolverComposer>();
        services.TryAddSingleton<IBaseJsonOptionsProvider, DefaultBaseJsonOptionsProvider>();
        services.TryAddSingleton<IBaseResultFactory, DefaultBaseResultFactory>();
        services.TryAddSingleton<IBaseResultNormalizer, DefaultBaseResultNormalizer>();
        services.TryAddSingleton<IBaseOperationalFailureMapper, DefaultBaseOperationalFailureMapper>();
        services.TryAddSingleton<IBaseResultRedactor, DefaultBaseResultRedactor>();
        services.TryAddSingleton<IBaseEventPublisher, NoOpBaseEventPublisher>();
        services.TryAddSingleton<IBaseEventEnvelopeFactory, DefaultBaseEventEnvelopeFactory>();
        services.TryAddSingleton<IBaseEventDispatcher, DefaultBaseEventDispatcher>();
        services.TryAddSingleton<IHPDBaseRuntime, DefaultHPDBaseRuntime>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, PolicyAdminDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, PolicyAdminHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, PolicyAdminHealthContributor>());

        return new HPDBaseRuntimeBuilder(services, options);
    }
}
