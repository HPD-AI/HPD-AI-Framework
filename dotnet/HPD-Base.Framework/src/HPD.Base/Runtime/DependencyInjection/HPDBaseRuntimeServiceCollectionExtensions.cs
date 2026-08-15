using HPD.Events.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base;
/// <summary>Represents hPDBase Runtime Service Collection Extensions.</summary>
public static class HPDBaseRuntimeServiceCollectionExtensions
{
    /// <summary>Installs one explicit graph-owned policy authority for a low-level runtime host.</summary>
    public static IHPDBaseRuntimeBuilder UsePolicyAuthority(
        this IHPDBaseRuntimeBuilder builder,
        string applicationId,
        BasePolicyAuthorityDefinition definition,
        IPolicyEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(evaluator);
        var authority = new BasePolicyAuthorityBuilder();
        authority.AddPolicy(definition, evaluator);
        builder.Services.AddSingleton(authority.Freeze(applicationId));
        return builder;
    }

    /// <summary>Installs one exact service-resolved graph-owned policy authority for a low-level host.</summary>
    public static IHPDBaseRuntimeBuilder UsePolicyAuthorityFromServices<T>(
        this IHPDBaseRuntimeBuilder builder,
        string applicationId,
        BasePolicyAuthorityDefinition definition)
        where T : class, IPolicyEvaluator
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        var authority = new BasePolicyAuthorityBuilder();
        authority.AddPolicyFactory(definition, typeof(T), static services => services.GetRequiredService<T>());
        builder.Services.AddSingleton(authority.Freeze(applicationId));
        return builder;
    }

    /// <summary>
    /// Configures HPD.BASE runtime policy handling to fail closed when no evaluator
    /// exists or when every evaluator abstains.
    /// </summary>
    /// <param name = "builder">The runtime builder returned by <see cref = "AddHPDBaseRuntime(IServiceCollection, Action{HPDBaseRuntimeOptions}? )"/>.</param>
    /// <returns>The same <see cref = "IHPDBaseRuntimeBuilder"/> for fluent chaining.</returns>
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
    /// <param name = "builder">The runtime builder returned by <see cref = "AddHPDBaseRuntime(IServiceCollection, Action{HPDBaseRuntimeOptions}? )"/>.</param>
    /// <returns>The same <see cref = "IHPDBaseRuntimeBuilder"/> for fluent chaining.</returns>
    public static IHPDBaseRuntimeBuilder UseDevelopmentPolicyAbstainAsAllow(this IHPDBaseRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Options.AllowPolicyAbstainAsAllowForDevelopment = true;
        return builder;
    }

    /// <summary>
    /// Registers the core HPD.BASE runtime services.
    /// </summary>
    /// <param name = "services">The service collection to register runtime services into.</param>
    /// <param name = "configure">Optional runtime configuration applied before services are registered.</param>
    /// <returns>An HPD.BASE runtime builder for registering stores and host integrations.</returns>
    public static IHPDBaseRuntimeBuilder AddHPDBaseRuntime(this IServiceCollection services, Action<HPDBaseRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = HPDBaseRuntimeOptions.CreateDefault();
        configure?.Invoke(options);
        if (options.Events.PostCommitWorkTimeout < TimeSpan.FromMilliseconds(10) || options.Events.PostCommitWorkTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(configure), "Post-commit work timeout must be between 10 milliseconds and 1 minute.");
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
        services.TryAddSingleton(new BaseSubjectContractRegistry([]));
        services.TryAddSingleton<IBaseMutationCoordinator, DefaultBaseMutationCoordinator>();
        services.TryAddSingleton<IBaseSelectionMutationRuntime, DefaultBaseSelectionMutationRuntime>();
        services.TryAddSingleton<IBaseModuleMutationRuntime, DefaultBaseModuleMutationRuntime>();
        services.TryAddSingleton<IBaseRecordRuntime, DefaultBaseRecordRuntime>();
        services.TryAddSingleton(new BaseReadRegistry(new Dictionary<string, IBaseReadRegistration>(StringComparer.Ordinal)));
        services.TryAddSingleton(new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>(StringComparer.Ordinal)));
        services.TryAddSingleton<IBaseRegisteredReadRuntime, DefaultBaseRegisteredReadRuntime>();
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

    /// <summary>Performs validate Mutation Options.</summary>
    private static void ValidateMutationOptions(HPDBaseRuntimeMutationOptions options, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxOperations is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(parameterName, "Maximum mutation operations must be between 1 and 1,000.");
        if (options.MaxCanonicalPayloadBytes is < 1_024 or > 16_777_216)
            throw new ArgumentOutOfRangeException(parameterName, "Maximum canonical mutation payload bytes must be between 1,024 and 16,777,216.");
        if (options.MaxItemIdLength is < 1 or > 256)
            throw new ArgumentOutOfRangeException(parameterName, "Maximum batch item identifier length must be between 1 and 256.");
        if (options.ReceiptLifetime < TimeSpan.FromHours(1) || options.ReceiptLifetime > TimeSpan.FromDays(90))
            throw new ArgumentOutOfRangeException(parameterName, "Receipt lifetime must be between 1 hour and 90 days.");
        if (options.MaxReceiptBytes is < 4_096 or > 16_777_216)
            throw new ArgumentOutOfRangeException(parameterName, "Maximum receipt bytes must be between 4,096 and 16,777,216.");
        if (options.MaxTransactionDuration < TimeSpan.FromMilliseconds(10) || options.MaxTransactionDuration > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Maximum transaction duration must be between 10 milliseconds and 60 seconds.");
        }

        if (options.StoreAcquisitionTimeout < TimeSpan.FromMilliseconds(10) || options.StoreAcquisitionTimeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Store acquisition timeout must be between 10 milliseconds and 30 seconds.");
        }

        if (options.CommitCompletionTimeout < TimeSpan.FromMilliseconds(10) || options.CommitCompletionTimeout > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Commit completion timeout must be between 10 milliseconds and 60 seconds.");
        }
    }
}
