using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Errors;

public sealed class SqliteDatabaseErrorTests
{
    [Fact]
    public async Task BusyWriteBoundaryMapsToNonRetryingTransactionConflict()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-busy-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var setup = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path });
            var created = await setup.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = RecordId.Create("seed"), Payload = Payload("seed") }, Operation(BaseOperationKind.Create));
            created.Status.Should().Be(OperationStatus.Created);

            await using var lockConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await lockConnection.OpenAsync();
            await using var lockCommand = lockConnection.CreateCommand();
            lockCommand.CommandText = "BEGIN IMMEDIATE;";
            await lockCommand.ExecuteNonQueryAsync();

            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, BusyTimeout = TimeSpan.FromMilliseconds(1), CommandTimeout = TimeSpan.FromSeconds(1) }, initializeSchema: false);
            var result = await store.CreateAsync(Collection(), new RecordCreateRequest { RequestedId = RecordId.Create("blocked"), Payload = Payload("blocked") }, Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.Conflict);
            result.Error!.Code.Should().Be(BaseMutationErrorCodes.TransactionConflict);
            result.Error.Category.Should().Be(ErrorCategory.Conflict);
            result.Error.Conflict!.Kind.Should().Be(ConflictKind.Transaction);
            result.Error.Store.Should().BeNull();
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task CorruptDatabaseMapsToStoreError()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-corrupt-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await File.WriteAllTextAsync(path, "not sqlite");
            var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path }, initializeSchema: false);
            var result = await store.GetAsync(Collection(), RecordId.Create("one"), Operation(BaseOperationKind.Get));

            result.Status.Should().Be(OperationStatus.StoreError);
            result.Error!.Code.Should().BeOneOf("sqlite.database.corrupt", "sqlite.database.unavailable");
            result.Error.Store!.NativeCategory.Should().Be("sqlite");
            result.Error.Store.NativeCode.Should().NotBeNullOrWhiteSpace();
            result.Error.Store.NativeSubcode.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task CantOpenDatabaseMapsToSpecificNativeStoreError()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-missing-" + Guid.NewGuid().ToString("N"), "store.db");
        var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path }, initializeSchema: false);

        var result = await store.GetAsync(Collection(), RecordId.Create("one"), Operation(BaseOperationKind.Get));

        result.Status.Should().Be(OperationStatus.StoreError);
        result.Error!.Code.Should().Be("sqlite.database.cantOpen");
        result.Error.Message.Should().Contain("could not be opened");
        result.Error.Store!.NativeCategory.Should().Be("sqlite");
        result.Error.Store.NativeCode.Should().Be("14");
        result.Error.Store.NativeSubcode.Should().NotBeNullOrWhiteSpace();
        result.Error.Store.NativeMessage.Should().NotBeNullOrWhiteSpace();
    }

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
    private static RecordPayload Payload(string title)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }
}
