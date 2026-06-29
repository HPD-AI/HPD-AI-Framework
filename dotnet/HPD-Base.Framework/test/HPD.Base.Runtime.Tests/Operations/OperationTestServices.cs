using HPD.Base.Policy;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Operations;

internal static class OperationTestServices
{
    public static ServiceProvider Build(
        IRecordStore? store = null,
        IPolicyEvaluator? policy = null,
        FieldDefinition[]? fields = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor(fields));
        services.AddSingleton(policy ?? new AllowPolicyEvaluator());
        configureServices?.Invoke(services);
        services.AddHPDBaseRuntime();

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
                Fields = _fields,
                Operations = new CollectionOperationMatrix
                {
                    List = true,
                    Get = true,
                    Create = true,
                    Patch = true,
                    Replace = true,
                    Delete = true
                }
            });
        }
    }
}
