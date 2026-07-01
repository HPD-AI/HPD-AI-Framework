using FluentAssertions;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteInMemoryModeTests
{
    [Fact]
    public async Task DefaultInMemoryModePersistsAcrossStoreOperations()
    {
        await using var store = new SqliteRecordStore();
        var create = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));
        create.Status.Should().Be(OperationStatus.Created);

        var get = await store.GetAsync(Collection(), new RecordId("one"), Operation(BaseOperationKind.Get));
        get.Status.Should().Be(OperationStatus.Ok);
        get.Value!.Payload.Fields!["title"].GetString().Should().Be("memory");
    }

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
    private static RecordPayload Payload()
    {
        using var document = JsonDocument.Parse("""{"title":"memory"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }
}
