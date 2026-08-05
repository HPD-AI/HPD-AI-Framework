using System.Text.Json;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Operations;

public sealed class GetOperationPipelineTests
{
    [Fact]
    public async Task GetReturnsRedactedRecordWhenCandidatePolicyAllows()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(Record("rec_1"));
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().GetAsync(
            "items",
            new RecordId("rec_1"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Get),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(1, store.GetCalls);
    }

    [Fact]
    public async Task PublicGetMapsCandidatePolicyDenialToNotFound()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(Record("rec_1"));
        using var provider = OperationTestServices.Build(store, new DenyExistingRecordPolicyEvaluator());

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().GetAsync(
            "items",
            new RecordId("rec_1"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Get),
            CancellationToken.None);

        Assert.Equal(OperationStatus.NotFound, result.Status);
        Assert.Equal("base.runtime.record.notFound", result.Error!.Code);
        Assert.Equal(1, store.GetCalls);
    }

    private static RecordEnvelope Record(string id)
    {
        using var document = JsonDocument.Parse("""{"title":"hello"}""");
        return new RecordEnvelope
        {
            CollectionId = "items",
            Id = new RecordId(id),
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = document.RootElement.Clone()
            },
            Metadata = new RecordMetadata()
        };
    }
}
