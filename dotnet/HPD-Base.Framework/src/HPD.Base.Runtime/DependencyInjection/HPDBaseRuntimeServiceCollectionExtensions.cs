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
using HPD.Events.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Runtime.DependencyInjection;

public static class HPDBaseRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Configures HPD.BASE runtime policy handling to fail closed when no evaluator
    /// exists or when every evaluator abstains.
    /// </summary>
    /// <param name="builder">The runtime builder returned by <see cref="AddHPDBaseRuntime(IServiceCollection, Action{HPDBaseRuntimeOptions}?)"/>.</param>
    /// <returns>The same <see cref="IHPDBaseRuntimeBuilder"/> for fluent chaining.</returns>
    public static IHPDBaseRuntimeBuilder UseFailClosedPolicy(this IHPDBaseRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Options.AllowPolicyAbstainAsAllowForDevelopment = false;
        return builder;
    }

    /// <summary>
    /// Allows policy abstain results to be treated as allowed for development-only hosts.
    /// Production and control-plane hosts should keep the default fail-closed behavior.
    /// </summary>
    /// <param name="builder">The runtime builder returned by <see cref="AddHPDBaseRuntime(IServiceCollection, Action{HPDBaseRuntimeOptions}?)"/>.</param>
    /// <returns>The same <see cref="IHPDBaseRuntimeBuilder"/> for fluent chaining.</returns>
    public static IHPDBaseRuntimeBuilder UseDevelopmentPolicyAbstainAsAllow(this IHPDBaseRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Options.AllowPolicyAbstainAsAllowForDevelopment = true;
        return builder;
    }

    /// <summary>
    /// Registers the core HPD.BASE runtime services.
    /// </summary>
    /// <param name="services">The service collection to register runtime services into.</param>
    /// <param name="configure">Optional runtime configuration applied before services are registered.</param>
    /// <returns>An HPD.BASE runtime builder for registering stores and host integrations.</returns>
    public static IHPDBaseRuntimeBuilder AddHPDBaseRuntime(
        this IServiceCollection services,
        Action<HPDBaseRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = HPDBaseRuntimeOptions.CreateDefault();
        configure?.Invoke(options);
        if (options.Events.PostCommitWorkTimeout < TimeSpan.FromMilliseconds(10)
            || options.Events.PostCommitWorkTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                "Post-commit work timeout must be between 10 milliseconds and 1 minute.");
        }

        services.AddHPDEvents();
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
        services.TryAddSingleton<IBaseEventPublisher, HPDEventsBaseEventPublisher>();
        services.TryAddSingleton<IBaseEventFactory, DefaultBaseEventFactory>();
        services.TryAddSingleton<IBaseEventDispatcher, DefaultBaseEventDispatcher>();
        services.TryAddSingleton<IHPDBaseRuntime, DefaultHPDBaseRuntime>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, PolicyAdminDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, PolicyAdminHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, PolicyAdminHealthContributor>());

        return new HPDBaseRuntimeBuilder(services, options);
    }
}
