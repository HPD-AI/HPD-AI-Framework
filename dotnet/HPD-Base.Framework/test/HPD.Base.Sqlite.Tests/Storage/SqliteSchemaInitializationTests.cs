using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteSchemaInitializationTests
{
    [Fact]
    public async Task DeclaredFieldsAndIndexesHaveStableTypedPhysicalStorage()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-typed-" + Guid.NewGuid().ToString("N") + ".db");
        var collection = Collection() with
        {
            SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject,
            Fields =
            [
                new FieldDefinition { Id = "item.title", Name = "title", Type = BaseFieldTypes.String, Required = true, Nullable = false },
                new FieldDefinition { Id = "item.rank", Name = "rank", Type = BaseFieldTypes.Integer },
                new FieldDefinition { Id = "item.blob", Name = "blob", Type = BaseFieldTypes.String, Format = "base64", MaximumBytes = 16, Required = true, Nullable = false }
            ],
            Indexes =
            [
                new IndexDefinition
                {
                    Id = "item.by-rank", Name = "by-rank", CollectionId = "items", Kind = IndexKind.Key,
                    Parts = [new IndexPart { Kind = IndexPartKind.Field, FieldId = "item.rank", Direction = IndexSortDirection.Desc }]
                }
            ]
        };
        try
        {
            await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] });
            (await store.ListAsync(collection, new RecordQuery(), Operation(BaseOperationKind.List))).Status.Should().Be(OperationStatus.Ok);
            OperationResult<RecordEnvelope> created = await store.CreateAsync(collection, new RecordCreateRequest
            {
                RequestedId = new RecordId("binary"),
                Payload = Payload("{\"title\":\"schema\",\"blob\":\"AQID\"}")
            }, Operation(BaseOperationKind.Create));
            created.Status.Should().Be(OperationStatus.Created);
            created.Value!.Payload.Fields!["blob"].GetString().Should().Be("AQID");

            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var columns = connection.CreateCommand();
            columns.CommandText = $"PRAGMA table_info({PhysicalTable("items")});";
            var physicalColumns = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var reader = await columns.ExecuteReaderAsync())
                while (await reader.ReadAsync()) physicalColumns[reader.GetString(1)] = reader.GetString(2);

            physicalColumns.Should().Contain(new KeyValuePair<string, string>(PhysicalField("item.title"), "TEXT"));
            physicalColumns.Should().Contain(new KeyValuePair<string, string>(PhysicalField("item.rank"), "INTEGER"));
            physicalColumns.Should().Contain(new KeyValuePair<string, string>(PhysicalField("item.blob"), "BLOB"));
            physicalColumns.Keys.Should().Contain(PhysicalPresence("item.rank"));
            physicalColumns.Keys.Should().NotContain(["payload_json", "extension_json"]);

            await using var indexes = connection.CreateCommand();
            indexes.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index';";
            var names = new List<string>();
            await using (var reader = await indexes.ExecuteReaderAsync())
                while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            names.Should().Contain(PhysicalIndex("item.by-rank"));
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task TestSchemaInitializationCreatesOnlyProviderOwnedTables()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE host_table(id TEXT PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }

            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, SchemaPrefix = "l21_" });
            var create = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));
            create.Status.Should().Be(OperationStatus.Created);

            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await verify.OpenAsync();
            await using var list = verify.CreateCommand();
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            var names = new List<string>();
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));

            names.Should().Contain(["host_table", PhysicalTable("items"), "l21_collections", "l21_provider_state", "l21_mutation_journal"]);
            names.Should().NotContain("l21_records");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task MissingAcceptedSchemaFailsClosedAndHealthIsUnhealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-missing-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path }, initializeSchema: false);
            var result = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.StoreError);
            result.Error!.Code.Should().Be("sqlite.schema.missing");

            var services = new ServiceCollection().AddLogging().AddHPDBaseSqliteStore(options =>
            {
                options.DataSource = path;
            });
            await using var provider = services.BuildServiceProvider();
            var health = await provider.GetRequiredService<IEnumerable<IBaseHealthContributor>>().Single().GetHealthAsync();
            health.Single().Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task ExistingProviderTableWithMissingColumnsFailsValidation()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-badschema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE TABLE {PhysicalTable("items")}(record_id TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path }, initializeSchema: false);
            var result = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.StoreError);
            result.Error!.Code.Should().Be("sqlite.schema.missing");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task ExistingMutationJournalWithMissingColumnFailsValidation()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "hpd-base-sqlite-badjournal-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var initialized = SqliteTestFactory.Create(
                new HPDBaseSqliteOptions { DataSource = path }))
            {
                var created = await initialized.CreateAsync(
                    Collection(),
                    new RecordCreateRequest
                    {
                        RequestedId = new RecordId("seed"),
                        Payload = Payload()
                    },
                    Operation(BaseOperationKind.Create));
                created.Status.Should().Be(OperationStatus.Created);
            }

            await using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE hpd_base_mutation_journal DROP COLUMN visibility;";
                await command.ExecuteNonQueryAsync();
            }

            await using var store = SqliteTestFactory.Create(
                new HPDBaseSqliteOptions { DataSource = path }, initializeSchema: false);
            var result = await store.CreateAsync(
                Collection(),
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("after-corruption"),
                    Payload = Payload()
                },
                Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.StoreError);
            result.Error!.Code.Should().Be("sqlite.schema.missing");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }
    }

    [Fact]
    public async Task ExistingPhysicalIndexWithWrongShapeIsReportedAsDrift()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-index-drift-" + Guid.NewGuid().ToString("N") + ".db");
        var collection = Collection() with
        {
            Fields = [new FieldDefinition { Id = "item.rank", Name = "rank", Type = BaseFieldTypes.Integer }],
            Indexes = [new IndexDefinition
            {
                Id = "item.by-rank", Name = "by-rank", CollectionId = "items", Kind = IndexKind.Key,
                Parts = [new IndexPart { Kind = IndexPartKind.Field, FieldId = "item.rank" }]
            }]
        };
        var options = new HPDBaseSqliteOptions { DataSource = path, Collections = [collection] };
        try
        {
            await using (SqliteRecordStore initialized = SqliteTestFactory.Create(options)) { }
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using (SqliteCommand corrupt = connection.CreateCommand())
            {
                corrupt.CommandText = $"DROP INDEX {PhysicalIndex("item.by-rank")}; CREATE INDEX {PhysicalIndex("item.by-rank")} ON {PhysicalTable("items")}(record_id);";
                await corrupt.ExecuteNonQueryAsync();
            }

            string[] drift = await new SqliteSchemaInitializer(options).GetMissingSchemaPartsAsync(connection, CancellationToken.None);
            drift.Should().Contain("index-shape:" + PhysicalIndex("item.by-rank"));
        }
        finally
        {
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static string PhysicalTable(string collectionId) => "b_c_" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(collectionId)))[..32];
    private static string PhysicalField(string fieldId) => "f_" + Digest(fieldId);
    private static string PhysicalPresence(string fieldId) => "p_" + Digest(fieldId);
    private static string PhysicalIndex(string indexId) => "b_i_" + Digest(indexId);
    private static string Digest(string id) => Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..32];
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
    private static RecordPayload Payload(string json = "{\"title\":\"schema\"}")
    {
        using var document = JsonDocument.Parse(json);
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }
}
