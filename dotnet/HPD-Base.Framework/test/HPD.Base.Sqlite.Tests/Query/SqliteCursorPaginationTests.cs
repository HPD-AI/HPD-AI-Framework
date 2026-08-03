using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Options;
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
                Key = Enumerable.Repeat((byte)0x99, 32).ToArray()
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
                Name = "title",
                Type = BaseFieldTypes.String,
                Required = true
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
                RequestedId = new RecordId(id),
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

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-cursor-" + Guid.NewGuid().ToString("N") + ".db");

    private static void Delete(string path)
    {
        foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }
}
