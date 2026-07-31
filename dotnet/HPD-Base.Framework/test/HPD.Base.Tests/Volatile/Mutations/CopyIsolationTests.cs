using HPD.Base.Tests.Volatile.TestDoubles;

namespace HPD.Base.Tests.Volatile.Mutations;

public sealed class CopyIsolationTests
{
    [Fact]
    public async Task ReturnedEnvelopeMutationDoesNotMutateStoredState()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();

        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "original")) },
            VolatileTestData.Operation(BaseOperationKind.Create));
        create.Value!.Payload.Fields!["title"] = Json("mutated");

        var get = await store.GetAsync(collection, create.Value.Id, VolatileTestData.Operation(BaseOperationKind.Get));

        get.Value!.Payload.Fields!["title"].GetString().Should().Be("original");
    }

    [Fact]
    public async Task DisposedJsonDocumentInputIsClonedOnCreate()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        RecordCreateRequest request;
        using (var document = JsonDocument.Parse("""{"title":"owned"}"""))
        {
            request = new RecordCreateRequest
            {
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.Json,
                    Json = document.RootElement.Clone()
                }
            };
        }

        var create = await VolatileMutationTestDriver.CreateAsync(store, collection, request, VolatileTestData.Operation(BaseOperationKind.Create));
        var get = await store.GetAsync(collection, create.Value!.Id, VolatileTestData.Operation(BaseOperationKind.Get));

        get.Value!.Payload.Fields!["title"].GetString().Should().Be("owned");
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse($"\"{value}\"");
        return document.RootElement.Clone();
    }
}
