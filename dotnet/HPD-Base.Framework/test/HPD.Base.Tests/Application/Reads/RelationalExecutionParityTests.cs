using System.Text.Json.Serialization;
using System.Text.Json;
using FluentAssertions;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Application.Reads;

public sealed class RelationalExecutionParityTests
{
    [Theory]
    [InlineData(BaseJoinKind.Inner, "left-1:right-1,left-1:right-2")]
    [InlineData(BaseJoinKind.Left, "left-1:right-1,left-1:right-2,left-2:null")]
    [InlineData(BaseJoinKind.Semi, "left-1")]
    [InlineData(BaseJoinKind.Anti, "left-2")]
    public async Task InMemoryAndSqliteSharePortableJoinMultiplicityAndOrdering(BaseJoinKind kind, string expected)
    {
        string[] inMemoryRows = await ExecuteJoinAsync(sqlitePath: null, kind);
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-relational-join-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            string[] sqliteRows = await ExecuteJoinAsync(path, kind);
            sqliteRows.Should().Equal(inMemoryRows);
            inMemoryRows.Should().Equal(expected.Split(','));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task InMemoryAndSqliteShareNumericBooleanEmptyAndNullAggregateSemantics()
    {
        BaseRelationalFieldValue[] inMemoryResult = await ExecuteAsync(sqlitePath: null, seed: true);
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-relational-parity-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            BaseRelationalFieldValue[] sqliteResult = await ExecuteAsync(path, seed: true);
            sqliteResult.Should().BeEquivalentTo(inMemoryResult, options => options.WithStrictOrdering());
            Value(inMemoryResult, "minimum").Integer.Should().Be(2);
            Value(inMemoryResult, "maximum").Integer.Should().Be(10);
            Value(inMemoryResult, "sum").Integer.Should().Be(12);
            Value(inMemoryResult, "any").Boolean.Should().BeTrue();
            Value(inMemoryResult, "all").Boolean.Should().BeTrue();
            Value(inMemoryResult, "decimalMinimum").Decimal.Should().Be("2");
            Value(inMemoryResult, "decimalMaximum").Decimal.Should().Be("10");
            Value(inMemoryResult, "decimalSum").Decimal.Should().Be("12");
            Value(inMemoryResult, "decimalAverage").Decimal.Should().Be("6");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task InMemoryAndSqliteReturnOnePortableAggregateRowForAnEmptySource()
    {
        BaseRelationalFieldValue[] inMemoryResult = await ExecuteAsync(sqlitePath: null, seed: false);
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-relational-empty-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            BaseRelationalFieldValue[] sqliteResult = await ExecuteAsync(path, seed: false);
            sqliteResult.Should().BeEquivalentTo(inMemoryResult, options => options.WithStrictOrdering());
            Value(inMemoryResult, "minimum").Kind.Should().Be(QueryValueKind.Null);
            Value(inMemoryResult, "maximum").Kind.Should().Be(QueryValueKind.Null);
            Value(inMemoryResult, "sum").Integer.Should().Be(0);
            Value(inMemoryResult, "any").Boolean.Should().BeFalse();
            Value(inMemoryResult, "all").Boolean.Should().BeTrue();
            Value(inMemoryResult, "decimalSum").Decimal.Should().Be("0");
            Value(inMemoryResult, "decimalAverage").Kind.Should().Be(QueryValueKind.Null);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task TypedGroupHavingDistinctCountAndPageMatchAcrossProviders()
    {
        BasePage<GroupedPageRead.Row> inMemoryPage = await ExecuteGroupedPageAsync(sqlitePath: null);
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-relational-group-page-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            BasePage<GroupedPageRead.Row> sqlitePage = await ExecuteGroupedPageAsync(path);
            sqlitePage.Should().BeEquivalentTo(inMemoryPage);
            inMemoryPage.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Category = "B", Total = 8L });
            inMemoryPage.Count!.Total.Should().Be(3);
            inMemoryPage.Page.HasMore.Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task DeclaredArbitraryOffsetMatchesAcrossProviders()
    {
        BasePage<GroupedPageRead.Row> inMemoryPage = await ExecuteGroupedPageAsync(sqlitePath: null, offset: true);
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-relational-offset-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            BasePage<GroupedPageRead.Row> sqlitePage = await ExecuteGroupedPageAsync(path, offset: true);
            sqlitePage.Should().BeEquivalentTo(inMemoryPage);
            inMemoryPage.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Category = "B", Total = 8L });
            inMemoryPage.Page.Should().BeEquivalentTo(new PageInfo { Offset = 1, Limit = 1, HasMore = true });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task NullAndMissingPredicatesHaveExactPortableSemantics()
    {
        Dictionary<string, string[]> inMemoryResults = await ExecuteNullPredicatesAsync(sqlitePath: null);
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-relational-null-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            Dictionary<string, string[]> sqliteResults = await ExecuteNullPredicatesAsync(path);
            sqliteResults.Should().BeEquivalentTo(inMemoryResults, options => options.WithStrictOrdering());
            inMemoryResults["defined"].Should().Equal("null", "text");
            inMemoryResults["null"].Should().Equal("null");
            inMemoryResults["equal-null"].Should().Equal("null");
            inMemoryResults["not-equal-text"].Should().Equal("null");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task OrdinalStringPredicatesHaveExactPortableSemantics()
    {
        Dictionary<string, string[]> inMemory = await ExecuteStringPredicatesAsync(sqlitePath: null);
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-relational-string-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            Dictionary<string, string[]> sqlite = await ExecuteStringPredicatesAsync(path);
            sqlite.Should().BeEquivalentTo(inMemory, options => options.WithStrictOrdering());
            inMemory["contains"].Should().Equal("alphabet", "lower");
            inMemory["starts"].Should().Equal("alphabet");
            inMemory["ends"].Should().Equal("wildcards");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task DateTimeOrderingAndComparisonAreChronologicalAcrossProviders()
    {
        string[] inMemoryIds = await ExecuteDateTimesAsync(sqlitePath: null);
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-relational-datetime-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            string[] sqliteIds = await ExecuteDateTimesAsync(path);
            sqliteIds.Should().Equal(inMemoryIds);
            inMemoryIds.Should().Equal("early", "middle", "late");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static async Task<string[]> ExecuteDateTimesAsync(string? sqlitePath)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddTestPolicyAuthority<AllowPolicyEvaluator>();
            builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x77, 32).ToArray())
                .AddCollection(DateTimeParityRecord.Collection)
                .AddRead(DateTimeParityRead.Definition)
                .AddRead(NullableDateTimeParityRead.Definition);
            if (sqlitePath is not null) builder.UseStore(SqliteStore.Configure(options => options.DataSource = sqlitePath));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        if (sqlitePath is not null)
        {
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan schema = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = schema.ProtectedArtifact })).IsSuccess().Should().BeTrue();
        }
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });
        foreach ((string id, DateTimeOffset time) in new[]
        {
            ("late", new DateTimeOffset(2026, 8, 2, 18, 0, 0, TimeSpan.Zero)),
            ("early", new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)),
            ("middle", new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)),
        })
            (await session.Collection(DateTimeParityRecord.Collection).CreateAsync(RecordId.Create(id), new DateTimeParityRecord { OccurredAt = time })).Should().BeOfType<BaseSuccess<BaseRecord<DateTimeParityRecord>>>();

        BasePage<DateTimeParityRead.Row> result = (await session.Reads.ExecuteAsync(
            DateTimeParityRead.Handle,
            new DateTimeParityRead { After = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero) },
            BaseReadPageRequest.Create(1, 10))).RequireValue();
        BasePage<NullableDateTimeParityRead.Row> withoutBoundary = (await session.Reads.ExecuteAsync(
            NullableDateTimeParityRead.Handle,
            new NullableDateTimeParityRead { After = null },
            BaseReadPageRequest.Create(1, 10))).RequireValue();
        withoutBoundary.Items.Select(static row => row.Id.Value.Value)
            .Should().Equal("early", "middle", "late");
        result.Items.Select(static row => row.OccurredAt.Offset).Should().OnlyContain(offset => offset == TimeSpan.Zero);
        result.Items.Select(static row => row.Revision.Value).Should().OnlyContain(static value => !string.IsNullOrWhiteSpace(value));
        return result.Items.Select(static row => row.Id.Value.Value).ToArray();
    }

    private static async Task<Dictionary<string, string[]>> ExecuteNullPredicatesAsync(string? sqlitePath)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddTestPolicyAuthority<AllowPolicyEvaluator>();
            builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x76, 32).ToArray())
                .AddCollection(NullableParityRecord.Collection);
            if (sqlitePath is not null) builder.UseStore(SqliteStore.Configure(options => options.DataSource = sqlitePath));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        if (sqlitePath is not null)
        {
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan schema = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = schema.ProtectedArtifact })).IsSuccess().Should().BeTrue();
        }
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        IRecordStore store = provider.GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(NullableParityRecord.Collection.Id)!;
        string prefix = Guid.NewGuid().ToString("N") + "-";
        foreach ((string id, string json) in new[] { ("missing", "{\"Marker\":\"m\"}"), ("null", "{\"Marker\":\"m\",\"Value\":null}"), ("text", "{\"Marker\":\"m\",\"Value\":\"x\"}") })
        {
            using JsonDocument document = JsonDocument.Parse(json);
            OperationResult<RecordEnvelope> created = await store.CreateAsync(
                NullableParityRecord.Collection.Definition,
                new RecordCreateRequest { RequestedId = RecordId.Create(prefix + id), Payload = new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() } },
                new OperationContext { Operation = BaseOperationKind.Create, CollectionId = NullableParityRecord.Collection.Id, RecordId = prefix + id });
            created.IsSuccess().Should().BeTrue(id + ":" + created.Error?.Code);
        }
        long generation = provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness.SchemaGeneration ?? 0;
        BaseRelationalOperand field = new() { Kind = BaseRelationalOperandKind.SourceField, SourceId = "values", FieldId = NullableParityRecord.Fields.Value.Id };
        BaseRelationalOperand nullValue = new() { Kind = BaseRelationalOperandKind.Literal, Literal = new QueryValue { Kind = QueryValueKind.Null } };
        BaseRelationalOperand textValue = new() { Kind = BaseRelationalOperandKind.Literal, Literal = new QueryValue { Kind = QueryValueKind.String, String = "x" } };
        var predicates = new Dictionary<string, BaseRelationalPredicate>(StringComparer.Ordinal)
        {
            ["defined"] = new() { Kind = FilterNodeKind.IsDefined, Left = field },
            ["null"] = new() { Kind = FilterNodeKind.IsNull, Left = field },
            ["equal-null"] = new() { Kind = FilterNodeKind.Compare, Operator = FilterOperator.Equal, Left = field, Right = nullValue },
            ["not-equal-text"] = new() { Kind = FilterNodeKind.Compare, Operator = FilterOperator.NotEqual, Left = field, Right = textValue },
        };
        var results = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach ((string name, BaseRelationalPredicate predicate) in predicates)
        {
            var plan = new BaseRelationalReadPlan
            {
                Id = "null-" + name, Topology = BaseRelationalReadTopology.Ordinary, SchemaGeneration = generation,
                Sources = [new BaseRelationalReadSource { Id = "values", CollectionId = NullableParityRecord.Collection.Id }],
                Predicate = predicate,
                Projection = [new BaseRelationalReadProjection { FieldId = "id", Operand = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = "values", FieldId = "base.recordId" } }],
                Sort = [new BaseRelationalReadSort { Operand = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = "values", FieldId = "base.recordId" } }],
                Parameters = [], Budgets = new BaseRelationalReadBudgets { MaxResultRows = 10, MaxResultBytes = 10_000, MaxOperations = 20, MaxExecutionMilliseconds = 2_000, MaxCompoundBranches = 0, MaxCompoundOperations = 0 },
                Pagination = new BaseRegisteredReadPaginationAuthority { Mode = BaseRegisteredReadPaginationMode.PageOnly, MaximumOffset = 0 },
            };
            OperationResult<BaseRelationalReadExecutionResult> result = await ((IRelationalReadStore)store).ExecuteReadAsync(new BaseRelationalReadExecutionRequest
            {
                ApplicationId = ProviderAuthority(provider, NullableParityRecord.Collection.Id).ApplicationId,
                LogicalStoreId = ProviderAuthority(provider, NullableParityRecord.Collection.Id).LogicalStoreId,
                LogicalSchemaChecksum = ProviderAuthority(provider, NullableParityRecord.Collection.Id).LogicalSchemaChecksum,
                Plan = plan, ParameterValues = [], SourcePolicies = [new BaseRelationalReadSourcePolicy { SourceId = "values", CollectionId = NullableParityRecord.Collection.Id }],
                Operation = new OperationContext { Operation = BaseOperationKind.Query, CollectionId = NullableParityRecord.Collection.Id },
                AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1), MaxResultRows = 10, MaxResultBytes = 10_000,
            });
            result.IsSuccess().Should().BeTrue(result.Error?.Message);
            results[name] = result.Value!.Result.Rows.Select(row => Value(row.Fields, "id").Id![prefix.Length..]).ToArray();
        }
        return results;
    }

    private static async Task<Dictionary<string, string[]>> ExecuteStringPredicatesAsync(string? sqlitePath)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddTestPolicyAuthority<AllowPolicyEvaluator>();
            builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x78, 32).ToArray())
                .AddCollection(NullableParityRecord.Collection);
            if (sqlitePath is not null) builder.UseStore(SqliteStore.Configure(options => options.DataSource = sqlitePath));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        if (sqlitePath is not null)
        {
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan schema = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = schema.ProtectedArtifact })).IsSuccess().Should().BeTrue();
        }
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });
        foreach ((string id, string value) in new[]
        {
            ("alphabet", "Alphabet"), ("lower", "alphabet"), ("wildcards", "literal%_suffix"),
        })
            (await session.Collection(NullableParityRecord.Collection).CreateAsync(
                RecordId.Create(id), new NullableParityRecord { Marker = "m", Value = value }))
                .Should().BeOfType<BaseSuccess<BaseRecord<NullableParityRecord>>>();

        IRelationalReadStore store = (IRelationalReadStore)provider.GetRequiredService<IRecordStoreRegistry>()
            .GetStoreForCollection(NullableParityRecord.Collection.Id)!;
        long generation = provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness.SchemaGeneration ?? 0;
        BaseRelationalOperand field = new() { Kind = BaseRelationalOperandKind.SourceField, SourceId = "values", FieldId = NullableParityRecord.Fields.Value.Id };
        var cases = new (string Name, FilterOperator Operator, string Value)[]
        {
            ("contains", FilterOperator.Contains, "pha"),
            ("starts", FilterOperator.StartsWith, "Al"),
            ("ends", FilterOperator.EndsWith, "%_suffix"),
        };
        var results = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach ((string name, FilterOperator operation, string value) in cases)
        {
            var plan = new BaseRelationalReadPlan
            {
                Id = "string-" + name, Topology = BaseRelationalReadTopology.Ordinary, SchemaGeneration = generation,
                Sources = [new BaseRelationalReadSource { Id = "values", CollectionId = NullableParityRecord.Collection.Id }],
                Predicate = new BaseRelationalPredicate
                {
                    Kind = FilterNodeKind.Compare, Operator = operation, Left = field,
                    Right = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Literal, Literal = new QueryValue { Kind = QueryValueKind.String, String = value } },
                },
                Projection = [new BaseRelationalReadProjection { FieldId = "id", Operand = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = "values", FieldId = "base.recordId" } }],
                Sort = [new BaseRelationalReadSort { Operand = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = "values", FieldId = "base.recordId" } }],
                Parameters = [], Budgets = new BaseRelationalReadBudgets { MaxResultRows = 10, MaxResultBytes = 10_000, MaxOperations = 20, MaxExecutionMilliseconds = 2_000, MaxCompoundBranches = 0, MaxCompoundOperations = 0 },
                Pagination = new BaseRegisteredReadPaginationAuthority { Mode = BaseRegisteredReadPaginationMode.PageOnly, MaximumOffset = 0 },
            };
            var authority = ProviderAuthority(provider, NullableParityRecord.Collection.Id);
            OperationResult<BaseRelationalReadExecutionResult> result = await store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
            {
                ApplicationId = authority.ApplicationId, LogicalStoreId = authority.LogicalStoreId,
                LogicalSchemaChecksum = authority.LogicalSchemaChecksum, Plan = plan, ParameterValues = [],
                SourcePolicies = [new BaseRelationalReadSourcePolicy { SourceId = "values", CollectionId = NullableParityRecord.Collection.Id }],
                Operation = new OperationContext { Operation = BaseOperationKind.Query, CollectionId = NullableParityRecord.Collection.Id },
                AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1), MaxResultRows = 10, MaxResultBytes = 10_000,
            });
            result.IsSuccess().Should().BeTrue(result.Error?.Message);
            results[name] = result.Value!.Result.Rows.Select(row => Value(row.Fields, "id").Id!).ToArray();
        }
        return results;
    }

    private static async Task<BasePage<GroupedPageRead.Row>> ExecuteGroupedPageAsync(string? sqlitePath, bool offset = false)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddTestPolicyAuthority<AllowPolicyEvaluator>();
            builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x75, 32).ToArray())
                .AddCollection(GroupedPageRecord.Collection)
                .AddRead(GroupedPageRead.Definition);
            if (sqlitePath is not null) builder.UseStore(SqliteStore.Configure(options => options.DataSource = sqlitePath));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        if (sqlitePath is not null)
        {
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
        }
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });
        foreach ((string id, string category, int amount) in new[] { ("a1", "A", 2), ("a2", "A", 4), ("b", "B", 8), ("c", "C", 10) })
            (await session.Collection(GroupedPageRecord.Collection).CreateAsync(RecordId.Create(id), new GroupedPageRecord { Category = category, Amount = amount })).Should().BeOfType<BaseSuccess<BaseRecord<GroupedPageRecord>>>();

        var parameters = new GroupedPageRead { Amounts = [2, 4, 8, 10] };
        return offset
            ? (await session.Reads.ExecuteOffsetAsync(GroupedPageRead.Handle, parameters, BaseReadOffsetRequest.Create(1, 1))).RequireValue()
            : (await session.Reads.ExecuteAsync(GroupedPageRead.Handle, parameters, BaseReadPageRequest.Create(2, 1))).RequireValue();
    }

    private static async Task<BaseRelationalFieldValue[]> ExecuteAsync(string? sqlitePath, bool seed)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddTestPolicyAuthority<AllowPolicyEvaluator>();
            builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x73, 32).ToArray());
            builder.AddCollection(AggregateRecord.Collection);
            if (sqlitePath is not null) builder.UseStore(SqliteStore.Configure(options => options.DataSource = sqlitePath));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        if (sqlitePath is not null)
        {
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan schemaPlan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = schemaPlan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
        }
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });
        if (seed)
        {
            (await session.Collection(AggregateRecord.Collection).CreateAsync(RecordId.Create("one"), new AggregateRecord { Rank = 2, Price = 2m, Flag = true })).Should().BeOfType<BaseSuccess<BaseRecord<AggregateRecord>>>();
            (await session.Collection(AggregateRecord.Collection).CreateAsync(RecordId.Create("two"), new AggregateRecord { Rank = 10, Price = 10m, Flag = null })).Should().BeOfType<BaseSuccess<BaseRecord<AggregateRecord>>>();
        }

        IRelationalReadStore store = (IRelationalReadStore)provider.GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(AggregateRecord.Collection.Id)!;
        long generation = provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness.SchemaGeneration ?? 0;
        BaseRelationalReadPlan plan = Plan(generation);
        OperationResult<BaseRelationalReadExecutionResult> result = await store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
        {
            ApplicationId = ProviderAuthority(provider, AggregateRecord.Collection.Id).ApplicationId,
            LogicalStoreId = ProviderAuthority(provider, AggregateRecord.Collection.Id).LogicalStoreId,
            LogicalSchemaChecksum = ProviderAuthority(provider, AggregateRecord.Collection.Id).LogicalSchemaChecksum,
            Plan = plan,
            ParameterValues = [],
            SourcePolicies = [new BaseRelationalReadSourcePolicy { SourceId = "values", CollectionId = AggregateRecord.Collection.Id }],
            Operation = new OperationContext { Operation = BaseOperationKind.Query, CollectionId = AggregateRecord.Collection.Id },
            AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1),
            MaxResultRows = 10, MaxResultBytes = 10_000,
        });
        result.IsSuccess().Should().BeTrue(result.Error?.Message);
        return result.Value!.Result.Rows.Should().ContainSingle().Subject.Fields;
    }

    private static QueryValue Value(BaseRelationalFieldValue[] fields, string id) => fields.Single(field => field.FieldId == id).Value;

    private static async Task<string[]> ExecuteJoinAsync(string? sqlitePath, BaseJoinKind kind)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddTestPolicyAuthority<AllowPolicyEvaluator>();
            builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x74, 32).ToArray());
            builder.AddCollection(JoinLeft.Collection).AddCollection(JoinRight.Collection);
            if (sqlitePath is not null) builder.UseStore(SqliteStore.Configure(options => options.DataSource = sqlitePath));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        if (sqlitePath is not null)
        {
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan schemaPlan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = schemaPlan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
        }
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });
        (await session.Collection(JoinLeft.Collection).CreateAsync(RecordId.Create("left-1"), new JoinLeft { Key = "shared" })).Should().BeOfType<BaseSuccess<BaseRecord<JoinLeft>>>();
        (await session.Collection(JoinLeft.Collection).CreateAsync(RecordId.Create("left-2"), new JoinLeft { Key = "missing" })).Should().BeOfType<BaseSuccess<BaseRecord<JoinLeft>>>();
        (await session.Collection(JoinRight.Collection).CreateAsync(RecordId.Create("right-2"), new JoinRight { Key = "shared" })).Should().BeOfType<BaseSuccess<BaseRecord<JoinRight>>>();
        (await session.Collection(JoinRight.Collection).CreateAsync(RecordId.Create("right-1"), new JoinRight { Key = "shared" })).Should().BeOfType<BaseSuccess<BaseRecord<JoinRight>>>();

        IRelationalReadStore store = (IRelationalReadStore)provider.GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(JoinLeft.Collection.Id)!;
        long generation = provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness.SchemaGeneration ?? 0;
        BaseRelationalOperand leftId = new() { Kind = BaseRelationalOperandKind.RecordId, SourceId = "left", FieldId = "base.recordId" };
        BaseRelationalOperand rightId = new() { Kind = BaseRelationalOperandKind.RecordId, SourceId = "right", FieldId = "base.recordId" };
        BaseRelationalReadProjection[] projection = kind is BaseJoinKind.Semi or BaseJoinKind.Anti
            ? [new() { FieldId = "left", Operand = leftId }]
            : [new() { FieldId = "left", Operand = leftId }, new() { FieldId = "right", Operand = rightId }];
        var plan = new BaseRelationalReadPlan
        {
            Id = "join-" + kind, Topology = BaseRelationalReadTopology.Ordinary, SchemaGeneration = generation,
            Sources = [new() { Id = "left", CollectionId = JoinLeft.Collection.Id }, new() { Id = "right", CollectionId = JoinRight.Collection.Id }],
            Joins = [new()
            {
                Kind = kind,
                Left = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.SourceField, SourceId = "left", FieldId = JoinLeft.Fields.Key.Id },
                Right = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.SourceField, SourceId = "right", FieldId = JoinRight.Fields.Key.Id },
            }],
            Projection = projection,
            Sort = [new() { Operand = leftId }, .. (kind is BaseJoinKind.Semi or BaseJoinKind.Anti ? [] : new[] { new BaseRelationalReadSort { Operand = rightId } })],
            Parameters = [],
            Budgets = new() { MaxResultRows = 20, MaxResultBytes = 10_000, MaxOperations = 100, MaxExecutionMilliseconds = 2_000, MaxCompoundBranches = 0, MaxCompoundOperations = 0 },
            Pagination = new() { Mode = BaseRegisteredReadPaginationMode.PageOnly, MaximumOffset = 0 },
        };
        OperationResult<BaseRelationalReadExecutionResult> result = await store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
        {
            ApplicationId = ProviderAuthority(provider, JoinLeft.Collection.Id).ApplicationId,
            LogicalStoreId = ProviderAuthority(provider, JoinLeft.Collection.Id).LogicalStoreId,
            LogicalSchemaChecksum = ProviderAuthority(provider, JoinLeft.Collection.Id).LogicalSchemaChecksum,
            Plan = plan, ParameterValues = [],
            SourcePolicies = [new() { SourceId = "left", CollectionId = JoinLeft.Collection.Id }, new() { SourceId = "right", CollectionId = JoinRight.Collection.Id }],
            Operation = new() { Operation = BaseOperationKind.Query, CollectionId = JoinLeft.Collection.Id },
            AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1), MaxResultRows = 20, MaxResultBytes = 10_000,
        });
        result.IsSuccess().Should().BeTrue(result.Error?.Message);
        return result.Value!.Result.Rows.Select(row =>
        {
            BaseRelationalFieldValue[] fields = row.Fields;
            string left = Value(fields, "left").Id!;
            return fields.Length == 1 ? left : left + ":" + (Value(fields, "right").Id ?? "null");
        }).ToArray();
    }

    private static BaseRelationalReadPlan Plan(long generation)
    {
        BaseRelationalOperand rank = new() { Kind = BaseRelationalOperandKind.SourceField, SourceId = "values", FieldId = AggregateRecord.Fields.Rank.Id };
        BaseRelationalOperand price = new() { Kind = BaseRelationalOperandKind.SourceField, SourceId = "values", FieldId = AggregateRecord.Fields.Price.Id };
        BaseRelationalOperand flag = new() { Kind = BaseRelationalOperandKind.SourceField, SourceId = "values", FieldId = AggregateRecord.Fields.Flag.Id };
        BaseRelationalReadAggregate[] aggregates =
        [
            new() { Id = "minimum", Kind = BaseAggregateKind.Minimum, Operand = rank },
            new() { Id = "maximum", Kind = BaseAggregateKind.Maximum, Operand = rank },
            new() { Id = "sum", Kind = BaseAggregateKind.Sum, Operand = rank },
            new() { Id = "any", Kind = BaseAggregateKind.Any, Operand = flag },
            new() { Id = "all", Kind = BaseAggregateKind.All, Operand = flag },
            new() { Id = "decimalMinimum", Kind = BaseAggregateKind.Minimum, Operand = price },
            new() { Id = "decimalMaximum", Kind = BaseAggregateKind.Maximum, Operand = price },
            new() { Id = "decimalSum", Kind = BaseAggregateKind.Sum, Operand = price },
            new() { Id = "decimalAverage", Kind = BaseAggregateKind.Average, Operand = price },
        ];
        return new BaseRelationalReadPlan
        {
            Id = "aggregate-parity", Topology = BaseRelationalReadTopology.Ordinary, SchemaGeneration = generation,
            Sources = [new BaseRelationalReadSource { Id = "values", CollectionId = AggregateRecord.Collection.Id }],
            Aggregates = aggregates,
            Projection = aggregates.Select(item => new BaseRelationalReadProjection
            {
                FieldId = item.Id,
                Operand = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Aggregate, AggregateId = item.Id }
            }).ToArray(),
            Parameters = [],
            Pagination = new() { Mode = BaseRegisteredReadPaginationMode.PageOnly, MaximumOffset = 0 },
            Budgets = new BaseRelationalReadBudgets { MaxResultRows = 10, MaxResultBytes = 10_000, MaxOperations = 100, MaxExecutionMilliseconds = 2_000, MaxCompoundBranches = 0, MaxCompoundOperations = 0 },
        };
    }

    private static (string ApplicationId, string LogicalStoreId, BaseSchemaAuthorityChecksum LogicalSchemaChecksum)
        ProviderAuthority(IServiceProvider provider, string collectionId)
    {
        HPDBaseInstalledFeatures installed = provider.GetRequiredService<HPDBaseInstalledFeatures>();
        RecordStoreRegistration registration = provider.GetRequiredService<IRecordStoreRegistry>()
            .GetRegistrationForCollection(collectionId)!;
        return (installed.LogicalSchema.ApplicationId, registration.StoreId,
            BaseSchemaAuthorityChecksum.ParseHex(installed.LogicalSchema.CanonicalChecksum));
    }
}

[BaseCollection("aggregate-parity-values", typeof(AggregateParityJsonContext))]
internal sealed partial record AggregateRecord
{
    [BaseField("aggregate.rank")]
    public required int Rank { get; init; }

    [BaseField("aggregate.price")]
    public required decimal Price { get; init; }

    [BaseField("aggregate.flag")]
    public bool? Flag { get; init; }
}

[JsonSerializable(typeof(AggregateRecord))]
internal sealed partial class AggregateParityJsonContext : JsonSerializerContext;

[BaseCollection("join-parity-left", typeof(RelationalParityJsonContext))]
internal sealed partial record JoinLeft
{
    [BaseField("join.left.key")]
    public required string Key { get; init; }
}

[BaseCollection("join-parity-right", typeof(RelationalParityJsonContext))]
internal sealed partial record JoinRight
{
    [BaseField("join.right.key")]
    public required string Key { get; init; }
}

[JsonSerializable(typeof(JoinLeft))]
[JsonSerializable(typeof(JoinRight))]
internal sealed partial class RelationalParityJsonContext : JsonSerializerContext;

[BaseCollection("grouped-page-values", typeof(GroupedPageJsonContext))]
internal sealed partial record GroupedPageRecord
{
    [BaseField("grouped.category")]
    public required string Category { get; init; }

    [BaseField("grouped.amount")]
    public required int Amount { get; init; }
}

[BaseRead("grouped-page", typeof(GroupedPageJsonContext), RequiredGrantId = "grouped-page.execute")]
internal sealed partial record GroupedPageRead
{
    [BaseReadParameter("grouped-page.amounts")]
    public required int[] Amounts { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("grouped-page.category")]
        public required string Category { get; init; }

        [BaseReadField("grouped-page.total")]
        public required long Total { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<GroupedPageRead, Row> read)
    {
        read.From(GroupedPageRecord.Collection, "values", out var value)
            .Where(value.Field(GroupedPageRecord.Fields.Amount).In(read.Parameter(Parameters.Amounts)))
            .GroupBy(value.Field(GroupedPageRecord.Fields.Category))
            .Project(Row.Fields.Category, value.Field(GroupedPageRecord.Fields.Category))
            .Aggregate(Row.Fields.Total, BaseAggregate.Sum(value.Field(GroupedPageRecord.Fields.Amount)), out var total)
            .Having(total.GreaterThan(read.Literal(5L)))
            .Distinct()
            .OrderBy(total, QuerySortDirection.Desc)
            .AllowOffsetPagination(100_000);
    }
}

[JsonSerializable(typeof(GroupedPageRecord))]
[JsonSerializable(typeof(GroupedPageRead))]
[JsonSerializable(typeof(GroupedPageRead.Row), TypeInfoPropertyName = "GroupedPageReadRow")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class GroupedPageJsonContext : JsonSerializerContext;

[BaseCollection("nullable-parity-values", typeof(NullableParityJsonContext))]
internal sealed partial record NullableParityRecord
{
    [BaseField("nullable.marker")]
    public required string Marker { get; init; }

    [BaseField("nullable.value")]
    public string? Value { get; init; }
}

[JsonSerializable(typeof(NullableParityRecord))]
internal sealed partial class NullableParityJsonContext : JsonSerializerContext;

[BaseCollection("datetime-parity-values", typeof(DateTimeParityJsonContext))]
internal sealed partial record DateTimeParityRecord
{
    [BaseField("datetime.occurred-at")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset OccurredAt { get; init; }
}

[JsonSerializable(typeof(DateTimeParityRecord))]
[JsonSerializable(typeof(DateTimeParityRead))]
[JsonSerializable(typeof(DateTimeParityRead.Row), TypeInfoPropertyName = "DateTimeParityReadRow")]
[JsonSerializable(typeof(NullableDateTimeParityRead))]
[JsonSerializable(typeof(NullableDateTimeParityRead.Row), TypeInfoPropertyName = "NullableDateTimeParityReadRow")]
internal sealed partial class DateTimeParityJsonContext : JsonSerializerContext;

[BaseRead("datetime-parity", typeof(DateTimeParityJsonContext), RequiredGrantId = "datetime-parity.execute")]
internal sealed partial record DateTimeParityRead
{
    [BaseReadParameter("datetime.after")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset After { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("datetime.row.id")]
        public required BaseRecordId<DateTimeParityRecord> Id { get; init; }

        [BaseReadField("datetime.row.occurred-at")]
        [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
        public required DateTimeOffset OccurredAt { get; init; }

        [BaseReadField("datetime.row.revision")]
        public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<DateTimeParityRead, Row> read)
    {
        read.From(DateTimeParityRecord.Collection, "events", out var events)
            .Where(events.Field(DateTimeParityRecord.Fields.OccurredAt).GreaterThan(read.Parameter(Parameters.After)))
            .Project(Row.Fields.Id, events.RecordId)
            .Project(Row.Fields.OccurredAt, events.Field(DateTimeParityRecord.Fields.OccurredAt))
            .Project(Row.Fields.Revision, events.Revision)
            .OrderBy(events.Field(DateTimeParityRecord.Fields.OccurredAt));
    }
}

[BaseRead("nullable-datetime-parity", typeof(DateTimeParityJsonContext),
    RequiredGrantId = "nullable-datetime-parity.execute")]
internal sealed partial record NullableDateTimeParityRead
{
    [BaseReadParameter("nullable-datetime.after")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public DateTimeOffset? After { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("nullable-datetime.row.id")]
        public required BaseRecordId<DateTimeParityRecord> Id { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<NullableDateTimeParityRead, Row> read)
    {
        read.From(DateTimeParityRecord.Collection, "events", out var events);
        BaseReadOperand<DateTimeOffset> after = read.OptionalParameter(Parameters.After);
        read.Where(after.IsNull().Or(events.Field(DateTimeParityRecord.Fields.OccurredAt).GreaterThanOrEqual(after)))
            .Project(Row.Fields.Id, events.RecordId)
            .OrderBy(events.Field(DateTimeParityRecord.Fields.OccurredAt));
    }
}
