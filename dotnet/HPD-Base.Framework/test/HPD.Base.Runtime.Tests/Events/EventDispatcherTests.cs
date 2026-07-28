using System.Text.Json;
using HPD.Base.Events;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Events;

public sealed class EventDispatcherTests
{
    [Fact]
    public async Task BestEffortPublishFailurePreservesMutationAndAddsWarning()
    {
        var store = new FakeRecordStore("primary");
        using var provider = Provider(
            store,
            services => services.AddSingleton<IBaseEventPublisher, FailingEventPublisher>());

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Empty(result.Events!);
        Assert.Contains(result.Warnings!, warning => warning.Code == "base.runtime.events.publishFailed");
    }

    [Fact]
    public async Task DisabledEventsPreserveMutationAndAddWarning()
    {
        var store = new FakeRecordStore("primary");
        using var provider = Provider(
            store,
            configureRuntime: options => options.Events.Enabled = false);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Empty(result.Events!);
        Assert.Contains(result.Warnings!, warning => warning.Code == "base.runtime.events.disabled");
    }

    [Fact]
    public async Task RequireEnqueueFailsRuntimeValidationWithoutDurablePublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor());
        services.AddHPDBaseRuntime(options => options.Events.PublishFailureMode = BaseEventPublishFailureMode.RequireEnqueue);
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.events.requireEnqueueUnsupported");
    }

    private static ServiceProvider Provider(
        FakeRecordStore store,
        Action<IServiceCollection>? configureServices = null,
        Action<HPDBaseRuntimeOptions>? configureRuntime = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor());
        services.AddSingleton<IPolicyEvaluator>(new AllowPolicyEvaluator());
        configureServices?.Invoke(services);
        services.AddHPDBaseRuntime(configureRuntime);
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = store.Capabilities.StoreId,
            Store = store,
            CollectionIds = ["items"]
        });
        return provider;
    }

    private static RecordCreateRequest CreateRequest()
    {
        using var document = JsonDocument.Parse("""{"title":"hello"}""");
        return new RecordCreateRequest
        {
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = document.RootElement.Clone()
            }
        };
    }

    private sealed class CollectionContributor : IBaseDescriptorContributor
    {
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
                Operations = new CollectionOperationMatrix
                {
                    Create = true
                }
            });
        }
    }
}
