using System.Text.Json;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Operations;

public sealed class OperationStoreCapabilityGateTests
{
    [Fact]
    public async Task CreateFailsBeforeStoreWhenMutationCapabilityDisallowsCreate()
    {
        var store = new FakeRecordStore("primary", mutation: Mutations(create: false));
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
    public async Task ListFailsBeforeStoreWhenReadCapabilityDisallowsList()
    {
        var store = new FakeRecordStore("primary", read: Reads(list: false));
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            new HPD.Base.RecordQuery(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.store.operationUnsupported", result.Error!.Code);
        Assert.Equal(0, store.ListCalls);
    }

    private static RecordReadCapability Reads(
        bool list = true,
        bool get = true) => new()
        {
            List = list,
            Get = get
        };

    private static RecordMutationCapability Mutations(
        bool create = true,
        bool patch = true,
        bool replace = true,
        bool delete = true) => new()
        {
            Create = create,
            Patch = patch,
            Replace = replace,
            Delete = delete,
            IdAuthority = IdAuthority.Hybrid,
            TimestampAuthority = TimestampAuthority.Runtime,
            Consistency = ConsistencyModel.Strong
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
