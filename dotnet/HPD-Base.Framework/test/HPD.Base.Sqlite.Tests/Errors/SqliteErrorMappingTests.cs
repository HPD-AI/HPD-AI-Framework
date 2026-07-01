using FluentAssertions;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Errors;

public sealed class SqliteErrorMappingTests
{
    [Fact]
    public async Task DuplicateRecordIdMapsToConflict()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-errors-" + Guid.NewGuid().ToString("N") + ".db");
        var store = new SqliteRecordStore(new HPDBaseSqliteOptions { DataSource = path });
        var request = new RecordCreateRequest { RequestedId = new RecordId("dup"), Payload = Payload() };
        (await store.CreateAsync(Collection(), request, Operation(BaseOperationKind.Create))).Status.Should().Be(OperationStatus.Created);

        var duplicate = await store.CreateAsync(Collection(), request, Operation(BaseOperationKind.Create));
        duplicate.Status.Should().Be(OperationStatus.Conflict);
        duplicate.Error!.Conflict!.Kind.Should().Be(ConflictKind.Unique);
    }

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
    private static RecordPayload Payload()
    {
        using var document = JsonDocument.Parse("""{"title":"dup"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }
}
