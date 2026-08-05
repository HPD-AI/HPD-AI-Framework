using HPD.Base.Tests.InMemory.TestDoubles;

namespace HPD.Base.Tests.InMemory.Query;

public sealed class QueryExecutionTests
{
    [Fact]
    public async Task ListFiltersSortsCountsAndSelectsPayloadFields()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        await Create(store, collection, "a", "open", "1");
        await Create(store, collection, "b", "closed", "3");
        await Create(store, collection, "c", "open", "2");

        var result = await store.ListAsync(
            collection,
            new RecordQuery
            {
                Filter = new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "status",
                    Operator = FilterOperator.Equal,
                    Value = new QueryValue { Kind = QueryValueKind.String, String = "open" }
                },
                Sort = [new QuerySort("rank", QuerySortDirection.Desc)],
                Select = ["title"],
                Count = QueryCountMode.Exact
            },
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Count!.Total.Should().Be(2);
        result.Value.Items.Select(item => item.Payload.Fields!["title"].GetString()).Should().Equal("c", "a");
        foreach (var item in result.Value.Items)
        {
            item.Payload.Fields!.Keys.Should().Equal("title");
        }
    }

    [Fact]
    public async Task SelectReconstructsNestedPayloadFieldPaths()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        using var document = JsonDocument.Parse("""{"title":"a","profile":{"name":"ada","hidden":"x"}}""");
        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest
            {
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.Json,
                    Json = document.RootElement.Clone()
                }
            },
            InMemoryTestData.Operation(BaseOperationKind.Create));
        create.Status.Should().Be(OperationStatus.Created);

        var result = await store.ListAsync(
            collection,
            new RecordQuery { Select = ["profile.name"], Count = QueryCountMode.None },
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Items.Should().ContainSingle();
        var fields = result.Value.Items[0].Payload.Fields!;
        fields.Keys.Should().Equal("profile");
        fields["profile"].GetProperty("name").GetString().Should().Be("ada");
        fields["profile"].TryGetProperty("hidden", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvalidSelectFieldPathsFailClosed()
    {
        var store = new InMemoryRecordStore();

        var result = await store.ListAsync(
            InMemoryTestData.Collection(),
            new RecordQuery { Select = ["profile..name"] },
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task PageOffsetAndCursorPaginationReturnStablePages()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        await Create(store, collection, "a", "open", "1");
        await Create(store, collection, "b", "open", "2");
        await Create(store, collection, "c", "open", "3");

        var first = await store.ListAsync(
            collection,
            new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 2 } },
            InMemoryTestData.Operation(BaseOperationKind.List));
        first.Value!.Items.Select(item => item.Payload.Fields!["title"].GetString()).Should().Equal("a", "b");
        first.Value.Page.NextCursor.Should().NotBeNullOrWhiteSpace();

        var second = await store.ListAsync(
            collection,
            new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 2, Cursor = first.Value.Page.NextCursor } },
            InMemoryTestData.Operation(BaseOperationKind.List));
        second.Value!.Items.Select(item => item.Payload.Fields!["title"].GetString()).Should().Equal("c");
    }

    [Fact]
    public async Task MissingPageUsesConfiguredDefaultPageSize()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { DefaultPageSize = 2, MaxPageSize = 5 });
        var collection = InMemoryTestData.Collection();
        await Create(store, collection, "a", "open", "1");
        await Create(store, collection, "b", "open", "2");
        await Create(store, collection, "c", "open", "3");

        var result = await store.ListAsync(
            collection,
            new RecordQuery(),
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Items.Select(item => item.Payload.Fields!["title"].GetString()).Should().Equal("a", "b");
        result.Value.Page.NextCursor.Should().NotBeNullOrWhiteSpace();
        result.Value.Page.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task CursorCannotBeReusedForDifferentQueryShape()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { DefaultPageSize = 2, MaxPageSize = 5 });
        var collection = InMemoryTestData.Collection();
        await Create(store, collection, "a", "open", "1");
        await Create(store, collection, "b", "open", "2");
        await Create(store, collection, "c", "closed", "3");

        var first = await store.ListAsync(
            collection,
            new RecordQuery
            {
                Filter = StatusFilter("open"),
                Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 1 }
            },
            InMemoryTestData.Operation(BaseOperationKind.List));

        var reused = await store.ListAsync(
            collection,
            new RecordQuery
            {
                Filter = StatusFilter("closed"),
                Page = new QueryPage
                {
                    Mode = QueryPaginationMode.Cursor,
                    Limit = 1,
                    Cursor = first.Value!.Page.NextCursor
                }
            },
            InMemoryTestData.Operation(BaseOperationKind.List));

        reused.Status.Should().Be(OperationStatus.ValidationFailed);
        reused.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task AppendOnlyCursorExcludesLaterInsertInsideApplicationSortRange()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection() with
        {
            MutationMode = BaseCollectionMutationMode.AppendOnly
        };
        await Create(store, collection, "a", "open", "1");
        await Create(store, collection, "c", "open", "2");
        RecordQuery firstQuery = new()
        {
            Sort = [new QuerySort("title")],
            Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 1 }
        };
        OperationContext operation = InMemoryTestData.Operation(BaseOperationKind.List);
        var first = await store.ListAsync(collection, firstQuery, operation);
        await Create(store, collection, "b", "open", "3");

        var second = await store.ListAsync(collection, firstQuery with
        {
            Page = new QueryPage
            {
                Mode = QueryPaginationMode.Cursor,
                Limit = 1,
                Cursor = first.Value!.Page.NextCursor
            }
        }, operation);

        second.Status.Should().Be(OperationStatus.Ok);
        second.Value!.Items.Select(item => item.Payload.Fields!["title"].GetString())
            .Should().Equal("c");
    }

    [Fact]
    public async Task CursorIsConfidentialTamperEvidentAndScopeBound()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        await Create(store, collection, "private-ordering-value-one", "open", "1");
        await Create(store, collection, "private-ordering-value-two", "open", "2");
        OperationContext tenantA = InMemoryTestData.Operation(BaseOperationKind.List) with { TenantId = "tenant-a" };
        RecordQuery firstQuery = new()
        {
            Sort = [new QuerySort("title")],
            Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 1 }
        };
        var first = await store.ListAsync(collection, firstQuery, tenantA);
        string cursor = first.Value!.Page.NextCursor!;
        byte[] wire = DecodeCursorWire(cursor);
        wire.AsSpan().IndexOf("private-ordering-value-one"u8).Should().Be(-1);

        int tamperIndex = cursor.Length / 2;
        string tampered = cursor[..tamperIndex]
            + (cursor[tamperIndex] == 'A' ? 'B' : 'A')
            + cursor[(tamperIndex + 1)..];
        RecordQuery continuation = firstQuery with
        {
            Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 1, Cursor = tampered }
        };
        var invalid = await store.ListAsync(collection, continuation, tenantA);
        var wrongScope = await store.ListAsync(collection, continuation with
        {
            Page = continuation.Page! with { Cursor = cursor }
        }, tenantA with { TenantId = "tenant-b" });

        invalid.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorInvalid);
        wrongScope.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorScopeMismatch);
    }

    [Fact]
    public async Task PageModeRejectsPageZero()
    {
        var store = new InMemoryRecordStore();
        var result = await store.ListAsync(
            InMemoryTestData.Collection(),
            new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 0, PerPage = 10 } },
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task UnsupportedQueryFeaturesFailWithError()
    {
        var store = new InMemoryRecordStore();
        var result = await store.ListAsync(
            InMemoryTestData.Collection(),
            new RecordQuery { Include = [new RecordInclude { NavigationId = "relation" }] },
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task UnsupportedFilterOperatorsFailInsteadOfBeingIgnored()
    {
        var store = new InMemoryRecordStore();
        var result = await store.ListAsync(
            InMemoryTestData.Collection(),
            new RecordQuery
            {
                Filter = new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "title",
                    Operator = FilterOperator.Like,
                    Value = new QueryValue { Kind = QueryValueKind.String, String = "hello" }
                }
            },
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
    }

    private static async Task Create(InMemoryRecordStore store, CollectionDefinition collection, string title, string status, string rank)
    {
        var result = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", title), ("status", status), ("rank", rank)) },
            InMemoryTestData.Operation(BaseOperationKind.Create));
        result.Status.Should().Be(OperationStatus.Created);
    }

    private static FilterExpression StatusFilter(string status) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = "status",
        Operator = FilterOperator.Equal,
        Value = new QueryValue { Kind = QueryValueKind.String, String = status }
    };

    private static byte[] DecodeCursorWire(string value)
    {
        string text = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight(text.Length + ((4 - text.Length % 4) % 4), '='));
    }
}
