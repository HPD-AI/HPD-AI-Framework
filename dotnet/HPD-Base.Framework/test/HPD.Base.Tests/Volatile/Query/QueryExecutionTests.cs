using HPD.Base.Tests.Volatile.TestDoubles;

namespace HPD.Base.Tests.Volatile.Query;

public sealed class QueryExecutionTests
{
    [Fact]
    public async Task ListFiltersSortsCountsAndSelectsPayloadFields()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
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
            VolatileTestData.Operation(BaseOperationKind.List));

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
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        using var document = JsonDocument.Parse("""{"title":"a","profile":{"name":"ada","hidden":"x"}}""");
        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest
            {
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.Json,
                    Json = document.RootElement.Clone()
                }
            },
            VolatileTestData.Operation(BaseOperationKind.Create));
        create.Status.Should().Be(OperationStatus.Created);

        var result = await store.ListAsync(
            collection,
            new RecordQuery { Select = ["profile.name"], Count = QueryCountMode.None },
            VolatileTestData.Operation(BaseOperationKind.List));

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
        var store = new VolatileRecordStore();

        var result = await store.ListAsync(
            VolatileTestData.Collection(),
            new RecordQuery { Select = ["profile..name"] },
            VolatileTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task PageOffsetAndCursorPaginationReturnStablePages()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        await Create(store, collection, "a", "open", "1");
        await Create(store, collection, "b", "open", "2");
        await Create(store, collection, "c", "open", "3");

        var first = await store.ListAsync(
            collection,
            new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 2 } },
            VolatileTestData.Operation(BaseOperationKind.List));
        first.Value!.Items.Select(item => item.Payload.Fields!["title"].GetString()).Should().Equal("a", "b");
        first.Value.Page.NextCursor.Should().NotBeNullOrWhiteSpace();

        var second = await store.ListAsync(
            collection,
            new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 2, Cursor = first.Value.Page.NextCursor } },
            VolatileTestData.Operation(BaseOperationKind.List));
        second.Value!.Items.Select(item => item.Payload.Fields!["title"].GetString()).Should().Equal("c");
    }

    [Fact]
    public async Task MissingPageUsesConfiguredDefaultPageSize()
    {
        var store = new VolatileRecordStore(new HPDBaseVolatileStoreOptions { DefaultPageSize = 2, MaxPageSize = 5 });
        var collection = VolatileTestData.Collection();
        await Create(store, collection, "a", "open", "1");
        await Create(store, collection, "b", "open", "2");
        await Create(store, collection, "c", "open", "3");

        var result = await store.ListAsync(
            collection,
            new RecordQuery(),
            VolatileTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Items.Select(item => item.Payload.Fields!["title"].GetString()).Should().Equal("a", "b");
        result.Value.Page.NextCursor.Should().NotBeNullOrWhiteSpace();
        result.Value.Page.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task CursorCannotBeReusedForDifferentQueryShape()
    {
        var store = new VolatileRecordStore(new HPDBaseVolatileStoreOptions { DefaultPageSize = 2, MaxPageSize = 5 });
        var collection = VolatileTestData.Collection();
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
            VolatileTestData.Operation(BaseOperationKind.List));

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
            VolatileTestData.Operation(BaseOperationKind.List));

        reused.Status.Should().Be(OperationStatus.ValidationFailed);
        reused.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task PageModeRejectsPageZero()
    {
        var store = new VolatileRecordStore();
        var result = await store.ListAsync(
            VolatileTestData.Collection(),
            new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 0, PerPage = 10 } },
            VolatileTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task UnsupportedQueryFeaturesFailWithError()
    {
        var store = new VolatileRecordStore();
        var result = await store.ListAsync(
            VolatileTestData.Collection(),
            new RecordQuery { Include = [new QueryInclude { Path = "relation" }] },
            VolatileTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task UnsupportedFilterOperatorsFailInsteadOfBeingIgnored()
    {
        var store = new VolatileRecordStore();
        var result = await store.ListAsync(
            VolatileTestData.Collection(),
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
            VolatileTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
    }

    private static async Task Create(VolatileRecordStore store, CollectionDefinition collection, string title, string status, string rank)
    {
        var result = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", title), ("status", status), ("rank", rank)) },
            VolatileTestData.Operation(BaseOperationKind.Create));
        result.Status.Should().Be(OperationStatus.Created);
    }

    private static FilterExpression StatusFilter(string status) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = "status",
        Operator = FilterOperator.Equal,
        Value = new QueryValue { Kind = QueryValueKind.String, String = status }
    };
}
