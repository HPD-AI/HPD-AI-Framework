using FluentAssertions;
using HPD.Base;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteInMemoryModeTests
{
    [Fact]
    public async Task DefaultInMemoryModePersistsAcrossStoreOperations()
    {
        await using var store = SqliteTestFactory.Create();
        var create = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = RecordId.Create("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));
        create.Status.Should().Be(OperationStatus.Created);

        var get = await store.GetAsync(Collection(), RecordId.Create("one"), Operation(BaseOperationKind.Get));
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
