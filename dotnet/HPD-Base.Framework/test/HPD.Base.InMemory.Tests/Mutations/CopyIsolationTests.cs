using HPD.Base.InMemory.Tests.TestDoubles;

namespace HPD.Base.InMemory.Tests.Mutations;

public sealed class CopyIsolationTests
{
    [Fact]
    public async Task ReturnedEnvelopeMutationDoesNotMutateStoredState()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();

        var create = await store.CreateAsync(
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "original")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));
        create.Value!.Payload.Fields!["title"] = Json("mutated");

        var get = await store.GetAsync(collection, create.Value.Id, InMemoryTestData.Operation(BaseOperationKind.Get));

        get.Value!.Payload.Fields!["title"].GetString().Should().Be("original");
    }

    [Fact]
    public async Task DisposedJsonDocumentInputIsClonedOnCreate()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
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

        var create = await store.CreateAsync(collection, request, InMemoryTestData.Operation(BaseOperationKind.Create));
        var get = await store.GetAsync(collection, create.Value!.Id, InMemoryTestData.Operation(BaseOperationKind.Get));

        get.Value!.Payload.Fields!["title"].GetString().Should().Be("owned");
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse($"\"{value}\"");
        return document.RootElement.Clone();
    }
}
