using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests;

internal static class TestStoreProvider
{
    internal static HPDBaseStoreProvider Create(
        FakeRecordStore store,
        bool requiredIndexes = false,
        bool relational = false,
        bool schema = false) =>
        HPDBaseStoreProviderFactory.Create(new BaseStoreProviderDescriptor
        {
            Kind = store.Capabilities.StoreId,
            Capabilities = BaseStoreProviderCapabilities.Records |
                BaseStoreProviderCapabilities.AtomicMutations |
                (requiredIndexes || schema ? BaseStoreProviderCapabilities.RequiredIndexes : 0) |
                (relational ? BaseStoreProviderCapabilities.RelationalExecution : 0),
            RegistrationIds = [store.Capabilities.StoreId + ".records"],
            SubjectReferences = BaseSubjectProviderCapabilities.BuiltIn,
            SubjectLifecycle = BaseSubjectLifecycleProviderCapabilities.BuiltIn,
            SubjectRetirement = BaseSubjectRetirementProviderCapabilities.BuiltIn,
            ModuleMutations = new BaseModuleMutationCapability
            {
                Supported = true, SerializableExecution = true, DurableReceipts = true,
                GenerationCells = true, AtomicRecordAndGenerationCommit = true,
                MaximumLimits = BaseModuleMutationPlatform.MaximumLimits,
            },
        }, new Installer(store, schema));

    private sealed class Installer(FakeRecordStore store, bool schema) : IHPDBaseStoreInstaller
    {
        public HPDBaseStoreRegistrationReceipt Configure(HPDBaseStoreInstallationContext context)
        {
            context.Services.AddSingleton(store);
            context.Services.AddSingleton<IRecordStore>(store);
            context.Services.AddSingleton<IRecordMutationStore>(store);
            context.Services.AddSingleton<IAtomicRecordStore>(store);
            if (store is IRelationalReadStore relational) context.Services.AddSingleton(relational);
            if (schema && store is IBaseSchemaStore schemaStore) context.Services.AddSingleton(schemaStore);
            context.Services.AddSingleton<IBaseDescriptorContributor>(new Contributor(context.Collections, store.Capabilities.StoreId));
            return context.CreateReceipt(store.Capabilities.StoreId);
        }

        public ValueTask InitializeAsync(HPDBaseStoreInitializationContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Services.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
            {
                StoreId = store.Capabilities.StoreId,
                Store = store,
                CollectionIds = context.Services.GetRequiredService<BaseCollectionRegistry>().Collections.Keys.ToArray(),
            });
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Contributor(IReadOnlyList<CollectionDefinition> collections, string storeId) : IBaseDescriptorContributor
    {
        public string Id => storeId + ".collections";
        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            foreach (CollectionDefinition collection in collections)
                builder.AddCollection(collection with { Store = new StoreAnnotation { StoreId = storeId, Owner = EnforcementOwner.Store } });
        }
    }
}
