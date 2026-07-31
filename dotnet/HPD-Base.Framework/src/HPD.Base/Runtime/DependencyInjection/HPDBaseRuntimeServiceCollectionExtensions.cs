using HPD.Events.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base;

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
        ValidateMutationOptions(options.Mutations, nameof(configure));

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
        services.TryAddSingleton<IBaseStoreExecutionResolver, DefaultBaseStoreExecutionResolver>();
        services.TryAddSingleton<IBaseQueryValidator, DefaultBaseQueryValidator>();
        services.TryAddSingleton<IBasePolicyOrchestrator, DefaultBasePolicyOrchestrator>();
        services.TryAddSingleton<BasePolicyExplainRedactor>();
        services.TryAddSingleton<IBasePolicyExplainService, DefaultBasePolicyExplainService>();
        services.TryAddSingleton<IBaseRecordRedactor, DefaultBaseRecordRedactor>();
        services.TryAddSingleton<IBaseMutationPostCommitDispatcher, DefaultBaseMutationPostCommitDispatcher>();
        services.TryAddSingleton<IBaseMutationCoordinator, DefaultBaseMutationCoordinator>();
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, BaseMutationDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, PolicyAdminDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, PolicyAdminHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, PolicyAdminHealthContributor>());

        return new HPDBaseRuntimeBuilder(services, options);
    }

    private static void ValidateMutationOptions(
        HPDBaseRuntimeMutationOptions options,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxOperations is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(parameterName, "Maximum mutation operations must be between 1 and 1,000.");
        if (options.MaxCanonicalPayloadBytes is < 1_024 or > 16_777_216)
            throw new ArgumentOutOfRangeException(parameterName, "Maximum canonical mutation payload bytes must be between 1,024 and 16,777,216.");
        if (options.MaxItemIdLength is < 1 or > 256)
            throw new ArgumentOutOfRangeException(parameterName, "Maximum batch item identifier length must be between 1 and 256.");
        if (options.MaxTransactionDuration < TimeSpan.FromMilliseconds(10)
            || options.MaxTransactionDuration > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Maximum transaction duration must be between 10 milliseconds and 60 seconds.");
        }
        if (options.StoreAcquisitionTimeout < TimeSpan.FromMilliseconds(10)
            || options.StoreAcquisitionTimeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Store acquisition timeout must be between 10 milliseconds and 30 seconds.");
        }
        if (options.CommitCompletionTimeout < TimeSpan.FromMilliseconds(10)
            || options.CommitCompletionTimeout > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Commit completion timeout must be between 10 milliseconds and 60 seconds.");
        }
    }
}
