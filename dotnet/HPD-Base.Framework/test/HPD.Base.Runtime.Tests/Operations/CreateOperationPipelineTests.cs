using System.Text.Json;
using HPD.Base.Events;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Schema;
using HPD.Events;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Operations;

public sealed class CreateOperationPipelineTests
{
    [Fact]
    public async Task CreateWithoutStoreFailsBeforeInvocation()
    {
        using var provider = OperationTestServices.Build();

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CreateInvokesRegisteredStoreAndAttachesEventReference()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Events);
        Assert.Single(result.Events);
        Assert.Equal(1, store.CreateCalls);
    }

    [Fact]
    public async Task CreatePublishesRecordMutationEventToHPDEvents()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);
        var coordinator = provider.GetRequiredService<IEventCoordinator>();
        await using var inbox = coordinator.CreateInbox<BaseRecordMutationEvent>();

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var emitted = await inbox.Reader.ReadAsync(timeout.Token);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.NotNull(result.Events);
        Assert.Equal(result.Events![0].EventId, emitted.EventId);
        Assert.Equal(BaseEventTypes.RecordCreated, emitted.Type);
        Assert.Equal(BaseOperationKind.Create, emitted.Operation);
        Assert.Equal("items", emitted.Resource.CollectionId);
    }

    [Fact]
    public async Task WriteMaskDeniesCreateBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(
            store,
            new ConstrainedPolicyEvaluator(writeMask: new FieldMask
            {
                Mode = FieldMaskMode.IncludeOnly,
                Include = ["title"]
            }));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest("""{"title":"hello","secret":"nope"}"""),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("base.runtime.policy.writeMask.denied", result.Error!.Code);
        Assert.Null(result.Error.Target);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task WriteCheckDeniesCreateBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(
            store,
            new ConstrainedPolicyEvaluator(writeCheck: new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = "ownerId",
                Operator = FilterOperator.Equal,
                Value = new QueryValue
                {
                    Kind = QueryValueKind.String,
                    String = "user-1"
                }
            }));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest("""{"title":"hello","ownerId":"user-2"}"""),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("base.runtime.policy.writeCheck.denied", result.Error!.Code);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task WriteCheckAllowsCreateBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(
            store,
            new ConstrainedPolicyEvaluator(writeCheck: new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = "ownerId",
                Operator = FilterOperator.Equal,
                Value = new QueryValue
                {
                    Kind = QueryValueKind.String,
                    String = "user-1"
                }
            }));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest("""{"title":"hello","ownerId":"user-1"}"""),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(1, store.CreateCalls);
    }

    [Fact]
    public async Task UnsupportedWriteCheckFailsClosedBeforeCreateStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(
            store,
            new ConstrainedPolicyEvaluator(writeCheck: new FilterExpression
            {
                Kind = FilterNodeKind.Extension,
                Name = "host-only"
            }));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.policy.writeCheck.unsupported", result.Error!.Code);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task CreatePassesSchemaValidatedPayloadToStore()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services => services.AddSingleton<IBaseSchemaValidator>(new NormalizingSchemaValidator()));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest("""{"title":"original"}"""),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal("create-normalized", store.LastCreateRequest!.Payload.Fields!["normalized"].GetString());
    }

    private static RecordCreateRequest CreateRequest(string json = """{"title":"hello"}""")
    {
        using var document = JsonDocument.Parse(json);
        return new RecordCreateRequest
        {
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = document.RootElement.Clone()
            }
        };
    }

}
