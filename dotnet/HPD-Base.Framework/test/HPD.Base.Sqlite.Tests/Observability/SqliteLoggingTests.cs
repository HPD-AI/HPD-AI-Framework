using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.Tests.Observability;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Observability;

public sealed class SqliteLoggingTests
{
    [Fact]
    public async Task CantOpenEmitsExactSafeDatabaseOpenFailure()
    {
        var marker = "secret-" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(Path.GetTempPath(), marker, "store.db");
        using var collector = new LogCollector();
        using var loggerFactory = CreateLoggerFactory(collector);
        await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions { DataSource = path }, loggerFactory);

        var result = await store.GetAsync(Collection(), new RecordId("one"), Operation(BaseOperationKind.Get));

        result.Status.Should().Be(OperationStatus.StoreError);
        var record = collector.RecordsFor(3000).Should().ContainSingle().Subject;
        AssertContract(record, 3000);
        LogSafetyInspector.AssertSafe(collector.Records, marker, path);
        LogSafetyInspector.AssertNoExceptions(collector.Records);
        LogSafetyInspector.AssertNoScopes(collector.Records);
    }

    [Fact]
    public async Task UnsafeQueryEmitsExactQueryPlanRejection()
    {
        var path = TempPath("query");
        try
        {
            using var collector = new LogCollector();
            using var loggerFactory = CreateLoggerFactory(collector);
            await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions { DataSource = path }, loggerFactory);

            var result = await store.ListAsync(
                Collection(),
                new RecordQuery
                {
                    Filter = new FilterExpression
                    {
                        Kind = FilterNodeKind.Compare,
                        Field = "title",
                        Operator = FilterOperator.Contains,
                        Value = new QueryValue { Kind = QueryValueKind.String, String = "private-query-value" }
                    }
                },
                Operation(BaseOperationKind.List));

            result.Status.Should().Be(OperationStatus.Unsupported);
            AssertContract(collector.RecordsFor(3006).Should().ContainSingle().Subject, 3006);
            LogSafetyInspector.AssertSafe(collector.Records, "private-query-value");
            LogSafetyInspector.AssertNoExceptions(collector.Records);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task CorruptDatabaseEmitsExactProviderFailureWithoutNativeMessage()
    {
        var marker = "private-corrupt-content-" + Guid.NewGuid().ToString("N");
        var path = TempPath("corrupt");
        try
        {
            await File.WriteAllTextAsync(path, marker);
            using var collector = new LogCollector();
            using var loggerFactory = CreateLoggerFactory(collector);
            await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions { DataSource = path }, loggerFactory);

            var result = await store.GetAsync(Collection(), new RecordId("one"), Operation(BaseOperationKind.Get));

            result.Status.Should().Be(OperationStatus.StoreError);
            AssertContract(collector.RecordsFor(3008).Should().ContainSingle().Subject, 3008);
            collector.Records.Should().ContainSingle();
            LogSafetyInspector.AssertSafe(collector.Records, marker, path);
            LogSafetyInspector.AssertNoExceptions(collector.Records);
            LogSafetyInspector.AssertNoScopes(collector.Records);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task BusyWriteBoundaryReturnsRoutineConflictWithoutLogging()
    {
        var path = TempPath("busy");
        try
        {
            await using (var setup = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path }))
            {
                var created = await setup.CreateAsync(Collection(), CreateRequest("seed"), Operation(BaseOperationKind.Create));
                created.Status.Should().Be(OperationStatus.Created);
            }

            await using var lockConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await lockConnection.OpenAsync();
            await using var lockCommand = lockConnection.CreateCommand();
            lockCommand.CommandText = "BEGIN IMMEDIATE;";
            await lockCommand.ExecuteNonQueryAsync();

            using var collector = new LogCollector();
            using var loggerFactory = CreateLoggerFactory(collector);
            await using var store = new SqliteRecordStore(
                new HPDBaseSqliteOptions
                {
                    DataSource = path,
                    BusyTimeout = TimeSpan.FromMilliseconds(1),
                    CommandTimeout = TimeSpan.FromSeconds(1)
                },
                loggerFactory);

            var result = await store.CreateAsync(Collection(), CreateRequest("blocked"), Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.Conflict);
            result.Error!.Code.Should().Be(BaseMutationErrorCodes.TransactionConflict);
            collector.Records.Should().BeEmpty();
            LogSafetyInspector.AssertSafe(collector.Records, path);
            LogSafetyInspector.AssertNoExceptions(collector.Records);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task MissingSchemaEmitsExactSchemaFailure()
    {
        var path = TempPath("schema");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE hpd_base_records(collection_id TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            using var collector = new LogCollector();
            using var loggerFactory = CreateLoggerFactory(collector);
            await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions { DataSource = path }, loggerFactory);

            var result = await store.CreateAsync(Collection(), CreateRequest("one"), Operation(BaseOperationKind.Create));

            result.Status.Should().Be(OperationStatus.StoreError);
            AssertContract(collector.RecordsFor(3003).Should().ContainSingle().Subject, 3003);
            collector.Records.Should().ContainSingle();
            LogSafetyInspector.AssertSafe(collector.Records, path);
            LogSafetyInspector.AssertNoExceptions(collector.Records);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task DiagnosticMissingSchemaEmitsExactDegradedStateWarning()
    {
        var path = TempPath("diagnostic");
        try
        {
            using var collector = new LogCollector();
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(collector));
            services.AddHPDBaseSqliteStore(options =>
            {
                options.DataSource = path;
                options.AutoInitialize = false;
            });

            await using var provider = services.BuildServiceProvider();
            var contributor = provider.GetRequiredService<IEnumerable<IBaseDiagnosticContributor>>().Single();

            var diagnostics = await contributor.GetDiagnosticsAsync();

            diagnostics.Should().Contain(diagnostic => diagnostic.Code == "base.sqlite.configuration");
            AssertContract(collector.RecordsFor(3005).Should().ContainSingle().Subject, 3005);
            collector.Records.Should().ContainSingle();
            LogSafetyInspector.AssertSafe(collector.Records, path);
            LogSafetyInspector.AssertNoExceptions(collector.Records);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task ExpectedSuccessAndDuplicateConflictEmitNoLogs()
    {
        var path = TempPath("noise");
        try
        {
            using var collector = new LogCollector();
            using var loggerFactory = CreateLoggerFactory(collector);
            await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions { DataSource = path }, loggerFactory);

            var created = await store.CreateAsync(Collection(), CreateRequest("one"), Operation(BaseOperationKind.Create));
            var duplicate = await store.CreateAsync(Collection(), CreateRequest("one"), Operation(BaseOperationKind.Create));

            created.Status.Should().Be(OperationStatus.Created);
            duplicate.Status.Should().Be(OperationStatus.Conflict);
            collector.Records.Should().BeEmpty();
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static ILoggerFactory CreateLoggerFactory(LogCollector collector) =>
        LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(collector));

    private static void AssertContract(CapturedLogRecord record, int eventId)
    {
        var contract = HPDBaseLogEventRegistry.Active.Single(candidate => candidate.Owner == "HPD.Base.Sqlite" && candidate.Id == eventId);
        record.EventId.Id.Should().Be(contract.Id);
        record.EventId.Name.Should().Be(contract.Name);
        record.Level.Should().Be(contract.Level);
        record.OriginalFormat.Should().Be(contract.Template);
        record.State
            .Where(property => property.Key != "{OriginalFormat}")
            .Select(property => property.Key)
            .Should().Equal(contract.Properties);
        record.State.Select(property => property.Key).Should().NotContain("StoreId");
    }

    private static string TempPath(string purpose) =>
        Path.Combine(Path.GetTempPath(), $"hpd-base-sqlite-log-{purpose}-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private static CollectionDefinition Collection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    private static OperationContext Operation(BaseOperationKind kind) => new()
    {
        Operation = kind,
        CollectionId = "items",
        Now = DateTimeOffset.UnixEpoch
    };

    private static RecordCreateRequest CreateRequest(string id)
    {
        using var document = JsonDocument.Parse("""{"title":"safe"}""");
        return new RecordCreateRequest
        {
            RequestedId = new RecordId(id),
            Payload = new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() }
        };
    }
}
