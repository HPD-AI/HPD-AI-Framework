using FluentAssertions;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteFileLifecycleTests
{
    [Fact]
    public async Task FileBackedStoreReleasesDatabaseFilesAfterOperation()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-lifecycle-" + Guid.NewGuid().ToString("N") + ".db");
        var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, EnableWal = false });
        var create = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));
        create.Status.Should().Be(OperationStatus.Created);

        File.Delete(path);
        File.Exists(path).Should().BeFalse();
    }

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
    private static RecordPayload Payload()
    {
        using var document = JsonDocument.Parse("""{"title":"life"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }
}
