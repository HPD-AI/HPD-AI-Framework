using System.Text.Json;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Operations;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Operations;

public sealed class OperationStoreCapabilityGateTests
{
    [Fact]
    public async Task CreateFailsBeforeStoreWhenStoreCrudDisallowsCreate()
    {
        var store = new FakeRecordStore("primary", Crud(create: false));
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.store.operationUnsupported", result.Error!.Code);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task ListFailsBeforeStoreWhenStoreCrudDisallowsList()
    {
        var store = new FakeRecordStore("primary", Crud(list: false));
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            new HPD.Base.Query.RecordQuery(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.store.operationUnsupported", result.Error!.Code);
        Assert.Equal(0, store.ListCalls);
    }

    private static CrudCapability Crud(
        bool list = true,
        bool get = true,
        bool create = true,
        bool patch = true,
        bool replace = true,
        bool delete = true) => new()
        {
            List = list,
            Get = get,
            Create = create,
            Patch = patch,
            Replace = replace,
            Delete = delete
        };

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
