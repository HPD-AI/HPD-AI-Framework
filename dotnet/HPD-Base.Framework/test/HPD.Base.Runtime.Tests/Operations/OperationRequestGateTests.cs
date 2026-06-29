using System.Text.Json;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Operations;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Operations;

public sealed class OperationRequestGateTests
{
    [Fact]
    public async Task IdempotencyKeyFailsBeforeCreateStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest() with { IdempotencyKey = "idem_1" },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.create.idempotencyUnsupported", result.Error!.Code);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task RequestedIdFailsWhenStoreIdAuthorityIsNotClientOrHybrid()
    {
        var store = new FakeRecordStore("primary", new CrudCapability
        {
            List = true,
            Get = true,
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            IdAuthority = IdAuthority.Store
        });
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest() with { RequestedId = new RecordId("client_1") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.create.requestedIdUnsupported", result.Error!.Code);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task EmptyGetRecordIdFailsBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().GetAsync(
            "items",
            new RecordId(" "),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Get),
            CancellationToken.None);

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.recordId.invalid", result.Error!.Code);
        Assert.Equal(0, store.GetCalls);
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
}
