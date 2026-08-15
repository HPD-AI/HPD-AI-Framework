using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Operations;

internal static class OperationTestServices
{
    public static ServiceProvider Build(
        IRecordStore? store = null,
        IPolicyEvaluator? policy = null,
        FieldDefinition[]? fields = null,
        Action<IServiceCollection>? configureServices = null,
        Action<HPDBaseRuntimeOptions>? configureRuntime = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor(fields));
        IPolicyEvaluator evaluator = policy ?? new AllowPolicyEvaluator();
        configureServices?.Invoke(services);
        services.AddHPDBaseRuntime(configureRuntime).UsePolicyAuthority(
            "operation-tests",
            new BasePolicyAuthorityDefinition
            {
                Id = "operation-tests.policy",
                Version = 1,
                OwningModuleId = "operation-tests",
                EvaluatorContractId = "operation-tests.policy-evaluator",
                EvaluatorContractVersion = 1,
                CompositionOrder = 0,
            },
            evaluator);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();

        if (store is not null)
        {
            provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
            {
                StoreId = store.Capabilities.StoreId,
                Store = store,
                CollectionIds = ["items"]
            });
        }

        return provider;
    }

    private sealed class CollectionContributor : IBaseDescriptorContributor
    {
        private readonly FieldDefinition[]? _fields;

        public CollectionContributor(FieldDefinition[]? fields)
        {
            _fields = fields;
        }

        public string Id => "collections";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(new CollectionDefinition
            {
                Id = "items",
                Name = "items",
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve,
                Fields = _fields ??
                [
                    new FieldDefinition { Id = "title", ApplicationName = "title", WireName = "title", Type = BaseFieldTypes.String },
                    new FieldDefinition { Id = "tenantId", ApplicationName = "tenantId", WireName = "tenantId", Type = BaseFieldTypes.String },
                ],
                MutationMode = BaseCollectionMutationMode.Mutable
            });
        }
    }
}
