using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using HPD.Base.Observability;
using HPD.Base.Tests.Observability;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;

namespace HPD.Base.Sqlite.Tests.Observability;

public sealed class SqliteTelemetryTests
{
    [Fact]
    public async Task StoreAndProviderSpansDoNotLeakIdsPayloadsSqlOrPaths()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.Sqlite);
        using var metrics = new MeterCollector(HPDBaseMeterNames.Sqlite);
        var path = TempPath("sqlite-secret-path");
        await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions
        {
            StoreId = "primary",
            DataSource = path,
            CollectionIds = ["items"],
            Collections = [Collection()]
        });

        var create = await store.CreateAsync(
            Collection(),
            new RecordCreateRequest
            {
                RequestedId = new RecordId("rec_secret"),
                Payload = Payload("payload-secret")
            },
            Operation(BaseOperationKind.Create, "rec_secret"));
        var list = await store.ListAsync(
            Collection(),
            new RecordQuery
            {
                Filter = new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "title",
                    Operator = FilterOperator.Equal,
                    Value = new QueryValue { Kind = QueryValueKind.String, String = "payload-secret" }
                }
            },
            Operation(BaseOperationKind.List));

        create.Status.Should().Be(OperationStatus.Created);
        list.Status.Should().Be(OperationStatus.Ok);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.StoreCreate);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.StoreList);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.SqliteConnectionOpen);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.SqliteSchemaInitialize);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.SqliteQueryPlan);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.SqliteTransaction);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.StoreOperations);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.SqliteConnectionsOpened);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.SqliteQueryPlans);
        metrics.RecordObservableInstruments();
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.SqliteSchemaMissingParts);

        var forbidden = new[] { "rec_secret", "payload-secret", "sqlite-secret-path", "SELECT ", "INSERT ", "$payload" };
        activities.Stopped.Should().NotContain(activity => TagValues(activity).Any(value => forbidden.Any(marker => value.Contains(marker, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task SqliteNativeMessagesStayOutOfTelemetryTags()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.Sqlite);
        var dataSource = Path.Combine(Path.GetTempPath(), "hpd-base-missing-native-message", "native-message-secret.db");
        await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions
        {
            StoreId = "primary",
            DataSource = dataSource,
            CollectionIds = ["items"],
            Collections = [Collection()]
        });

        var result = await store.ListAsync(Collection(), new RecordQuery(), Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.StoreError);
        result.Error!.Store!.NativeMessage.Should().NotBeNullOrWhiteSpace();
        var nativeMessage = result.Error.Store.NativeMessage!;
        activities.Stopped.Should().NotContain(activity => TagValues(activity).Any(value =>
            value.Contains(nativeMessage, StringComparison.Ordinal) ||
            value.Contains("native-message-secret", StringComparison.Ordinal)));
        activities.Stopped.Should().Contain(activity => activity.TagObjects.Any(tag => tag.Key == HPDBaseTelemetryTags.SqliteNativeCode));
    }

    [Fact]
    public async Task StoreOperationsWorkWithoutConfiguredTelemetryListeners()
    {
        var path = TempPath("no-listener");
        try
        {
            await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions
            {
                StoreId = "primary",
                DataSource = path,
                CollectionIds = ["items"],
                Collections = [Collection()]
            });

            var create = await store.CreateAsync(
                Collection(),
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("rec_no_listener"),
                    Payload = Payload("no-listener")
                },
                Operation(BaseOperationKind.Create, "rec_no_listener"));
            var list = await store.ListAsync(Collection(), new RecordQuery(), Operation(BaseOperationKind.List));

            create.Status.Should().Be(OperationStatus.Created);
            list.Status.Should().Be(OperationStatus.Ok);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
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

    private static OperationContext Operation(BaseOperationKind operation, string? recordId = null) => new()
    {
        Operation = operation,
        CollectionId = "items",
        RecordId = recordId,
        Now = DateTimeOffset.UnixEpoch
    };

    private static RecordPayload Payload(string title)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    private static string TempPath(string marker) =>
        Path.Combine(Path.GetTempPath(), "hpd-base-" + marker + "-" + Guid.NewGuid().ToString("N") + ".db");

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

}
