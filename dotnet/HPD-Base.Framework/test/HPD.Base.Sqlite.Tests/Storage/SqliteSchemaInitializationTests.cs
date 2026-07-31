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
    public async Task AutoInitializeCreatesOnlyProviderOwnedTables()
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

            names.Should().Contain(["host_table", "l21_records", "l21_collections", "l21_provider_state", "l21_mutation_journal"]);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task AutoInitializeFalseWithMissingSchemaFailsWhenConfigured()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-missing-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, AutoInitialize = false, FailIfSchemaMissing = true });
            var result = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = new RecordId("one"), Payload = Payload() }, Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.StoreError);
            result.Error!.Code.Should().Be("sqlite.database.unavailable");

            var services = new ServiceCollection().AddLogging().AddHPDBaseSqliteStore(options =>
            {
                options.DataSource = path;
                options.AutoInitialize = false;
                options.FailIfSchemaMissing = true;
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
                command.CommandText = "CREATE TABLE hpd_base_records(collection_id TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path });
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
                new HPDBaseSqliteOptions { DataSource = path });
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

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
    private static RecordPayload Payload()
    {
        using var document = JsonDocument.Parse("""{"title":"schema"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }
}
