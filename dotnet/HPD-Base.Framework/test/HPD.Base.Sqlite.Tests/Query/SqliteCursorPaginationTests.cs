using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Query;

public sealed class SqliteCursorPaginationTests
{
    [Fact]
    public async Task StableHistoryExcludesLaterInsertAndContinuesAcrossRestart()
    {
        string path = TempPath();
        CollectionDefinition collection = Collection(BaseCollectionMutationMode.AppendOnly);
        HPDBaseSqliteOptions options = StoreOptions(path, collection);
        string cursor;
        try
        {
            await using (SqliteRecordStore firstStore = CreateStore(options))
            {
                await CreateAsync(firstStore, collection, "a", "a");
                await CreateAsync(firstStore, collection, "c", "c");
                OperationResult<RecordPage> first = await firstStore.ListAsync(
                    collection, FirstQuery(), Operation());
                cursor = first.Value!.Page.NextCursor!;
                cursor.Should().NotBeNullOrWhiteSpace();
                await CreateAsync(firstStore, collection, "b", "b");
            }

            await using SqliteRecordStore restarted = CreateStore(options);
            OperationResult<RecordPage> second = await restarted.ListAsync(
                collection,
                FirstQuery() with
                {
                    Page = new QueryPage
                    {
                        Mode = QueryPaginationMode.Cursor,
                        Limit = 1,
                        Cursor = cursor
                    }
                },
                Operation());

            second.Status.Should().Be(OperationStatus.Ok);
            second.Value!.Items.Select(item => item.Payload.Fields!["title"].GetString())
                .Should().Equal("c");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task CursorIsQueryScopeBoundAndTamperEvident()
    {
        string path = TempPath();
        CollectionDefinition collection = Collection(BaseCollectionMutationMode.Mutable);
        try
        {
            await using SqliteRecordStore store = CreateStore(StoreOptions(path, collection));
            await CreateAsync(store, collection, "one", "private-sqlite-order-one");
            await CreateAsync(store, collection, "two", "private-sqlite-order-two");
            OperationContext tenantA = Operation() with { TenantId = "tenant-a" };
            OperationResult<RecordPage> first = await store.ListAsync(collection, FirstQuery(), tenantA);
            string cursor = first.Value!.Page.NextCursor!;
            Decode(cursor).AsSpan().IndexOf("private-sqlite-order-one"u8).Should().Be(-1);

            RecordQuery continuation = FirstQuery() with
            {
                Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 1, Cursor = cursor }
            };
            OperationResult<RecordPage> wrongScope = await store.ListAsync(
                collection, continuation, tenantA with { TenantId = "tenant-b" });
            int tamperIndex = cursor.Length / 2;
            string tampered = cursor[..tamperIndex]
                + (cursor[tamperIndex] == 'A' ? 'B' : 'A')
                + cursor[(tamperIndex + 1)..];
            OperationResult<RecordPage> invalid = await store.ListAsync(
                collection, continuation with { Page = continuation.Page! with { Cursor = tampered } }, tenantA);

            wrongScope.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorScopeMismatch);
            invalid.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorInvalid);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task CursorReportsExactQueryDirectionVersionAndPurgeOutcomes()
    {
        string path = TempPath();
        CollectionDefinition collection = Collection(BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge);
        try
        {
            await using SqliteRecordStore store = CreateStore(StoreOptions(path, collection));
            await CreateAsync(store, collection, "one", "one");
            await CreateAsync(store, collection, "two", "two");
            RecordQuery firstQuery = FirstQuery();
            string cursor = (await store.ListAsync(collection, firstQuery, Operation())).Value!.Page.NextCursor!;
            RecordQuery continuation = firstQuery with
            {
                Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 1, Cursor = cursor },
            };

            OperationResult<RecordPage> queryMismatch = await store.ListAsync(
                collection,
                continuation with { Sort = [new QuerySort("title", QuerySortDirection.Desc)] },
                Operation());
            OperationResult<RecordPage> directionMismatch = await store.ListAsync(
                collection,
                continuation with { Page = continuation.Page! with { CursorDirection = QueryCursorDirection.Before } },
                Operation());
            byte[] futureWire = Decode(cursor);
            futureWire[1] = 2;
            OperationResult<RecordPage> futureVersion = await store.ListAsync(
                collection,
                continuation with { Page = continuation.Page! with { Cursor = Encode(futureWire) } },
                Operation());

            var processor = new PurgeGenerationProcessor(collection);
            RecordMutationExecutionResult purge = await store.ExecuteAtomicAsync(processor, ExecutionRequest());
            OperationResult<RecordPage> expired = await store.ListAsync(collection, continuation, Operation());

            queryMismatch.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorQueryMismatch);
            directionMismatch.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorDirectionUnsupported);
            futureVersion.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorVersionUnsupported);
            purge.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            expired.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorExpired);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task CursorExpiresAfterItsBoundedLifetime()
    {
        string path = TempPath();
        CollectionDefinition collection = Collection(BaseCollectionMutationMode.Mutable);
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero));
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(
                StoreOptions(path, collection),
                timeProvider: time,
                tokenProtector: Protector());
            await CreateAsync(store, collection, "one", "one");
            await CreateAsync(store, collection, "two", "two");
            RecordQuery first = FirstQuery();
            string cursor = (await store.ListAsync(collection, first, Operation())).Value!.Page.NextCursor!;
            time.Advance(TimeSpan.FromDays(8));

            OperationResult<RecordPage> expired = await store.ListAsync(
                collection,
                first with { Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 1, Cursor = cursor } },
                Operation());

            expired.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorExpired);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task CursorFailsClosedWhenAnOrderingKeyExceedsItsWireBound()
    {
        string path = TempPath();
        CollectionDefinition collection = Collection(BaseCollectionMutationMode.Mutable);
        try
        {
            await using SqliteRecordStore store = CreateStore(StoreOptions(path, collection));
            await CreateAsync(store, collection, "one", new string('a', 4_097));
            await CreateAsync(store, collection, "two", new string('b', 4_097));

            OperationResult<RecordPage> result = await store.ListAsync(collection, FirstQuery(), Operation());

            result.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorKeyTooLarge);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task EqualSortValuesContinueDeterministicallyByRecordIdentity()
    {
        string path = TempPath();
        CollectionDefinition collection = Collection(BaseCollectionMutationMode.Mutable);
        try
        {
            await using SqliteRecordStore store = CreateStore(StoreOptions(path, collection));
            await CreateAsync(store, collection, "c", "same");
            await CreateAsync(store, collection, "a", "same");
            await CreateAsync(store, collection, "b", "same");
            var ids = new List<string>();
            string? cursor = null;
            do
            {
                RecordQuery query = FirstQuery() with
                {
                    Page = cursor is null
                        ? FirstQuery().Page
                        : new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 1, Cursor = cursor },
                };
                RecordPage page = (await store.ListAsync(collection, query, Operation())).Value!;
                ids.AddRange(page.Items.Select(static item => item.Id.Value));
                cursor = page.Page.NextCursor;
            } while (cursor is not null);

            ids.Should().Equal("a", "b", "c");
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task ConcurrentCommittedCreatesOwnUniqueIncreasingAppendPositions()
    {
        string path = TempPath();
        CollectionDefinition collection = Collection(BaseCollectionMutationMode.AppendOnly);
        try
        {
            await using SqliteRecordStore store = CreateStore(StoreOptions(path, collection));
            await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
                CreateAsync(store, collection, $"id-{index:D2}", $"title-{index:D2}")));

            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT append_position FROM {PhysicalTable(collection.Id)} ORDER BY append_position;";
            var positions = new List<long>();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) positions.Add(reader.GetInt64(0));

            positions.Should().HaveCount(20);
            positions.Should().OnlyHaveUniqueItems();
            positions.Should().BeInAscendingOrder();
            positions.Should().Equal(Enumerable.Range(1, 20).Select(static value => (long)value));
        }
        finally { Delete(path); }
    }

    private static SqliteRecordStore CreateStore(HPDBaseSqliteOptions options)
    {
        SqliteRecordStore store = SqliteTestFactory.Create(
            options,
            tokenProtector: Protector());
        return store;
    }

    private static BaseOpaqueTokenProtector Protector() => new(Options.Create(
        new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 9,
                Key = Enumerable.Repeat((byte)0x99, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch
            }
        }));

    private static HPDBaseSqliteOptions StoreOptions(string path, CollectionDefinition collection) => new()
    {
        DataSource = path,
        Collections = [collection],
        DefaultPageSize = 1,
        MaxPageSize = 10
    };

    private static CollectionDefinition Collection(BaseCollectionMutationMode mode) => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Strict,
        UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = mode,
        Fields =
        [
            new FieldDefinition
            {
                Id = "title",
                ApplicationName = "title", WireName = "title",
                Type = BaseFieldTypes.String,
                Presence = BaseFieldPresence.Required
            }
        ]
    };

    private static RecordQuery FirstQuery() => new()
    {
        Sort = [new QuerySort("title")],
        Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 1 },
        Count = QueryCountMode.None
    };

    private static async Task CreateAsync(
        SqliteRecordStore store,
        CollectionDefinition collection,
        string id,
        string title)
    {
        using JsonDocument document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
        OperationResult<RecordEnvelope> result = await store.CreateAsync(
            collection,
            new RecordCreateRequest
            {
                RequestedId = RecordId.Create(id),
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.Json,
                    Json = document.RootElement.Clone()
                }
            },
            Operation(BaseOperationKind.Create));
        result.Status.Should().Be(OperationStatus.Created);
    }

    private static OperationContext Operation(BaseOperationKind kind = BaseOperationKind.List) => new()
    {
        Operation = kind,
        CollectionId = "items",
        Now = DateTimeOffset.UnixEpoch
    };

    private static byte[] Decode(string value)
    {
        string text = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight(text.Length + ((4 - text.Length % 4) % 4), '='));
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static RecordMutationExecutionRequest ExecutionRequest() => new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5),
    };

    private sealed class PurgeGenerationProcessor(CollectionDefinition collection) : IAtomicMutationProcessor
    {
        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            OperationResult<long> generation = await session.AdvancePurgeGenerationAsync(collection, 0, cancellationToken);
            return generation.IsSuccess()
                ? new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, [])
                : new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.Failed, [], generation.Error!);
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;
        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan duration) => _value += duration;
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-cursor-" + Guid.NewGuid().ToString("N") + ".db");

    private static string PhysicalTable(string collectionId) => "b_c_" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(collectionId)))[..32];

    private static void Delete(string path)
    {
        foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }
}
