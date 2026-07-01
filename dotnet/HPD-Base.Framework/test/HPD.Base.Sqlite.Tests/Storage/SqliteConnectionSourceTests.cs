using FluentAssertions;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteConnectionSourceTests
{
    [Fact]
    public async Task DataSourceBuildsFileBackedConnectionString()
    {
        var dataSource = TempPath();
        try
        {
            await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions
            {
                DataSource = dataSource,
                EnableWal = false,
                CollectionIds = ["items"]
            });

            var create = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload("data-source") }, Operation(BaseOperationKind.Create));

            create.Status.Should().Be(OperationStatus.Created);
            File.Exists(dataSource).Should().BeTrue();
        }
        finally
        {
            DeleteFiles(dataSource);
        }
    }

    [Fact]
    public async Task ConnectionStringWinsOverDataSource()
    {
        var connectionStringDataSource = TempPath();
        var ignoredDataSource = TempPath();
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = connectionStringDataSource,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions
            {
                ConnectionString = connectionString,
                DataSource = ignoredDataSource,
                EnableWal = false,
                CollectionIds = ["items"]
            });

            var create = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload("connection-string") }, Operation(BaseOperationKind.Create));

            create.Status.Should().Be(OperationStatus.Created);
            File.Exists(connectionStringDataSource).Should().BeTrue();
            File.Exists(ignoredDataSource).Should().BeFalse();
        }
        finally
        {
            DeleteFiles(connectionStringDataSource);
            DeleteFiles(ignoredDataSource);
        }
    }

    [Fact]
    public async Task AspireStyleConnectionStringIsAcceptedAsExactConnectionString()
    {
        var dataSource = TempPath();
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dataSource,
                Cache = SqliteCacheMode.Shared,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions
            {
                ConnectionString = connectionString,
                EnableWal = false,
                CollectionIds = ["items"]
            });

            var create = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload("aspire") }, Operation(BaseOperationKind.Create));
            var get = await store.GetAsync(Collection(), new RecordId("one"), Operation(BaseOperationKind.Get));

            create.Status.Should().Be(OperationStatus.Created);
            get.Status.Should().Be(OperationStatus.Ok);
            get.Value!.Payload.Fields!["title"].GetString().Should().Be("aspire");
        }
        finally
        {
            DeleteFiles(dataSource);
        }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-connection-" + Guid.NewGuid().ToString("N") + ".db");

    private static CollectionDefinition Collection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };

    private static RecordPayload Payload(string title)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }

    private static void DeleteFiles(string dataSource)
    {
        foreach (var candidate in new[] { dataSource, dataSource + "-wal", dataSource + "-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
