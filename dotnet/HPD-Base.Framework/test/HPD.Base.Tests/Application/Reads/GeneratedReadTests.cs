using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Tests.Application.Reads;

public sealed class GeneratedReadTests
{
    [Fact]
    public void Provider_result_byte_accumulators_fail_closed_on_arithmetic_overflow()
    {
        InMemoryRecordStore.TryAccumulateRelationalResultBytes(long.MaxValue, 1, out long inMemoryTotal)
            .Should().BeFalse();
        inMemoryTotal.Should().Be(0);
        SqliteRecordStore.TryAccumulateRelationalResultBytes(long.MaxValue, 1, out long sqliteTotal)
            .Should().BeFalse();
        sqliteTotal.Should().Be(0);
    }

    [Fact]
    public void InMemory_stops_projection_at_the_first_over_limit_row_and_discards_the_page()
    {
        int projections = 0;

        bool admitted = InMemoryRecordStore.TryMaterializeBoundedPage(
            new[] { 1, 2, 3 },
            maximumRows: 3,
            maximumBytes: 4,
            value =>
            {
                projections++;
                return value;
            },
            _ => 5,
            out int[] page);

        admitted.Should().BeFalse();
        projections.Should().Be(1, "projection must stop before materializing the remainder of an over-limit page");
        page.Should().BeEmpty("no partial projected page may escape");
    }

    [Fact]
    public void RegisteredReadExecutionTimeoutParticipatesInLogicalSchemaIdentity()
    {
        static BaseLogicalSchema Build(int timeoutMilliseconds)
        {
            BaseReadDefinition<ProjectSummaryRead, ProjectSummaryRead.Row> source = ProjectSummaryRead.Definition;
            var read = new BaseReadDefinition<ProjectSummaryRead, ProjectSummaryRead.Row>(
                source.Plan with
                {
                    Budgets = source.Plan.Budgets with { MaxExecutionMilliseconds = timeoutMilliseconds },
                },
                null,
                null,
                source.ParameterCodec,
                source.RowCodec,
                source.ClientContract)
            {
                Exposure = source.Exposure,
                Authorization = source.Authorization,
                Disclosure = source.Disclosure,
                SourceAuthority = source.SourceAuthority,
                Audience = source.Audience,
                RequiredGrantId = source.RequiredGrantId,
                ConfidentialOutputFieldIds = source.ConfidentialOutputFieldIds,
                SecretOutputFieldIds = source.SecretOutputFieldIds,
                SystemSourceIds = source.SystemSourceIds,
                SerializerRegistration = source.SerializerRegistration,
                ParameterDeclarations = source.ParameterDeclarations,
                RowDeclarations = source.RowDeclarations,
            };
            var services = new ServiceCollection();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => options.ApplicationId = "read-timeout-checksum")
                .AddCollection(ReadProject.Collection)
                .AddCollection(ReadOwner.Collection)
                .AddCollection(ReadTask.Collection)
                .AddRead(read));
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<BaseLogicalSchema>();
        }

        Build(1_000).CanonicalChecksum.Should().NotBe(Build(2_000).CanonicalChecksum);
    }

    [Fact]
    public void RegisteredReadPaginationAuthorityParticipatesInLogicalSchemaIdentity()
    {
        static BaseLogicalSchema Build(BaseRegisteredReadPaginationAuthority pagination)
        {
            BaseReadDefinition<ProjectSummaryRead, ProjectSummaryRead.Row> source = ProjectSummaryRead.Definition;
            var read = new BaseReadDefinition<ProjectSummaryRead, ProjectSummaryRead.Row>(
                source.Plan with { Pagination = pagination },
                null,
                null,
                source.ParameterCodec,
                source.RowCodec,
                source.ClientContract)
            {
                Exposure = source.Exposure,
                Authorization = source.Authorization,
                Disclosure = source.Disclosure,
                SourceAuthority = source.SourceAuthority,
                Audience = source.Audience,
                RequiredGrantId = source.RequiredGrantId,
                ConfidentialOutputFieldIds = source.ConfidentialOutputFieldIds,
                SecretOutputFieldIds = source.SecretOutputFieldIds,
                SystemSourceIds = source.SystemSourceIds,
                SerializerRegistration = source.SerializerRegistration,
                ParameterDeclarations = source.ParameterDeclarations,
                RowDeclarations = source.RowDeclarations,
            };
            var services = new ServiceCollection();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => options.ApplicationId = "read-pagination-checksum")
                .AddCollection(ReadProject.Collection)
                .AddCollection(ReadOwner.Collection)
                .AddCollection(ReadTask.Collection)
                .AddRead(read));
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<BaseLogicalSchema>();
        }

        BaseLogicalSchema pageOnly = Build(new()
        {
            Mode = BaseRegisteredReadPaginationMode.PageOnly,
            MaximumOffset = 0,
        });
        BaseLogicalSchema offsetZero = Build(new()
        {
            Mode = BaseRegisteredReadPaginationMode.PageAndOffset,
            MaximumOffset = 0,
        });
        BaseLogicalSchema offsetOne = Build(new()
        {
            Mode = BaseRegisteredReadPaginationMode.PageAndOffset,
            MaximumOffset = 1,
        });
        BaseLogicalSchema offsetMaximum = Build(new()
        {
            Mode = BaseRegisteredReadPaginationMode.PageAndOffset,
            MaximumOffset = 100_000,
        });

        new[]
        {
            pageOnly.CanonicalChecksum,
            offsetZero.CanonicalChecksum,
            offsetOne.CanonicalChecksum,
            offsetMaximum.CanonicalChecksum,
        }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GeneratorProducesTypedHandlesAndClosedCanonicalPlan()
    {
        ProjectNameRead.Parameters.Name.Id.Should().Be("project-name.name");
        ProjectNameRead.Row.Fields.Name.Id.Should().Be("project-name.row.name");
        ProjectNameRead.Handle.Id.Should().Be("project-name");
        ProjectNameRead.Definition.Exposure.Should().Be(BaseReadExposure.Public);
        ProjectNameRead.Definition.Authorization.Should().Be(BaseReadAuthorization.Authenticated);

        BaseRelationalReadPlan plan = ProjectNameRead.Definition.Plan;
        plan.Sources.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new BaseRelationalReadSource { Id = "projects", CollectionId = "read-projects" });
        plan.Predicate!.Left!.FieldId.Should().Be("read-project.name");
        plan.Predicate.Right!.ParameterId.Should().Be("project-name.name");
        plan.Projection.Should().ContainSingle().Which.FieldId.Should().Be("project-name.row.name");
        ProjectSummaryRead.Definition.ClientContract.Parameters.Single().Should().BeEquivalentTo(new
        {
            GeneratedName = "OwnerId",
            WireName = "ownerId",
        });
    }

    [Fact]
    public void UnifiedBuilderRegistersOnlyGeneratedTypedReadDefinitions()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<HPDBaseInstalledFeatures>().ReadIds
            .Should().Equal("project-name");
    }

    [Fact]
    public void CompletePortableBuilderLowersJoinsGroupingAndAggregates()
    {
        BaseRelationalReadPlan plan = ProjectSummaryRead.Definition.Plan;

        plan.Sources.Select(source => source.Id).Should().Equal("projects", "owners", "tasks");
        plan.Joins.Select(join => join.Kind).Should().Equal(BaseJoinKind.Inner, BaseJoinKind.Left);
        plan.Joins[0].Right.Kind.Should().Be(BaseRelationalOperandKind.RecordId);
        plan.GroupKeys.Should().HaveCount(2);
        plan.Aggregates.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = "project-summary.task-count",
            Kind = BaseAggregateKind.Count,
        });
        plan.Projection.Should().HaveCount(3);
    }

    [Fact]
    public async Task SessionExecutesGeneratedReadThroughTheClosedProviderBoundary()
    {
        var store = new RelationalReadStore();
        var services = new ServiceCollection().AddLogging();
                services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using var provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync())
            .IsSuccess().Should().BeTrue();

        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "subject_1",
            SubjectKind = AccessSubjectKind.User,
            CurrentTenantId = "tenant_1",
        });
        BaseResult<BasePage<ProjectNameRead.Row>> result = await session.Reads.ExecuteAsync(
            ProjectNameRead.Handle,
            new ProjectNameRead { Name = "alpha" },
            BaseReadPageRequest.Create(1, 20));

        result.RequireValue().Items.Should().ContainSingle().Which.Name.Should().Be("returned");
        store.Request!.ParameterValues.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new BaseRelationalParameterValue
            {
                ParameterId = "project-name.name",
                Value = new QueryValue { Kind = QueryValueKind.String, String = "alpha" },
            });
        store.Request.Plan.Window.Should().BeEquivalentTo(new BaseRegisteredReadWindow
        {
            Kind = BaseRegisteredReadWindowKind.Page, Page = 1, PerPage = 20,
        });
        store.Request.ExecutionTimeout.Should().Be(TimeSpan.FromMilliseconds(ProjectNameRead.Definition.Plan.Budgets.MaxExecutionMilliseconds));
        store.Request.MaxResultBytes.Should().Be(ProjectNameRead.Definition.Plan.Budgets.MaxResultBytes);
        store.Request.Operation.TenantId.Should().Be("tenant_1");
        store.Request.SourcePolicies.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            SourceId = "projects",
            CollectionId = "read-projects",
        });
    }

    [Fact]
    public async Task OffsetAuthorityRejectsOverMaximumBeforeProviderAndRejectsHostileEcho()
    {
        var store = new RelationalReadStore();
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
        });

        BaseFailure<BasePage<ProjectNameRead.Row>> excessive = (await session.Reads.ExecuteOffsetAsync(
            ProjectNameRead.Handle, new ProjectNameRead { Name = "alpha" }, BaseReadOffsetRequest.Create(100_001, 1)))
            .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;
        excessive.Error.Code.Should().Be("base.relational.read.invalid");
        store.Request.Should().BeNull();

        store.Response = new BaseRelationalReadExecutionResult
        {
            Result = new BaseRelationalReadResult
            {
                Rows = [], Page = new PageInfo { Offset = 2, Limit = 1 }, Count = 0, SchemaGeneration = 0,
            },
            DependencyEvidence = [new BaseReadDependencyEvidence { CollectionId = ReadProject.Collection.Id }],
        };
        BaseFailure<BasePage<ProjectNameRead.Row>> hostile = (await session.Reads.ExecuteOffsetAsync(
            ProjectNameRead.Handle, new ProjectNameRead { Name = "alpha" }, BaseReadOffsetRequest.Create(1, 1)))
            .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;
        hostile.Error.Code.Should().Be("base.relational.read.resultInvalid");

        BaseRelationalRow row = new()
        {
            Fields = [new BaseRelationalFieldValue
            {
                FieldId = "project-name.row.name",
                Value = new QueryValue { Kind = QueryValueKind.String, String = "hostile" },
            }],
        };
        foreach ((BaseRelationalRow[] rows, long count, int offset, int limit, bool hasMore) in new[]
        {
            (Array.Empty<BaseRelationalRow>(), 100L, 0, 10, true),
            (Array.Empty<BaseRelationalRow>(), 5L, 0, 10, true),
            (new[] { row }, 0L, 100, 1, false),
        })
        {
            store.Response = new BaseRelationalReadExecutionResult
            {
                Result = new BaseRelationalReadResult
                {
                    Rows = rows, Page = new PageInfo { Offset = offset, Limit = limit, HasMore = hasMore },
                    Count = count, SchemaGeneration = 0,
                },
                DependencyEvidence = [new BaseReadDependencyEvidence { CollectionId = ReadProject.Collection.Id }],
            };
            BaseFailure<BasePage<ProjectNameRead.Row>> incomplete = (await session.Reads.ExecuteOffsetAsync(
                ProjectNameRead.Handle, new ProjectNameRead { Name = "alpha" }, BaseReadOffsetRequest.Create(offset, limit)))
                .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;
            incomplete.Error.Code.Should().Be("base.relational.read.resultInvalid");
        }

        store.Response = new BaseRelationalReadExecutionResult
        {
            Result = new BaseRelationalReadResult
            {
                Rows = [], Page = new PageInfo { Page = 1, PerPage = 10, Offset = 0, HasMore = true },
                Count = 5, SchemaGeneration = 0,
            },
            DependencyEvidence = [new BaseReadDependencyEvidence { CollectionId = ReadProject.Collection.Id }],
        };
        BaseFailure<BasePage<ProjectNameRead.Row>> mixedPage = (await session.Reads.ExecuteAsync(
            ProjectNameRead.Handle, new ProjectNameRead { Name = "alpha" }, BaseReadPageRequest.Create(1, 10)))
            .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;
        mixedPage.Error.Code.Should().Be("base.relational.read.resultInvalid");
    }

    [Fact]
    public async Task HostileProviderRowsAndDependencyEvidenceFailClosedBeforeReturningValues()
    {
        var store = new RelationalReadStore();
        var services = new ServiceCollection().AddLogging();
                services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "hostile-provider-subject",
        });

        store.Response = Result(Row(Value(QueryValueKind.Null)), Evidence());
        BaseResult<BasePage<ProjectNameRead.Row>> wrongKind = await ExecuteAsync();
        wrongKind.Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Which.Error.Code.Should().Be("base.relational.read.resultInvalid");

        BaseRelationalRow validRow = Row(Value(QueryValueKind.String, "returned"));
        store.Response = ResultRows(Enumerable.Repeat(validRow, 1_001).ToArray(), Evidence());
        BaseResult<BasePage<ProjectNameRead.Row>> oversized = await ExecuteAsync();
        oversized.Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Which.Error.Code.Should().Be("base.relational.read.limitExceeded");

        store.Response = Result(validRow, []);
        BaseResult<BasePage<ProjectNameRead.Row>> incompleteEvidence = await ExecuteAsync();
        incompleteEvidence.Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Which.Error.Code.Should().Be("base.relational.dependencies.invalid");

        store.Response = Result(validRow,
            Enumerable.Range(0, 1_002).Select(index => new BaseReadDependencyEvidence
            {
                CollectionId = ReadProject.Collection.Id, RecordId = "record-" + index,
            }).ToArray());
        BaseResult<BasePage<ProjectNameRead.Row>> oversizedEvidence = await ExecuteAsync();
        oversizedEvidence.Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Which.Error.Code.Should().Be("base.relational.dependencies.invalid");

        Task<BaseResult<BasePage<ProjectNameRead.Row>>> ExecuteAsync() => session.Reads.ExecuteAsync(
            ProjectNameRead.Handle, new ProjectNameRead { Name = "alpha" }, BaseReadPageRequest.Create(1, 20)).AsTask();
        static BaseReadDependencyEvidence[] Evidence() => [new() { CollectionId = ReadProject.Collection.Id }];
        static QueryValue Value(QueryValueKind kind, string? value = null) => new() { Kind = kind, String = value };
        static BaseRelationalRow Row(QueryValue value) => new()
        {
            Fields = [new BaseRelationalFieldValue { FieldId = "project-name.row.name", Value = value }],
        };
        static BaseRelationalReadExecutionResult Result(BaseRelationalRow row, BaseReadDependencyEvidence[] evidence) => ResultRows([row], evidence);
        static BaseRelationalReadExecutionResult ResultRows(BaseRelationalRow[] rows, BaseReadDependencyEvidence[] evidence) => new()
        {
            Result = new BaseRelationalReadResult
            {
                Rows = rows,
                Page = new PageInfo { Page = 1, PerPage = 20 },
                Count = rows.Length,
                SchemaGeneration = 0,
            },
            DependencyEvidence = evidence,
        };
    }

    [Fact]
    public void HostRegisteredReadCeilingCannotUndercutImmutableReadAuthority()
    {
        var store = new RelationalReadStore();
        var services = new ServiceCollection().AddLogging();
        Action configure = () => services.AddHPDBase(builder => builder
            .ConfigureRelational(options => options.MaxRegisteredReadResultBytes = 99_999)
            .AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage("Read 'project-name' has an invalid or over-limit topology.");
        store.Request.Should().BeNull();
    }

    [Fact]
    public async Task EverySourcePolicyIsEvaluatedAndHiddenInfluenceStopsBeforeProviderExecution()
    {
        var store = new RelationalReadStore();
        var evaluator = new SourceMaskPolicyEvaluator();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator>(evaluator);
        services.AddHPDBase(builder => builder
            .AddTestPolicyAuthority(evaluator)
            .AddCollection(ReadProject.Collection)
            .AddCollection(ReadOwner.Collection)
            .AddCollection(ReadTask.Collection)
            .AddRead(ProjectSummaryRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "subject_1",
        });

        BaseResult<ProjectSummaryRead.Row[]> result = await session.Reads.ToArrayAsync(
            ProjectSummaryRead.Handle,
            new ProjectSummaryRead { OwnerId = new BaseRecordId<ReadOwner>(RecordId.Create("owner_1")) });

        result.Should().BeOfType<BaseFailure<ProjectSummaryRead.Row[]>>()
            .Which.Error.Code.Should().Be("base.relational.read.policyUnsupported");
        evaluator.Collections.Should().Equal("read-projects", "read-owners", "read-tasks");
        store.Request.Should().BeNull();
    }

    [Fact]
    public async Task DeniedSourcePolicyCannotLeakItsProviderMessageOrExecuteTheRead()
    {
        var store = new RelationalReadStore();
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .AddTestPolicyAuthority<DenyReadSourcePolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });

        BaseFailure<BasePage<ProjectNameRead.Row>> failure = (await session.Reads.ExecuteAsync(
            ProjectNameRead.Handle, new ProjectNameRead { Name = "secret-value" }, BaseReadPageRequest.Create(1, 10)))
            .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;

        failure.Error.Code.Should().Be("base.relational.read.policyUnsupported");
        failure.Error.Message.ToLowerInvariant().Should().NotContain("secret");
        store.Request.Should().BeNull();
    }

    [Fact]
    public async Task WeakSnapshotsAndMixedStoresFailBeforeAnyProviderExecution()
    {
        var weak = new RelationalReadStore(snapshotConsistency: false);
        var weakServices = new ServiceCollection().AddLogging();
        weakServices.AddHPDBase(builder => builder
            .AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(weak, relational: true)));
        await using (ServiceProvider provider = weakServices.BuildServiceProvider())
        {
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });
            BaseFailure<BasePage<ProjectNameRead.Row>> failure = (await session.Reads.ExecuteAsync(
                ProjectNameRead.Handle, new ProjectNameRead { Name = "value" }, BaseReadPageRequest.Create(1, 10)))
                .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;
            failure.Error.Code.Should().Be("base.relational.read.snapshotUnavailable");
            weak.Request.Should().BeNull();
        }

        var unsupported = new RelationalReadStore(comparisonOperators: []);
        var unsupportedServices = new ServiceCollection().AddLogging();
        unsupportedServices.AddHPDBase(builder => builder
            .AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(unsupported, relational: true)));
        await using (ServiceProvider provider = unsupportedServices.BuildServiceProvider())
        {
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });
            BaseFailure<BasePage<ProjectNameRead.Row>> failure = (await session.Reads.ExecuteAsync(
                ProjectNameRead.Handle, new ProjectNameRead { Name = "value" }, BaseReadPageRequest.Create(1, 10)))
                .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;
            failure.Error.Code.Should().Be("base.relational.read.unsupported");
            unsupported.Request.Should().BeNull();
        }

        var first = new RelationalReadStore();
        var second = new RelationalReadStore();
        var mixedServices = new ServiceCollection().AddLogging();
        mixedServices.AddHPDBase(builder => builder
            .AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddCollection(ReadOwner.Collection)
            .AddCollection(ReadTask.Collection)
            .AddRead(ProjectSummaryRead.Definition)
            .Use(new SplitRelationalReadInstaller(first, second)));
        await using (ServiceProvider provider = mixedServices.BuildServiceProvider())
        {
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeFalse();
            first.Request.Should().BeNull();
            second.Request.Should().BeNull();
        }
    }

    [Fact]
    public async Task NamedReadAuthorizationFailsBeforeStoreSelectionOrProviderExecution()
    {
        var store = new RelationalReadStore();
        var services = new ServiceCollection().AddLogging();
        var policy = new CountingReadPolicyEvaluator();
        services.AddSingleton<IPolicyEvaluator>(policy);
        services.AddHPDBase(builder => builder
            .AddTestPolicyAuthority(policy)
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession anonymous = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Anonymous,
        });

        BaseFailure<BasePage<ProjectNameRead.Row>> failure = (await anonymous.Reads.ExecuteAsync(
            ProjectNameRead.Handle, new ProjectNameRead { Name = "secret" }, BaseReadPageRequest.Create(1, 10)))
            .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;

        failure.Status.Should().Be(OperationStatus.PolicyDenied);
        failure.Error.Code.Should().Be("base.relational.read.denied");
        store.Request.Should().BeNull();
        policy.QueryEvaluations.Should().Be(0);
    }

    [Fact]
    public async Task NonRelationalProviderKeepsCrudAndRejectsRegisteredReadsHonestly()
    {
        var store = new FakeRecordStore("non-relational");
        var services = new ServiceCollection().AddLogging();
                services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddCollection(ReadOwner.Collection)
            .AddRead(ProjectNameRead.Definition)
            .UseStore(TestStoreProvider.Create(store)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });

        BaseRecordId<ReadOwner> ownerId = new(RecordId.Create("owner"));
        BaseResult<BaseRecord<ReadOwner>> ownerCreate = await session.Collection(ReadOwner.Collection).CreateAsync(ownerId.Value, new ReadOwner { Name = "owner" });
        ownerCreate.Should().BeOfType<BaseSuccess<BaseRecord<ReadOwner>>>(ownerCreate is BaseFailure<BaseRecord<ReadOwner>> ownerFailure ? ownerFailure.Error.Code : "unexpected result");
        BaseRecordId<ReadProject> id = new(RecordId.Create("project"));
        (await session.Collection(ReadProject.Collection).CreateAsync(id.Value, new ReadProject
        {
            Name = "created",
            OwnerId = ownerId,
        })).Should().BeOfType<BaseSuccess<BaseRecord<ReadProject>>>();
        (await session.Collection(ReadProject.Collection).GetAsync(id.Value)).RequireValue().Value.Name.Should().Be("created");

        BaseFailure<BasePage<ProjectNameRead.Row>> failure = (await session.Reads.ExecuteAsync(
            ProjectNameRead.Handle, new ProjectNameRead { Name = "created" }, BaseReadPageRequest.Create(1, 10)))
            .Should().BeOfType<BaseFailure<BasePage<ProjectNameRead.Row>>>().Subject;
        failure.Error.Code.Should().Be("base.relational.read.unsupported");
    }

    [Fact]
    public async Task LiveReadOverflowTerminatesWithTheExactBoundedFailure()
    {
        var store = new RelationalReadStore();
        BaseRelationalRow row = new()
        {
            Fields = [new BaseRelationalFieldValue
            {
                FieldId = "project-name.row.name",
                Value = new QueryValue { Kind = QueryValueKind.String, String = "value" },
            }],
        };
        store.Response = new BaseRelationalReadExecutionResult
        {
            Result = new BaseRelationalReadResult
            {
                Rows = Enumerable.Repeat(row, 1_001).ToArray(),
                Page = new PageInfo { Limit = 1_000 }, Count = 1_001, SchemaGeneration = 0,
            },
            DependencyEvidence = [new BaseReadDependencyEvidence { CollectionId = ReadProject.Collection.Id }],
        };
        var services = new ServiceCollection().AddLogging();
                services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddRead(ProjectNameRead.Definition)
            .AddDependencies(options => options.ProtectionKey = Enumerable.Repeat((byte)0x5b, 32).ToArray())
            .AddLiveQueries()
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System });
        await using IAsyncEnumerator<BaseLiveQueryTransition<ProjectNameRead.Row[]>> transitions = session.Reads
            .LiveAsync(ProjectNameRead.Handle, new ProjectNameRead { Name = "value" }).GetAsyncEnumerator();

        Func<Task> move = async () => await transitions.MoveNextAsync();
        BaseLiveQueryException failure = (await move.Should().ThrowAsync<BaseLiveQueryException>()).Which;
        failure.Code.Should().Be("base.relational.read.limitExceeded");
        failure.Message.Should().NotContain("value");
    }

    [Fact]
    public async Task BuiltInInMemoryExecutesJoinGroupAggregateFromOneImmutableSnapshot()
    {
        var services = new ServiceCollection().AddLogging();
                services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(ReadProject.Collection)
            .AddCollection(ReadOwner.Collection)
            .AddCollection(ReadTask.Collection)
            .AddRead(ProjectSummaryRead.Definition));
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "subject_1",
        });
        var ownerId = new BaseRecordId<ReadOwner>(RecordId.Create("owner_1"));
        var projectId = new BaseRecordId<ReadProject>(RecordId.Create("project_1"));
        (await session.Collection(ReadOwner.Collection).CreateAsync(ownerId.Value, new ReadOwner { Name = "Owner" })).Should().BeOfType<BaseSuccess<BaseRecord<ReadOwner>>>();
        (await session.Collection(ReadProject.Collection).CreateAsync(projectId.Value, new ReadProject { Name = "Project", OwnerId = ownerId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadProject>>>();
        (await session.Collection(ReadTask.Collection).CreateAsync(RecordId.Create("task_1"), new ReadTask { ProjectId = projectId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadTask>>>();
        (await session.Collection(ReadTask.Collection).CreateAsync(RecordId.Create("task_2"), new ReadTask { ProjectId = projectId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadTask>>>();

        ProjectSummaryRead.Row[] rows = (await session.Reads.ToArrayAsync(
            ProjectSummaryRead.Handle, new ProjectSummaryRead { OwnerId = ownerId })).RequireValue();

        rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            ProjectId = projectId,
            OwnerName = "Owner",
            TaskCount = 2L,
        });

        IRelationalReadStore inMemory = (IRelationalReadStore)provider
            .GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(ReadProject.Collection.Id)!;
        long generation = provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness!.SchemaGeneration!.Value;
        OperationResult<BaseRelationalReadExecutionResult> overLimit = await inMemory.ExecuteReadAsync(
            new BaseRelationalReadExecutionRequest
            {
                Plan = ProjectSummaryRead.Definition.Plan with { SchemaGeneration = generation },
                ParameterValues =
                [
                    new BaseRelationalParameterValue
                    {
                        ParameterId = "project-summary.owner-id",
                        Value = new QueryValue { Kind = QueryValueKind.Id, Id = ownerId.Value.Value },
                    },
                ],
                SourcePolicies = ProjectSummaryRead.Definition.Plan.Sources.Select(source =>
                    new BaseRelationalReadSourcePolicy { SourceId = source.Id, CollectionId = source.CollectionId }).ToArray(),
                Operation = new OperationContext { Operation = BaseOperationKind.List, CollectionId = ReadProject.Collection.Id },
                AcquisitionTimeout = TimeSpan.FromSeconds(1),
                ExecutionTimeout = TimeSpan.FromSeconds(1),
                MaxResultRows = 100,
                MaxResultBytes = 1,
            });
        overLimit.Error!.Code.Should().Be("base.relational.read.limitExceeded");
        overLimit.Value.Should().BeNull("providers must not expose a partial buffered page");
    }

    [Fact]
    public async Task InMemory_real_pipeline_bounds_final_projection_across_explicit_fallback_and_distinct_ordering()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(L60BoundedProjectionRecord.Collection)
            .AddRead(L60BoundedProjectionRead.Definition));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "l60-pipeline",
        });
        BaseCollectionSession<L60BoundedProjectionRecord> collection =
            session.Collection(L60BoundedProjectionRecord.Collection);
        (await collection.CreateAsync(RecordId.Create("one"), new L60BoundedProjectionRecord
        {
            Order = 1,
            Payload = new string('a', 128),
            Poison = 1,
        })).RequireValue();
        (await collection.CreateAsync(RecordId.Create("two"), new L60BoundedProjectionRecord
        {
            Order = 2,
            Payload = new string('z', 128),
            Poison = 2,
        })).RequireValue();
        (await collection.CreateAsync(RecordId.Create("three"), new L60BoundedProjectionRecord
        {
            Order = 3,
            Payload = new string('z', 128) + "z",
            Poison = 3,
        })).RequireValue();

        var store = (InMemoryRecordStore)provider.GetRequiredService<IRecordStoreRegistry>()
            .GetStoreForCollection(L60BoundedProjectionRecord.Collection.Id)!;
        InMemoryStoreState state = store.CaptureVectorRoot();
        string poisonWireName = L60BoundedProjectionRecord.Collection.Definition.Fields!
            .Single(field => field.Id == L60BoundedProjectionRecord.Fields.Poison.Id).WireName;
        state.Collections[L60BoundedProjectionRecord.Collection.Id].RecordsById["two"]
            .Payload.Fields![poisonWireName] = JsonSerializer.SerializeToElement("poison-must-not-project");
        state.Collections[L60BoundedProjectionRecord.Collection.Id].RecordsById["three"]
            .Payload.Fields![poisonWireName] = JsonSerializer.SerializeToElement("poison-must-not-project");

        long generation = provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness!.SchemaGeneration!.Value;
        async ValueTask<OperationResult<BaseRelationalReadExecutionResult>> Execute(BaseRelationalReadPlan plan) =>
            await store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
            {
                Plan = plan with { SchemaGeneration = generation },
                ParameterValues = [],
                SourcePolicies = plan.Sources.Select(source => new BaseRelationalReadSourcePolicy
                {
                    SourceId = source.Id,
                    CollectionId = source.CollectionId,
                }).ToArray(),
                Operation = new OperationContext
                {
                    Operation = BaseOperationKind.List,
                    CollectionId = L60BoundedProjectionRecord.Collection.Id,
                },
                AcquisitionTimeout = TimeSpan.FromSeconds(1),
                ExecutionTimeout = TimeSpan.FromSeconds(1),
                MaxResultRows = 3,
                MaxResultBytes = 1,
            });

        OperationResult<BaseRelationalReadExecutionResult> explicitSort =
            await Execute(L60BoundedProjectionRead.Definition.Plan);
        explicitSort.Error!.Code.Should().Be("base.relational.read.limitExceeded");
        explicitSort.Value.Should().BeNull();

        OperationResult<BaseRelationalReadExecutionResult> fallbackSort = await Execute(
            L60BoundedProjectionRead.Definition.Plan with { Sort = [] });
        fallbackSort.Error!.Code.Should().Be("base.relational.read.limitExceeded");
        fallbackSort.Value.Should().BeNull();

        state.Collections[L60BoundedProjectionRecord.Collection.Id].RecordsById["two"]
            .Payload.Fields![poisonWireName] = JsonSerializer.SerializeToElement(2L);
        state.Collections[L60BoundedProjectionRecord.Collection.Id].RecordsById["three"]
            .Payload.Fields![poisonWireName] = JsonSerializer.SerializeToElement(3L);
        OperationResult<BaseRelationalReadExecutionResult> distinct = await Execute(
            L60BoundedProjectionRead.Definition.Plan with { Distinct = true });
        distinct.Error!.Code.Should().Be("base.relational.read.limitExceeded");
        distinct.Value.Should().BeNull("Distinct may retain authority and digests, but never a partial projected page");
    }

    [Fact]
    public async Task RegisteredReadLiveQueryRerunsFromCompleteDependenciesAndEmitsWholeReplacements()
    {
        var services = new ServiceCollection().AddLogging();
        var policy = new CountingReadPolicyEvaluator();
        services.AddSingleton<IPolicyEvaluator>(policy);
        services.AddHPDBase(builder => builder
            .AddTestPolicyAuthority(policy)
            .AddCollection(ReadProject.Collection)
            .AddCollection(ReadOwner.Collection)
            .AddCollection(ReadTask.Collection)
            .AddCollection(ReadUnrelated.Collection)
            .AddRead(ProjectSummaryRead.Definition)
            .AddDependencies(options => options.ProtectionKey = Enumerable.Repeat((byte)0x5a, 32).ToArray())
            .AddLiveQueries());
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "live-subject" });
        var ownerId = new BaseRecordId<ReadOwner>(RecordId.Create("owner_1"));
        var projectId = new BaseRecordId<ReadProject>(RecordId.Create("project_1"));
        (await session.Collection(ReadOwner.Collection).CreateAsync(ownerId.Value, new ReadOwner { Name = "Owner" })).Should().BeOfType<BaseSuccess<BaseRecord<ReadOwner>>>();
        (await session.Collection(ReadProject.Collection).CreateAsync(projectId.Value, new ReadProject { Name = "Project", OwnerId = ownerId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadProject>>>();
        (await session.Collection(ReadTask.Collection).CreateAsync(RecordId.Create("task_1"), new ReadTask { ProjectId = projectId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadTask>>>();

        await using IAsyncEnumerator<BaseLiveQueryTransition<ProjectSummaryRead.Row[]>> transitions = session.Reads
            .LiveAsync(ProjectSummaryRead.Handle, new ProjectSummaryRead { OwnerId = ownerId })
            .GetAsyncEnumerator();
        (await transitions.MoveNextAsync()).Should().BeTrue();
        transitions.Current.Kind.Should().Be(BaseLiveQueryTransitionKind.Snapshot);
        transitions.Current.Value.Should().ContainSingle().Which.TaskCount.Should().Be(1);

        int queryEvaluations = policy.QueryEvaluations;
        (await session.Collection(ReadUnrelated.Collection).CreateAsync(RecordId.Create("unrelated"), new ReadUnrelated { Value = "ignored" })).Should().BeOfType<BaseSuccess<BaseRecord<ReadUnrelated>>>();
        await Task.Delay(100);
        policy.QueryEvaluations.Should().Be(queryEvaluations, "precise dependency evidence must not rerun for an unrelated collection");

        (await session.Collection(ReadTask.Collection).CreateAsync(RecordId.Create("task_2"), new ReadTask { ProjectId = projectId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadTask>>>();

        (await transitions.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        transitions.Current.Kind.Should().Be(BaseLiveQueryTransitionKind.Snapshot);
        transitions.Current.Value.Should().ContainSingle().Which.TaskCount.Should().Be(2);
        transitions.Current.Version.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task SqliteExecutesRegisteredJoinGroupAggregateInsideNativeSnapshot()
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-relational-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
                        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
                .ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x62, 32).ToArray())
                .AddCollection(ReadProject.Collection)
                .AddCollection(ReadOwner.Collection)
                .AddCollection(ReadTask.Collection)
                .AddRead(ProjectSummaryRead.Definition)
                .UseStore(SqliteStore.Configure(options => options.DataSource = database)));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectId = "subject_1",
            });
            var ownerId = new BaseRecordId<ReadOwner>(RecordId.Create("owner_1"));
            var projectId = new BaseRecordId<ReadProject>(RecordId.Create("project_1"));
            (await session.Collection(ReadOwner.Collection).CreateAsync(ownerId.Value, new ReadOwner { Name = "Owner" })).Should().BeOfType<BaseSuccess<BaseRecord<ReadOwner>>>();
            (await session.Collection(ReadProject.Collection).CreateAsync(projectId.Value, new ReadProject { Name = "Project", OwnerId = ownerId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadProject>>>();
            (await session.Collection(ReadTask.Collection).CreateAsync(RecordId.Create("task_1"), new ReadTask { ProjectId = projectId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadTask>>>();
            (await session.Collection(ReadTask.Collection).CreateAsync(RecordId.Create("task_2"), new ReadTask { ProjectId = projectId })).Should().BeOfType<BaseSuccess<BaseRecord<ReadTask>>>();

            ProjectSummaryRead.Row[] rows = (await session.Reads.ToArrayAsync(
                ProjectSummaryRead.Handle, new ProjectSummaryRead { OwnerId = ownerId })).RequireValue();

            rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                ProjectId = projectId,
                OwnerName = "Owner",
                TaskCount = 2L,
            });

            SqliteRecordStore store = provider.GetRequiredService<SqliteRecordStore>();
            long generation = provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness!.SchemaGeneration!.Value;
            OperationResult<BaseRelationalReadExecutionResult> overLimit = await store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
            {
                Plan = ProjectSummaryRead.Definition.Plan with { SchemaGeneration = generation },
                ParameterValues = [new BaseRelationalParameterValue { ParameterId = "project-summary.owner-id", Value = new QueryValue { Kind = QueryValueKind.Id, Id = ownerId.Value.Value } }],
                SourcePolicies = ProjectSummaryRead.Definition.Plan.Sources.Select(source => new BaseRelationalReadSourcePolicy { SourceId = source.Id, CollectionId = source.CollectionId }).ToArray(),
                Operation = new OperationContext { Operation = BaseOperationKind.List, CollectionId = ReadProject.Collection.Id },
                AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1),
                MaxResultRows = 100, MaxResultBytes = 1,
            });
            overLimit.Error!.Code.Should().Be("base.relational.read.limitExceeded");
            overLimit.Value.Should().BeNull("providers must not expose a partial buffered page");
            OperationResult<BaseRelationalReadExecutionResult> stale = await store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
            {
                Plan = ProjectSummaryRead.Definition.Plan with { SchemaGeneration = 99 },
                ParameterValues = [new BaseRelationalParameterValue { ParameterId = "project-summary.owner-id", Value = new QueryValue { Kind = QueryValueKind.Id, Id = ownerId.Value.Value } }],
                SourcePolicies = ProjectSummaryRead.Definition.Plan.Sources.Select(source => new BaseRelationalReadSourcePolicy { SourceId = source.Id, CollectionId = source.CollectionId }).ToArray(),
                Operation = new OperationContext { Operation = BaseOperationKind.List, CollectionId = ReadProject.Collection.Id },
                AcquisitionTimeout = TimeSpan.FromSeconds(1),
                ExecutionTimeout = TimeSpan.FromSeconds(1),
                MaxResultRows = 100,
                MaxResultBytes = 100_000,
            });
            stale.Error!.Code.Should().Be("base.relational.read.schemaNotReady");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { database, database + "-wal", database + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed class SplitRelationalReadInstaller(RelationalReadStore first, RelationalReadStore second) : IHPDBaseBuilderExtension
    {
        public string Id => "split-relational-test";
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) { }
        public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            IRecordStoreRegistry stores = services.GetRequiredService<IRecordStoreRegistry>();
            stores.Add(new RecordStoreRegistration { StoreId = "split-a", Store = first, CollectionIds = [ReadProject.Collection.Id, ReadOwner.Collection.Id] });
            stores.Add(new RecordStoreRegistration { StoreId = "split-b", Store = second, CollectionIds = [ReadTask.Collection.Id] });
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RelationalReadStore : FakeRecordStore, IRelationalReadStore
    {
        public RelationalReadStore(bool snapshotConsistency = true, FilterOperator[]? comparisonOperators = null) : base("relational-test")
        {
            RelationalReads = Capability with
            {
                SnapshotConsistency = snapshotConsistency,
                ComparisonOperators = comparisonOperators ?? Capability.ComparisonOperators,
            };
        }

        private static RelationalReadCapability Capability { get; } = new()
        {
            Supported = true,
            JoinKinds = [BaseJoinKind.Inner, BaseJoinKind.Left],
            AggregateKinds = [BaseAggregateKind.Count],
            ComparisonOperators = [FilterOperator.Equal],
            ValueKinds = [QueryValueKind.String, QueryValueKind.Id],
            MaxSources = 4,
            MaxJoins = 3,
            MaxPredicateNodes = 32,
            MaxGroupKeys = 8,
            MaxAggregates = 8,
            MaxProjectionFields = 16,
            MaxSortFields = 8,
            MaxResultRows = 1_000,
            MaxResultBytes = 4 * 1024 * 1024,
            SnapshotConsistency = true,
            CompleteDependencyEvidence = true,
        };

        public RelationalReadCapability RelationalReads { get; }

        public BaseRelationalReadExecutionRequest? Request { get; private set; }
        public BaseRelationalReadExecutionResult? Response { get; set; }

        public ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(
            BaseRelationalReadExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            PageInfo page = request.Plan.Window switch
            {
                { Kind: BaseRegisteredReadWindowKind.Page } window => new PageInfo
                {
                    Page = window.Page, PerPage = window.PerPage, HasMore = false,
                },
                { Kind: BaseRegisteredReadWindowKind.Offset } window => new PageInfo
                {
                    Offset = window.Offset, Limit = window.Limit, HasMore = false,
                },
                _ => new PageInfo { Limit = 20, HasMore = false },
            };
            return ValueTask.FromResult(OperationResults.Ok(Response ?? new BaseRelationalReadExecutionResult
            {
                Result = new BaseRelationalReadResult
                {
                    Rows = [new BaseRelationalRow
                    {
                        Fields = [new BaseRelationalFieldValue
                        {
                            FieldId = "project-name.row.name",
                            Value = new QueryValue { Kind = QueryValueKind.String, String = "returned" },
                        }],
                    }],
                    Page = page,
                    Count = 1,
                    SchemaGeneration = 0,
                },
                DependencyEvidence = [new BaseReadDependencyEvidence { CollectionId = ReadProject.Collection.Id }],
            }));
        }
    }

    private sealed class SourceMaskPolicyEvaluator : IPolicyEvaluator
    {
        public List<string> Collections { get; } = [];
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
        {
            Collections.Add(request.Collection!.Id);
            PolicyDecision decision = PolicyDecision.Allow();
            if (request.Collection.Id == ReadOwner.Collection.Id)
                decision = decision.WithReadMask(new FieldMask { Mode = FieldMaskMode.IncludeOnly, Include = [] });
            return ValueTask.FromResult(decision);
        }
    }

    private sealed class CountingReadPolicyEvaluator : IPolicyEvaluator
    {
        public int QueryEvaluations { get; private set; }
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Resource.Kind == PolicyResourceKind.Query) QueryEvaluations++;
            return ValueTask.FromResult(PolicyDecision.Allow());
        }
    }

    private sealed class DenyReadSourcePolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(request.Resource.Kind == PolicyResourceKind.Query
                ? PolicyDecision.Deny("secret.source.code", "secret source policy message")
                : PolicyDecision.Allow());
    }
}

[BaseCollection("read-projects", typeof(GeneratedReadJsonContext))]
internal sealed partial record ReadProject
{
    [BaseField("read-project.name")]
    public required string Name { get; init; }

    [BaseField("read-project.owner-id")]
    public required BaseRecordId<ReadOwner> OwnerId { get; init; }
}

[BaseCollection("read-owners", typeof(GeneratedReadJsonContext))]
internal sealed partial record ReadOwner
{
    [BaseField("read-owner.name")]
    public required string Name { get; init; }
}

[BaseCollection("read-tasks", typeof(GeneratedReadJsonContext))]
internal sealed partial record ReadTask
{
    [BaseField("read-task.project-id")]
    public required BaseRecordId<ReadProject> ProjectId { get; init; }
}

[BaseCollection("read-unrelated", typeof(GeneratedReadJsonContext))]
internal sealed partial record ReadUnrelated
{
    [BaseField("read-unrelated.value")]
    public required string Value { get; init; }
}

[BaseCollection("l60-bounded-projection", typeof(GeneratedReadJsonContext))]
internal sealed partial record L60BoundedProjectionRecord
{
    [BaseField("l60-bounded.order", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required long Order { get; init; }

    [BaseField("l60-bounded.payload", MaximumUtf8Bytes = 512)]
    public required string Payload { get; init; }

    [BaseField("l60-bounded.poison")]
    public required long Poison { get; init; }
}

[BaseRead("l60-bounded-projection", typeof(GeneratedReadJsonContext), RequiredGrantId = "l60-bounded.execute")]
internal sealed partial record L60BoundedProjectionRead
{
    public sealed partial record Row
    {
        [BaseReadField("l60-bounded.row.payload")]
        public required string Payload { get; init; }

        [BaseReadField("l60-bounded.row.poison")]
        public required long Poison { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<L60BoundedProjectionRead, Row> read)
    {
        read.From(L60BoundedProjectionRecord.Collection, "records", out var record)
            .Project(Row.Fields.Payload, record.Field(L60BoundedProjectionRecord.Fields.Payload))
            .Project(Row.Fields.Poison, record.Field(L60BoundedProjectionRecord.Fields.Poison))
            .OrderBy(record.Field(L60BoundedProjectionRecord.Fields.Order))
            .Limits(maximumResultRows: 3, maximumResultBytes: 1_024,
                maximumOperations: 1_000, maximumExecutionMilliseconds: 1_000);
    }
}

[BaseRead("project-name", typeof(GeneratedReadJsonContext), Exposure = BaseReadExposure.Public, RequiredGrantId = "project-name.execute")]
internal sealed partial record ProjectNameRead
{
    [BaseReadParameter("project-name.name")]
    public required string Name { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("project-name.row.name")]
        public required string Name { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ProjectNameRead, Row> read)
    {
        read.From(ReadProject.Collection, "projects", out var project)
            .Where(project.Field(ReadProject.Fields.Name)
                .Equal(read.Parameter(Parameters.Name)))
            .Project(Row.Fields.Name, project.Field(ReadProject.Fields.Name))
            .AllowOffsetPagination(100_000);
    }
}

[BaseRead("project-summary", typeof(GeneratedReadJsonContext), RequiredGrantId = "project-summary.execute")]
internal sealed partial record ProjectSummaryRead
{
    [BaseReadParameter("project-summary.owner-id")]
    public required BaseRecordId<ReadOwner> OwnerId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("project-summary.project-id")]
        public required BaseRecordId<ReadProject> ProjectId { get; init; }
        [BaseReadField("project-summary.owner-name")]
        public required string OwnerName { get; init; }
        [BaseReadField("project-summary.task-count")]
        public required long TaskCount { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ProjectSummaryRead, Row> read)
    {
        read.From(ReadProject.Collection, "projects", out var project)
            .Join(ReadOwner.Collection, "owners", project.Field(ReadProject.Fields.OwnerId),
                BaseFields.RecordId, BaseJoinKind.Inner, out var owner)
            .LeftJoin(ReadTask.Collection, "tasks", project.RecordId,
                ReadTask.Fields.ProjectId, out var task)
            .Where(project.Field(ReadProject.Fields.OwnerId).Equal(read.Parameter(Parameters.OwnerId)))
            .GroupBy(project.RecordId, owner.Field(ReadOwner.Fields.Name))
            .Project(Row.Fields.ProjectId, project.RecordId)
            .Project(Row.Fields.OwnerName, owner.Field(ReadOwner.Fields.Name))
            .Aggregate(Row.Fields.TaskCount, BaseAggregate.Count(task.RecordId));
    }
}

[JsonSerializable(typeof(ReadProject))]
[JsonSerializable(typeof(ReadOwner))]
[JsonSerializable(typeof(ReadTask))]
[JsonSerializable(typeof(ReadUnrelated))]
[JsonSerializable(typeof(L60BoundedProjectionRecord))]
[JsonSerializable(typeof(L60BoundedProjectionRead))]
[JsonSerializable(typeof(L60BoundedProjectionRead.Row), TypeInfoPropertyName = "L60BoundedProjectionReadRow")]
[JsonSerializable(typeof(ProjectNameRead))]
[JsonSerializable(typeof(ProjectNameRead.Row), TypeInfoPropertyName = "ProjectNameReadRow")]
[JsonSerializable(typeof(ProjectSummaryRead))]
[JsonSerializable(typeof(ProjectSummaryRead.Row), TypeInfoPropertyName = "ProjectSummaryReadRow")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class GeneratedReadJsonContext : JsonSerializerContext;
