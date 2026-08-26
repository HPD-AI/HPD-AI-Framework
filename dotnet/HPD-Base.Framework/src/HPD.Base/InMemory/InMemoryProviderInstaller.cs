using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace HPD.Base;

internal sealed class InMemoryProviderInstaller(HPDBaseInMemoryStoreOptions configured) : IHPDBaseStoreInstaller
{
    internal static HPDBaseStoreProvider Create(Action<HPDBaseInMemoryStoreOptions>? configure)
    {
        var configured = new HPDBaseInMemoryStoreOptions();
        configure?.Invoke(configured);
        HPDBaseInMemoryStoreOptions owned = HPDBaseInMemoryServiceCollectionExtensions.Clone(configured);
        BaseActivationProviderCapability activation = InMemoryRecordStore.CreateActivationCapability(owned);
        return HPDBaseStoreProviderFactory.Create(new BaseStoreProviderDescriptor
        {
            Kind = "inmemory",
            ProtocolVersion = HPDBaseStoreProviderFactory.ProtocolVersion,
            Capabilities = BaseStoreProviderCapabilities.Records |
                BaseStoreProviderCapabilities.AtomicMutations |
                BaseStoreProviderCapabilities.RelationalExecution |
                BaseStoreProviderCapabilities.CoLocatedVectors |
                BaseStoreProviderCapabilities.CoLocatedTextSearch,
            RegistrationIds = ["inmemory.records"],
            RelationalReads = InMemoryRecordStore.CreateRelationalCapability(),
            SubjectReferences = BaseSubjectProviderCapabilities.BuiltIn,
            SubjectLifecycle = BaseSubjectLifecycleProviderCapabilities.BuiltIn,
            SubjectRetirement = BaseSubjectRetirementProviderCapabilities.BuiltIn,
            ModuleMutations = new BaseModuleMutationCapability
            {
                Supported = true, SerializableExecution = true, DurableReceipts = true,
                GenerationCells = true, AtomicRecordAndGenerationCommit = true,
                MaximumLimits = BaseModuleMutationPlatform.MaximumLimits,
            },
            TextSearch = BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional),
            Activations = activation,
            SemanticActivations = BaseSemanticActivationCapabilityContract.BuiltIn(durable: false),
            SemanticActivationCertification = SemanticCertificationProfile(activation),
        }, new InMemoryProviderInstaller(owned));
    }

    private static BaseSemanticActivationCertificationProfile SemanticCertificationProfile(BaseActivationProviderCapability activation) =>
        SealSemanticCertification("hpd.base.inMemory.semanticActivations", "1", "inmemory",
            BaseSemanticActivationCapabilityContract.BuiltIn(durable: false), ModuleCapability(),
            activation);

    private static BaseSemanticActivationCertificationProfile SealSemanticCertification(
        string providerId, string providerVersion, string kind, BaseSemanticActivationCapability semantic,
        BaseModuleMutationCapability module, BaseActivationProviderCapability activation)
    {
        BaseSemanticActivationCertificationSubject subject = BaseSemanticActivationCertificationContract.CreateSubject(
            providerId, providerVersion, kind, HPDBaseStoreProviderFactory.ProtocolVersion, semantic, module, activation);
        return BaseSemanticActivationCertificationContract.SealSuccessfulReport(
            BaseSemanticActivationBuiltInCertification.LoadFrozenExecutedReport(subject, semantic), semantic, module, activation);
    }

    private static BaseModuleMutationCapability ModuleCapability() => new()
    {
        Supported = true, SerializableExecution = true, DurableReceipts = true, GenerationCells = true,
        AtomicRecordAndGenerationCommit = true, MaximumLimits = BaseModuleMutationPlatform.MaximumLimits,
    };

    public HPDBaseStoreRegistrationReceipt Configure(HPDBaseStoreInstallationContext context)
    {
        bool hasVectors = context.Collections.SelectMany(static item => item.VectorIndexes ?? []).Any();
        bool hasText = context.Collections.SelectMany(static item => item.TextIndexes ?? []).Any();
        string? storeId = null;
        HPDBaseInMemoryStoreOptions options = HPDBaseInMemoryServiceCollectionExtensions.Clone(configured);
        {
            options.CollectionIds = context.Collections.Select(static item => item.Id).ToArray();
            options.Collections = context.Collections.ToArray();
            options.ExportedSubjects = context.ExportedSubjects.ToArray();
            options.ModuleMutations = context.ModuleMutations.ToArray();
            options.ModuleGenerationCells = context.ModuleGenerationCells.ToArray();
            options.SemanticActivations = context.SemanticActivations.ToArray();
            options.SemanticActivationMigrations = context.SemanticActivationMigrations.ToArray();
            options.SemanticActivationRemovals = context.SemanticActivationRemovals.ToArray();
            options.SemanticActivationApplicationId = context.ApplicationId;
            options.SemanticActivationOwnerGeneration = context.SemanticActivationOwnerGeneration;
            options.SemanticActivationDefinitionSetChecksum = context.SemanticActivationDefinitionSetChecksum.ToArray();
            options.SubjectLifecycleConsumers = context.SubjectLifecycleConsumers.ToArray();
            options.SubjectLifecycleInspectionAuthorities = context.SubjectLifecycleInspectionAuthorities.ToArray();
            options.SubjectRetirementConsumers = context.SubjectRetirementConsumers.ToArray();
            options.SubjectRetirementPolicies = context.SubjectRetirementPolicies.ToArray();
            storeId = options.StoreId;
        }
        context.Services.AddHPDBaseInMemoryStore(options);
        if (hasVectors && !context.Services.Any(static descriptor => descriptor.ServiceType == typeof(BaseExplicitVectorProviderRegistration)))
        {
            context.Services.AddSingleton<InMemoryVectorProvider>();
            context.Services.AddSingleton<IBaseVectorProvider>(static provider => provider.GetRequiredService<InMemoryVectorProvider>());
            context.Services.AddSingleton<IBaseVectorAuthority>(static provider => provider.GetRequiredService<InMemoryVectorProvider>());
            context.Services.AddSingleton<IBaseVectorAdministrationProvider>(static provider => provider.GetRequiredService<InMemoryVectorProvider>());
        }
        if (hasText)
        {
            context.Services.AddSingleton<InMemoryTextProvider>();
            context.Services.AddSingleton<IBaseTextProvider>(static provider => provider.GetRequiredService<InMemoryTextProvider>());
        }
        return context.CreateReceipt(storeId ?? throw new InvalidOperationException("base.store.providerInvalid"));
    }

    public async ValueTask InitializeAsync(HPDBaseStoreInitializationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await context.Services.GetRequiredService<InMemoryRecordStore>().InitializeVectorProjectionAsync(cancellationToken).ConfigureAwait(false);
        context.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(context.Services);
    }
}

internal sealed class BaseExplicitVectorProviderRegistration;
