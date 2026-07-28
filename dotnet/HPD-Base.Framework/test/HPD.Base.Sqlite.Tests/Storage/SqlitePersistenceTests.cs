using FluentAssertions;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task FileBackedStorePersistsAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-persist-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new HPDBaseSqliteOptions { DataSource = path, CollectionIds = ["items"] };
            var first = SqliteTestFactory.Create(options);
            var create = await first.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload("durable") }, Operation(BaseOperationKind.Create));
            create.Status.Should().Be(OperationStatus.Created);

            var second = SqliteTestFactory.Create(options);
            var get = await second.GetAsync(Collection(), new RecordId("one"), Operation(BaseOperationKind.Get));
            get.Status.Should().Be(OperationStatus.Ok);
            get.Value!.Payload.Fields!["title"].GetString().Should().Be("durable");
        }
        finally
        {
            DeleteFiles(path);
        }
    }

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };

    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };

    private static RecordPayload Payload(string title)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }

    private static void DeleteFiles(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }
}
