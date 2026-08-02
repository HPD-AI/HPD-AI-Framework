namespace HPD.Base.StoreConformance.Query;

public abstract class RecordStoreQueryConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task SupportedFilterSortCountAndSelectAreApplied()
    {
        if (!Capabilities.Mutation.Create || !Capabilities.Read.List ||
            Capabilities.Query.Filter.Supported != true ||
            Capabilities.Query.Sort.Supported != true ||
            Capabilities.Query.Select.PayloadFields != true ||
            Capabilities.Query.Count.SupportedModes?.Contains(QueryCountMode.Exact) != true ||
            Capabilities.Query.Filter.Operators?.Contains(FilterOperator.Equal) != true)
        {
            return;
        }

        var store = await CreateStoreAsync();
        await CreateRecordAsync(store, "query-a", ("title", RecordStoreConformanceData.StringElement("a")), ("status", RecordStoreConformanceData.StringElement("open")), ("rank", RecordStoreConformanceData.Element("1")));
        await CreateRecordAsync(store, "query-b", ("title", RecordStoreConformanceData.StringElement("b")), ("status", RecordStoreConformanceData.StringElement("closed")), ("rank", RecordStoreConformanceData.Element("3")));
        await CreateRecordAsync(store, "query-c", ("title", RecordStoreConformanceData.StringElement("c")), ("status", RecordStoreConformanceData.StringElement("open")), ("rank", RecordStoreConformanceData.Element("2")));

        var result = await store.ListAsync(
            Collection,
            new RecordQuery
            {
                Filter = RecordStoreConformanceQueries.Equal("status", "open"),
                Sort = [new QuerySort("rank", QuerySortDirection.Desc)],
                Select = ["title"],
                Count = QueryCountMode.Exact
            },
            Operation(BaseOperationKind.List));

        RecordStoreConformanceAssertions.Success(result, OperationStatus.Ok);
        Assert.Equal(2, result.Value!.Count!.Total);
        Assert.Equal(["c", "a"], result.Value.Items.Select(item => item.Payload.Fields!["title"].GetString()!).ToArray());
        Assert.All(result.Value.Items, item => Assert.Equal(["title"], item.Payload.Fields!.Keys.ToArray()));
    }

    [Fact]
    public async Task SupportedFilterNodeShapesAreApplied()
    {
        if (!Capabilities.Mutation.Create || !Capabilities.Read.List || Capabilities.Query.Filter.Supported != true)
        {
            return;
        }

        var store = await CreateStoreAsync();
        await CreateRecordAsync(
            store,
            "filter-a",
            ("title", RecordStoreConformanceData.StringElement("a")),
            ("status", RecordStoreConformanceData.StringElement("open")),
            ("rank", RecordStoreConformanceData.Element("1")),
            ("enabled", RecordStoreConformanceData.Element("true")),
            ("tags", RecordStoreConformanceData.Element("""["red","blue"]""")),
            ("profile", RecordStoreConformanceData.Element("""{"name":"ada"}""")),
            ("nullable", RecordStoreConformanceData.Element("null")));
        await CreateRecordAsync(
            store,
            "filter-b",
            ("title", RecordStoreConformanceData.StringElement("b")),
            ("status", RecordStoreConformanceData.StringElement("closed")),
            ("rank", RecordStoreConformanceData.Element("3")),
            ("enabled", RecordStoreConformanceData.Element("false")),
            ("tags", RecordStoreConformanceData.Element("""["green"]""")));

        await AssertFilterIds(store, new FilterExpression { Kind = FilterNodeKind.True }, "filter-a", "filter-b");
        await AssertFilterIds(store, new FilterExpression { Kind = FilterNodeKind.False });

        if (Capabilities.Query.Filter.BooleanComposition)
        {
            await AssertFilterIds(
                store,
                new FilterExpression
                {
                    Kind = FilterNodeKind.And,
                    Children =
                    [
                        RecordStoreConformanceQueries.Equal("status", "open"),
                        new FilterExpression
                        {
                            Kind = FilterNodeKind.Compare,
                            Field = "enabled",
                            Operator = FilterOperator.Equal,
                            Value = RecordStoreConformanceQueries.Boolean(true)
                        }
                    ]
                },
                "filter-a");

            await AssertFilterIds(
                store,
                new FilterExpression
                {
                    Kind = FilterNodeKind.Or,
                    Children =
                    [
                        RecordStoreConformanceQueries.Equal("title", "a"),
                        RecordStoreConformanceQueries.Equal("title", "b")
                    ]
                },
                "filter-a",
                "filter-b");
        }

        if (Capabilities.Query.Filter.Not)
        {
            await AssertFilterIds(
                store,
                new FilterExpression
                {
                    Kind = FilterNodeKind.Not,
                    Children = [RecordStoreConformanceQueries.Equal("status", "closed")]
                },
                "filter-a");
        }

        if (Capabilities.Query.Filter.Operators?.Contains(FilterOperator.GreaterThan) == true)
        {
            await AssertFilterIds(
                store,
                new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "rank",
                    Operator = FilterOperator.GreaterThan,
                    Value = RecordStoreConformanceQueries.Integer(2)
                },
                "filter-b");
        }

        await AssertFilterIds(
            store,
            new FilterExpression
            {
                Kind = FilterNodeKind.In,
                Field = "status",
                Values = [RecordStoreConformanceQueries.String("open")]
            },
            "filter-a");

        await AssertFilterIds(
            store,
            new FilterExpression
            {
                Kind = FilterNodeKind.Between,
                Field = "rank",
                Values = [RecordStoreConformanceQueries.Integer(1), RecordStoreConformanceQueries.Integer(2)]
            },
            "filter-a");

        if (Capabilities.Query.Filter.NullChecks)
        {
            await AssertFilterIds(
                store,
                new FilterExpression { Kind = FilterNodeKind.IsNull, Field = "nullable" },
                "filter-a");
        }

        if (Capabilities.Query.Filter.MissingFieldChecks)
        {
            await AssertFilterIds(
                store,
                new FilterExpression { Kind = FilterNodeKind.IsDefined, Field = "profile" },
                "filter-a");
        }

        if (Capabilities.Query.Filter.NestedFieldPaths)
        {
            await AssertFilterIds(
                store,
                new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "profile.name",
                    Operator = FilterOperator.Equal,
                    Value = RecordStoreConformanceQueries.String("ada")
                },
                "filter-a");
        }

        if (Capabilities.Query.Filter.ArrayMembership)
        {
            await AssertFilterIds(
                store,
                new FilterExpression
                {
                    Kind = FilterNodeKind.In,
                    Field = "tags",
                    Values = [RecordStoreConformanceQueries.String("red")]
                },
                "filter-a");
        }
    }

    [Fact]
    public async Task UnsupportedIncludeExtensionDependencyAndOperatorsFailClosed()
    {
        if (!Capabilities.Read.List)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var include = await store.ListAsync(
            Collection,
            new RecordQuery { Include = [new RecordInclude { NavigationId = "relation" }] },
            Operation(BaseOperationKind.List));
        if (Capabilities.Query.Include?.Supported != true)
        {
            RecordStoreConformanceAssertions.Failure(include, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        var extension = await store.ListAsync(
            Collection,
            new RecordQuery { Extensions = [new QueryExtension { ModuleId = "test", Name = "native" }] },
            Operation(BaseOperationKind.List));
        RecordStoreConformanceAssertions.Failure(extension, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);

        if (Capabilities.Query.Filter.Operators?.Contains(FilterOperator.Like) != true)
        {
            var unsupportedOperator = await store.ListAsync(
                Collection,
                new RecordQuery { Filter = RecordStoreConformanceQueries.UnsupportedLike("title", "a") },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(unsupportedOperator, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        foreach (var mode in Enum.GetValues<QueryCountMode>().Where(mode => Capabilities.Query.Count.SupportedModes?.Contains(mode) != true))
        {
            var unsupportedCount = await store.ListAsync(
                Collection,
                new RecordQuery { Count = mode },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(unsupportedCount, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }
    }

    [Fact]
    public async Task PaginationModesAreDeterministicWhenAdvertised()
    {
        if (!Capabilities.Mutation.Create || !Capabilities.Read.List || !Capabilities.Query.Pagination.Page)
        {
            return;
        }

        var store = await CreateStoreAsync();
        await CreateRecordAsync(store, "page-a", ("title", "a"));
        await CreateRecordAsync(store, "page-b", ("title", "b"));
        await CreateRecordAsync(store, "page-c", ("title", "c"));

        var first = await store.ListAsync(
            Collection,
            new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 2 }, Count = QueryCountMode.None },
            Operation(BaseOperationKind.List));
        RecordStoreConformanceAssertions.Success(first, OperationStatus.Ok);
        Assert.Equal(2, first.Value!.Items.Length);
        Assert.True(first.Value.Page.HasMore);

        if (Capabilities.Query.Pagination.Cursor)
        {
            Assert.False(string.IsNullOrWhiteSpace(first.Value.Page.NextCursor));
            var second = await store.ListAsync(
                Collection,
                new RecordQuery
                {
                    Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 2, Cursor = first.Value.Page.NextCursor },
                    Count = QueryCountMode.None
                },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Success(second, OperationStatus.Ok);
            Assert.Single(second.Value!.Items);

            var malformed = await store.ListAsync(
                Collection,
                new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 2, Cursor = "not-a-valid-cursor" } },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(malformed, OperationStatus.ValidationFailed, OperationStatus.Unsupported);

            var shapeMismatch = await store.ListAsync(
                Collection,
                new RecordQuery
                {
                    Filter = RecordStoreConformanceQueries.Equal("title", "a"),
                    Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 2, Cursor = first.Value.Page.NextCursor }
                },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(shapeMismatch, OperationStatus.ValidationFailed, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
        }

        if (Capabilities.Query.Pagination.Offset)
        {
            var offset = await store.ListAsync(
                Collection,
                new RecordQuery
                {
                    Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 1, Limit = 1 },
                    Count = QueryCountMode.None
                },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Success(offset, OperationStatus.Ok);
            Assert.Single(offset.Value!.Items);
            Assert.Equal(1, offset.Value.Page.Offset);
        }

        if (Capabilities.Query.Pagination.MaxLimit < int.MaxValue)
        {
            var overLimit = await store.ListAsync(
                Collection,
                new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = Capabilities.Query.Pagination.MaxLimit + 1 } },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(overLimit, OperationStatus.ValidationFailed, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
        }
    }

    [Fact]
    public async Task SelectAndSortLimitsFollowCapabilities()
    {
        if (!Capabilities.Mutation.Create || !Capabilities.Read.List)
        {
            return;
        }

        var store = await CreateStoreAsync();
        await CreateRecordAsync(
            store,
            "select-a",
            ("title", RecordStoreConformanceData.StringElement("a")),
            ("profile", RecordStoreConformanceData.Element("""{"name":"ada","hidden":"x"}""")),
            ("rank", RecordStoreConformanceData.Element("1")));

        if (Capabilities.Query.Select.PayloadFields && Capabilities.Query.Select.NestedFieldPaths)
        {
            var selected = await store.ListAsync(
                Collection,
                new RecordQuery { Select = ["profile.name"], Count = QueryCountMode.None },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Success(selected, OperationStatus.Ok);
            var item = Assert.Single(selected.Value!.Items);
            Assert.Equal(["profile"], item.Payload.Fields!.Keys.ToArray());
            Assert.Equal("ada", item.Payload.Fields["profile"].GetProperty("name").GetString());
            Assert.False(item.Payload.Fields["profile"].TryGetProperty("hidden", out _));
        }
        else
        {
            var selected = await store.ListAsync(
                Collection,
                new RecordQuery { Select = ["profile.name"], Count = QueryCountMode.None },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(selected, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (Capabilities.Query.Sort.Supported && Capabilities.Query.Sort.MaxFields is { } maxFields && maxFields < 32)
        {
            var sort = Enumerable.Range(0, maxFields + 1)
                .Select(index => new QuerySort($"field{index}"))
                .ToArray();
            var tooManySorts = await store.ListAsync(
                Collection,
                new RecordQuery { Sort = sort, Count = QueryCountMode.None },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(tooManySorts, OperationStatus.ValidationFailed, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
        }

        if (!Capabilities.Query.Sort.Supported)
        {
            var sort = await store.ListAsync(
                Collection,
                new RecordQuery { Sort = [new QuerySort("rank")], Count = QueryCountMode.None },
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(sort, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }
    }

    private async Task AssertFilterIds(IRecordStore store, FilterExpression filter, params string[] expectedIds)
    {
        var result = await store.ListAsync(
            Collection,
            new RecordQuery { Filter = filter, Count = QueryCountMode.None },
            Operation(BaseOperationKind.List));

        RecordStoreConformanceAssertions.Success(result, OperationStatus.Ok);
        Assert.Equal(expectedIds, result.Value!.Items.Select(item => item.Id.Value).ToArray());
    }
}
