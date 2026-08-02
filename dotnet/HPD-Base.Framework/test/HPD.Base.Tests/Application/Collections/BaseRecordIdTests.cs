using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Tests.Application.Collections;

public sealed class BaseRecordIdTests
{
    [Fact]
    public void GeneratedTypedRecordIdSerializesAsTheUnderlyingString()
    {
        var value = new TypedIdDocument
        {
            OwnerId = BaseRecordId<TypedIdOwner>.Create("owner_1"),
        };

        string json = JsonSerializer.Serialize(value, TypedIdJsonContext.Default.TypedIdDocument);
        var roundTrip = JsonSerializer.Deserialize(json, TypedIdJsonContext.Default.TypedIdDocument);

        json.Should().Be("{\"ownerId\":\"owner_1\"}");
        roundTrip!.OwnerId.Should().Be(value.OwnerId);
        TypedIdDocument.Collection.Definition.Fields![0].Relation.Should().BeEquivalentTo(
            new RelationDefinition
            {
                Id = "typed-id-document.owner",
                SourceCollectionId = "typed-id-documents",
                SourceFieldId = "typed-id-document.owner",
                TargetCollectionId = "typed-id-owners",
                TargetFieldId = "base.recordId",
                LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne,
                InverseNavigationId = "typed-id-owner.documents",
                Required = true,
                Include = new RelationIncludeDefinition { Allowed = true },
            });
    }

    [Fact]
    public void ManualRelationLowersToTheSameCanonicalShape()
    {
        var manual = HPD.Base.BaseCollection.Define(
            "manual-typed-id-documents",
            TypedIdJsonContext.Default.TypedIdDocument,
            schema => schema.Relation(
                    "typed-id-document.owner",
                    "typed-id-document.owner",
                    "ownerId",
                    TypedIdOwner.Collection)
                .ExactlyOne()
                .Inverse("typed-id-owner.documents")
                .Include(maximumDepth: 2));

        RelationDefinition relation = manual.Definition.Fields![0].Relation!;
        relation.Id.Should().Be("typed-id-document.owner");
        relation.SourceCollectionId.Should().Be("manual-typed-id-documents");
        relation.SourceFieldId.Should().Be("typed-id-document.owner");
        relation.TargetCollectionId.Should().Be("typed-id-owners");
        relation.LocalMultiplicity.Should().Be(BaseRelationMultiplicity.ExactlyOne);
        relation.InverseNavigationId.Should().Be("typed-id-owner.documents");
        relation.Include.Should().BeEquivalentTo(new RelationIncludeDefinition
        {
            Allowed = true,
            MaxDepth = 2,
        });
    }

    [Fact]
    public async Task SqliteIncludeReturnsDeclaredTargetFromTheSameSnapshot()
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-include-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x71, 32).ToArray())
                .AddCollection(TypedIdOwner.Collection)
                .AddCollection(TypedIdDocument.Collection)
                .UseSqlite(options => options.DataSource = database));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "include-user" };
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
            var ownerId = BaseRecordId<TypedIdOwner>.Create("owner_1");
            BaseResult<BaseRecord<TypedIdDocument>> unavailable = await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document_1"), new TypedIdDocument { OwnerId = ownerId });
            unavailable.Should().BeOfType<BaseFailure<BaseRecord<TypedIdDocument>>>().Which.Error.Code.Should().Be("base.relation.targetUnavailable");
            BaseResult<BaseRecord<TypedIdOwner>> ownerCreated = await session.Collection(TypedIdOwner.Collection).CreateAsync(ownerId.Value, new TypedIdOwner { Name = "Owner" });
            ownerCreated.Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>(ownerCreated is BaseFailure<BaseRecord<TypedIdOwner>> failure ? failure.Error.Code : "unexpected result");
            (await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document_1"), new TypedIdDocument { OwnerId = ownerId })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();

            OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
                TypedIdDocument.Collection.Id,
                new RecordQuery
                {
                    Include = [new RecordInclude { NavigationId = "typed-id-document.owner" }],
                    Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
                },
                principal,
                new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });

            result.IsSuccess().Should().BeTrue();
            RecordIncludeResult included = result.Value!.Items.Should().ContainSingle().Which.Includes.Should().ContainSingle().Which;
            included.Kind.Should().Be(RecordIncludeKind.One);
            included.Record!.Id.Value.Should().Be("owner_1");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { database, database + "-wal", database + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task VolatileIncludeReturnsDeclaredTargetFromOnePublishedState()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder.AddCollection(TypedIdOwner.Collection).AddCollection(TypedIdDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "include-user" };
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
        var ownerId = BaseRecordId<TypedIdOwner>.Create("owner_1");
        (await session.Collection(TypedIdOwner.Collection).CreateAsync(ownerId.Value, new TypedIdOwner { Name = "Owner" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
        (await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document_1"), new TypedIdDocument { OwnerId = ownerId })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();

        OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdDocument.Collection.Id,
            new RecordQuery { Include = [new RecordInclude { NavigationId = "typed-id-document.owner" }], Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 } },
            principal,
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });

        result.IsSuccess().Should().BeTrue();
        result.Value!.Items.Single().Includes!.Single().Record!.Id.Value.Should().Be("owner_1");
    }

    [Fact]
    public async Task VolatileBatchesMultipleRootsAndExpandsNestedRelationsBeforeFieldSelection()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder.AddCollection(TypedIdOwner.Collection).AddCollection(TypedIdDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "include-user" };
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
        foreach (string suffix in new[] { "1", "2" })
        {
            var ownerId = BaseRecordId<TypedIdOwner>.Create("owner_" + suffix);
            (await session.Collection(TypedIdOwner.Collection).CreateAsync(ownerId.Value, new TypedIdOwner { Name = "Owner " + suffix })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
            (await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document_" + suffix), new TypedIdDocument { OwnerId = ownerId })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();
        }
        RecordInclude[] plan =
        [
            new RecordInclude
            {
                NavigationId = "typed-id-document.owner",
                Includes =
                [
                    new RecordInclude
                    {
                        NavigationId = "typed-id-owner.documents",
                        SelectFieldIds = [],
                        Includes = [new RecordInclude { NavigationId = "typed-id-document.owner" }],
                    },
                ],
            },
        ];

        OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdDocument.Collection.Id,
            new RecordQuery { Include = plan, Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 } },
            principal,
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });

        result.IsSuccess().Should().BeTrue(result.Error?.Code);
        result.Value!.Items.Should().HaveCount(2);
        foreach (RecordEnvelope root in result.Value.Items)
        {
            RecordEnvelope owner = root.Includes!.Single().Record!;
            RecordEnvelope inverseDocument = owner.Includes!.Single().Records.Should().ContainSingle().Which;
            inverseDocument.Payload.Fields.Should().BeEmpty();
            inverseDocument.Includes!.Single().Record!.Id.Value.Should().Be(owner.Id.Value);
        }
    }

    [Fact]
    public async Task VolatileRelationMutationChecksTargetAndRestrictsItsDeletionInTheAtomicSnapshot()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder.AddCollection(TypedIdOwner.Collection).AddCollection(TypedIdDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "relation-user" });
        var ownerId = BaseRecordId<TypedIdOwner>.Create("owner_1");

        BaseResult<BaseRecord<TypedIdDocument>> unavailable = await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document_1"), new TypedIdDocument { OwnerId = ownerId });
        unavailable.Should().BeOfType<BaseFailure<BaseRecord<TypedIdDocument>>>().Which.Error.Code.Should().Be("base.relation.targetUnavailable");

        (await session.Collection(TypedIdOwner.Collection).CreateAsync(ownerId.Value, new TypedIdOwner { Name = "Owner" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
        (await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document_1"), new TypedIdDocument { OwnerId = ownerId })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();

        BaseResult<DeleteResult> restricted = await session.Collection(TypedIdOwner.Collection).DeleteAsync(ownerId.Value);
        restricted.Should().BeOfType<BaseFailure<DeleteResult>>().Which.Error.Code.Should().Be("base.relation.deleteRestricted");
    }

    [Fact]
    public async Task AtomicRelationMutationReadsAnEarlierTargetWriteFromTheSameBatch()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder.AddCollection(TypedIdOwner.Collection).AddCollection(TypedIdDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "batch-user" });
        var ownerId = BaseRecordId<TypedIdOwner>.Create("owner_1");
        BaseBatchBuilder batch = session.Atomic();
        batch.Create(TypedIdOwner.Collection, ownerId.Value, new TypedIdOwner { Name = "Owner" });
        batch.Create(TypedIdDocument.Collection, new RecordId("document_1"), new TypedIdDocument { OwnerId = ownerId });

        BaseResult<BaseBatchResult> result = await batch.CommitAsync();

        result.Should().BeOfType<BaseSuccess<BaseBatchResult>>().Which.Value.Outcome.Should().Be(BaseRecordBatchOutcome.Committed);
    }

    [Fact]
    public async Task RelationPolicyDenialIsIndistinguishableFromAMissingTargetAndRollsBackSource()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, DenyRelationTargetPolicyEvaluator>();
        services.AddHPDBase(builder => builder.AddCollection(TypedIdOwner.Collection).AddCollection(TypedIdDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "policy-user" });
        var ownerId = BaseRecordId<TypedIdOwner>.Create("owner_1");
        (await session.Collection(TypedIdOwner.Collection).CreateAsync(ownerId.Value, new TypedIdOwner { Name = "Owner" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();

        BaseResult<BaseRecord<TypedIdDocument>> denied = await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document_1"), new TypedIdDocument { OwnerId = ownerId });

        denied.Should().BeOfType<BaseFailure<BaseRecord<TypedIdDocument>>>().Which.Error.Code.Should().Be("base.relation.targetUnavailable");
        (await session.Collection(TypedIdDocument.Collection).GetAsync(new RecordId("document_1"))).Should().BeOfType<BaseFailure<BaseRecord<TypedIdDocument>>>();
    }

    [Fact]
    public async Task NonCooperativeRelationPolicyIsBoundedAndRollsBackTheSource()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, HangingRelationTargetPolicyEvaluator>();
        services.AddHPDBase(builder => builder
            .ConfigureRuntime(options => options.Mutations.MaxTransactionDuration = TimeSpan.FromMilliseconds(50))
            .AddCollection(TypedIdOwner.Collection)
            .AddCollection(TypedIdDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "timeout-user" });
        var ownerId = BaseRecordId<TypedIdOwner>.Create("owner_1");
        (await session.Collection(TypedIdOwner.Collection).CreateAsync(ownerId.Value, new TypedIdOwner { Name = "Owner" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();

        BaseResult<BaseRecord<TypedIdDocument>> timedOut = await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document_1"), new TypedIdDocument { OwnerId = ownerId });

        timedOut.Should().BeOfType<BaseFailure<BaseRecord<TypedIdDocument>>>().Which.Error.Code.Should().Be("base.relation.policyTimeout");
        (await session.Collection(TypedIdDocument.Collection).GetAsync(new RecordId("document_1"))).Should().BeOfType<BaseFailure<BaseRecord<TypedIdDocument>>>();
    }

    [Fact]
    public async Task GeneratedManyRelationPreservesOrderAndRejectsDuplicateOrOutOfRangeTargets()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder
            .AddCollection(TypedIdOwner.Collection)
            .AddCollection(TypedIdManyDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "many-relation-user",
        });
        var first = BaseRecordId<TypedIdOwner>.Create("owner_1");
        var second = BaseRecordId<TypedIdOwner>.Create("owner_2");
        var third = BaseRecordId<TypedIdOwner>.Create("owner_3");
        foreach ((BaseRecordId<TypedIdOwner> id, string name) in new[] { (first, "First"), (second, "Second"), (third, "Third") })
            (await session.Collection(TypedIdOwner.Collection).CreateAsync(id.Value, new TypedIdOwner { Name = name })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();

        BaseResult<BaseRecord<TypedIdManyDocument>> created = await session.Collection(TypedIdManyDocument.Collection).CreateAsync(
            new RecordId("ordered"), new TypedIdManyDocument { Members = [second, first] });
        created.Should().BeOfType<BaseSuccess<BaseRecord<TypedIdManyDocument>>>();
        OperationResult<RecordPage> listed = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdManyDocument.Collection.Id,
            new RecordQuery
            {
                Include = [new RecordInclude { NavigationId = "typed-id-many-document.members" }],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            },
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "many-relation-user" },
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdManyDocument.Collection.Id });
        listed.Value!.Items.Single().Includes!.Single().Records!.Select(record => record.Id.Value)
            .Should().Equal("owner_2", "owner_1");

        BaseResult<BaseRecord<TypedIdManyDocument>> duplicate = await session.Collection(TypedIdManyDocument.Collection).CreateAsync(
            new RecordId("duplicate"), new TypedIdManyDocument { Members = [first, first] });
        duplicate.Should().BeOfType<BaseFailure<BaseRecord<TypedIdManyDocument>>>().Which.Error.Code.Should().Be("base.relation.cardinalityInvalid");
        BaseResult<BaseRecord<TypedIdManyDocument>> tooMany = await session.Collection(TypedIdManyDocument.Collection).CreateAsync(
            new RecordId("too-many"), new TypedIdManyDocument { Members = [first, second, third] });
        tooMany.Should().BeOfType<BaseFailure<BaseRecord<TypedIdManyDocument>>>().Which.Error.Code.Should().Be("base.relation.cardinalityInvalid");
        BaseResult<BaseRecord<TypedIdManyDocument>> tooFew = await session.Collection(TypedIdManyDocument.Collection).CreateAsync(
            new RecordId("too-few"), new TypedIdManyDocument { Members = [] });
        tooFew.Should().BeOfType<BaseFailure<BaseRecord<TypedIdManyDocument>>>().Which.Error.Code.Should().Be("base.relation.cardinalityInvalid");

        OperationResult<RecordPage> shaped = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdManyDocument.Collection.Id,
            new RecordQuery
            {
                Include =
                [
                    new RecordInclude
                    {
                        NavigationId = "typed-id-many-document.members",
                        Filter = new FilterExpression
                        {
                            Kind = FilterNodeKind.Compare,
                            Field = TypedIdOwner.Fields.Name.Id,
                            Operator = FilterOperator.NotEqual,
                            Value = new QueryValue { Kind = QueryValueKind.String, String = "First" },
                        },
                        Sort = [new QuerySort { Field = TypedIdOwner.Fields.Name.Id, Direction = QuerySortDirection.Desc }],
                        Limit = 1,
                        SelectFieldIds = [TypedIdOwner.Fields.Name.Id],
                    },
                ],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            },
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "many-relation-user" },
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdManyDocument.Collection.Id });
        RecordEnvelope selected = shaped.Value!.Items.Single().Includes!.Single().Records.Should().ContainSingle().Which;
        selected.Id.Value.Should().Be("owner_2");
        selected.Payload.Fields.Should().ContainSingle().Which.Key.Should().Be("name");

        OperationResult<RecordPage> tooDeep = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdManyDocument.Collection.Id,
            new RecordQuery
            {
                Include = [new RecordInclude
                {
                    NavigationId = "typed-id-many-document.members",
                    Includes = [new RecordInclude
                    {
                        NavigationId = "typed-id-owner.many-documents",
                        Includes = [new RecordInclude { NavigationId = "typed-id-many-document.members" }],
                    }],
                }],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            },
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "many-relation-user" },
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdManyDocument.Collection.Id });
        tooDeep.Error!.Code.Should().Be("base.include.limitExceeded");
    }

    [Fact]
    public async Task SqliteManyIncludeAppliesFilterSortSelectAndLimitWithoutChangingRootPage()
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-many-include-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x77, 32).ToArray())
                .AddCollection(TypedIdOwner.Collection)
                .AddCollection(TypedIdManyDocument.Collection)
                .UseSqlite(options => options.DataSource = database));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan schema = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = schema.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "sqlite-many-user" };
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
            var first = BaseRecordId<TypedIdOwner>.Create("owner_1");
            var second = BaseRecordId<TypedIdOwner>.Create("owner_2");
            (await session.Collection(TypedIdOwner.Collection).CreateAsync(first.Value, new TypedIdOwner { Name = "First" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
            (await session.Collection(TypedIdOwner.Collection).CreateAsync(second.Value, new TypedIdOwner { Name = "Second" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
            (await session.Collection(TypedIdManyDocument.Collection).CreateAsync(new RecordId("document"), new TypedIdManyDocument { Members = [first, second] })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdManyDocument>>>();
            (await session.Collection(TypedIdManyDocument.Collection).CreateAsync(new RecordId("document-2"), new TypedIdManyDocument { Members = [second] })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdManyDocument>>>();

            OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
                TypedIdManyDocument.Collection.Id,
                new RecordQuery
                {
                    Include = [new RecordInclude
                    {
                        NavigationId = "typed-id-many-document.members",
                        Filter = new FilterExpression
                        {
                            Kind = FilterNodeKind.Compare, Field = TypedIdOwner.Fields.Name.Id,
                            Operator = FilterOperator.NotEqual,
                            Value = new QueryValue { Kind = QueryValueKind.String, String = "First" },
                        },
                        Sort = [new QuerySort { Field = TypedIdOwner.Fields.Name.Id, Direction = QuerySortDirection.Desc }],
                        SelectFieldIds = [TypedIdOwner.Fields.Name.Id], Limit = 1,
                        Includes = [new RecordInclude { NavigationId = "typed-id-owner.many-documents" }],
                    }],
                    Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
                    Count = QueryCountMode.Exact,
                }, principal,
                new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdManyDocument.Collection.Id });

            result.IsSuccess().Should().BeTrue(result.Error?.Code);
            result.Value!.Items.Select(static item => item.Id.Value).Should().BeEquivalentTo(["document", "document-2"]);
            result.Value.Count!.Total.Should().Be(2);
            foreach (RecordEnvelope root in result.Value.Items)
            {
                RecordEnvelope member = root.Includes!.Single().Records.Should().ContainSingle().Which;
                member.Id.Value.Should().Be("owner_2");
                member.Payload.Fields.Should().ContainSingle().Which.Key.Should().Be("name");
                member.Includes!.Single().Records!.Select(static item => item.Id.Value)
                    .Should().BeEquivalentTo(["document", "document-2"]);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { database, database + "-wal", database + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task HostileIncludeProviderCannotReturnTruncatedOrUntrustedResults()
    {
        var store = new HostileIncludeStore();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder
            .ConfigureRelational(options => options.MaxIncludedRecords = 1)
            .AddCollection(TypedIdOwner.Collection)
            .AddCollection(TypedIdManyDocument.Collection)
            .Use(new HostileIncludeInstaller(store)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        IBaseRecordRuntime runtime = provider.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.System };
        RecordQuery query = new()
        {
            Include = [new RecordInclude { NavigationId = "typed-id-many-document.members" }],
            Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
        };

        store.Response = Response([Child("one"), Child("two")],
            [new() { CollectionId = TypedIdManyDocument.Collection.Id }, new() { CollectionId = TypedIdOwner.Collection.Id }]);
        OperationResult<RecordPage> oversized = await ExecuteAsync();
        Assert.Equal("base.include.limitExceeded", oversized.Error!.Code);

        store.Response = Response([Child("one")],
            [new() { CollectionId = TypedIdManyDocument.Collection.Id }, new() { CollectionId = "unknown" }]);
        OperationResult<RecordPage> unknownEvidence = await ExecuteAsync();
        unknownEvidence.Error!.Code.Should().Be("base.relational.dependencies.invalid");

        store.Response = Response([Child("one")],
            [new() { CollectionId = TypedIdManyDocument.Collection.Id }, new() { CollectionId = TypedIdOwner.Collection.Id }, new() { CollectionId = TypedIdOwner.Collection.Id }]);
        OperationResult<RecordPage> duplicateEvidence = await ExecuteAsync();
        duplicateEvidence.Error!.Code.Should().Be("base.relational.dependencies.invalid");

        ValueTask<OperationResult<RecordPage>> ExecuteAsync() => runtime.ListAsync(
            TypedIdManyDocument.Collection.Id, query, principal,
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdManyDocument.Collection.Id });
        static RecordEnvelope Child(string id) => new()
        {
            CollectionId = TypedIdOwner.Collection.Id, Id = new RecordId(id),
            Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = [] }, Metadata = new RecordMetadata(),
        };
        static RecordIncludeExecutionResult Response(RecordEnvelope[] children, BaseReadDependencyEvidence[] evidence) => new()
        {
            Page = new RecordPage
            {
                Items = [new RecordEnvelope
                {
                    CollectionId = TypedIdManyDocument.Collection.Id, Id = new RecordId("root"),
                    Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = [] }, Metadata = new RecordMetadata(),
                    Includes = [new RecordIncludeResult { NavigationId = "typed-id-many-document.members", Kind = RecordIncludeKind.Many, Records = children }],
                }],
                Page = new PageInfo { Offset = 0, Limit = 10 },
            },
            DependencyEvidence = evidence,
        };
    }

    [Fact]
    public async Task CrossStoreIncludeIsRejectedBeforeRootExecution()
    {
        var root = new HostileIncludeStore();
        var target = new FakeRecordStore("include-target");
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder
            .AddCollection(TypedIdOwner.Collection)
            .AddCollection(TypedIdManyDocument.Collection)
            .Use(new CrossStoreIncludeInstaller(root, target)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

        OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdManyDocument.Collection.Id,
            new RecordQuery
            {
                Include = [new RecordInclude { NavigationId = "typed-id-many-document.members" }],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            },
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System },
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdManyDocument.Collection.Id });

        result.Error!.Code.Should().Be("base.include.snapshotUnsupported");
        root.ExecutionCalls.Should().Be(0);
    }

    [Fact]
    public async Task IncludePolicyDenialUsesOneNonDisclosingStableFailure()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, DenyOwnerIncludePolicyEvaluator>();
        services.AddHPDBase(builder => builder.AddCollection(TypedIdOwner.Collection).AddCollection(TypedIdDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "include-policy" };
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
        BaseRecordId<TypedIdOwner> owner = BaseRecordId<TypedIdOwner>.Create("secret-owner");
        (await session.Collection(TypedIdOwner.Collection).CreateAsync(owner.Value, new TypedIdOwner { Name = "Secret" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
        (await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document"), new TypedIdDocument { OwnerId = owner })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();

        OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdDocument.Collection.Id,
            new RecordQuery
            {
                Include = [new RecordInclude { NavigationId = "typed-id-document.owner" }],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            }, principal,
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });

        result.Error!.Code.Should().Be("base.include.policyUnsupported");
        result.Error.Message.ToLowerInvariant().Should().NotContain("secret");
    }
}

internal sealed class HostileIncludeInstaller(HostileIncludeStore store) : IHPDBaseBuilderExtension
{
    public string Id => "hostile-include";
    public bool IsRecordProvider => true;
    public bool SupportsRequiredIndexes => true;
    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
    {
        services.AddSingleton(store);
        services.AddSingleton<IBaseDescriptorContributor>(new HostileIncludeDescriptorContributor(collections));
    }
    public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        services.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = Id, Store = store,
            CollectionIds = [TypedIdOwner.Collection.Id, TypedIdManyDocument.Collection.Id],
        });
        return ValueTask.CompletedTask;
    }
}

internal sealed class HostileIncludeDescriptorContributor(IReadOnlyList<CollectionDefinition> collections) : IBaseDescriptorContributor
{
    public string Id => "hostile-include-descriptor";
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        foreach (CollectionDefinition collection in collections)
            builder.AddCollection(collection with { Store = new StoreAnnotation { StoreId = "hostile-include", Owner = EnforcementOwner.Store } });
    }
}

internal sealed class CrossStoreIncludeInstaller(HostileIncludeStore root, FakeRecordStore target) : IHPDBaseBuilderExtension
{
    public string Id => "cross-store-include";
    public bool IsRecordProvider => true;
    public bool SupportsRequiredIndexes => true;
    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
    {
        services.AddSingleton(root);
        services.AddSingleton<IBaseDescriptorContributor>(new CrossStoreIncludeDescriptorContributor(collections));
    }
    public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        IRecordStoreRegistry registry = services.GetRequiredService<IRecordStoreRegistry>();
        registry.Add(new RecordStoreRegistration { StoreId = root.Capabilities.StoreId, Store = root, CollectionIds = [TypedIdManyDocument.Collection.Id] });
        registry.Add(new RecordStoreRegistration { StoreId = target.Capabilities.StoreId, Store = target, CollectionIds = [TypedIdOwner.Collection.Id] });
        return ValueTask.CompletedTask;
    }
}

internal sealed class CrossStoreIncludeDescriptorContributor(IReadOnlyList<CollectionDefinition> collections) : IBaseDescriptorContributor
{
    public string Id => "cross-store-include-descriptor";
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        foreach (CollectionDefinition collection in collections)
            builder.AddCollection(collection with
            {
                Store = new StoreAnnotation
                {
                    StoreId = collection.Id == TypedIdOwner.Collection.Id ? "include-target" : "hostile-include",
                    Owner = EnforcementOwner.Store,
                },
            });
    }
}

internal sealed class HostileIncludeStore() : FakeRecordStore("hostile-include"), IConsistentRecordIncludeStore
{
    public RecordIncludeExecutionCapability Includes { get; } = new()
    {
        Supported = true, SnapshotConsistency = true, MaxDepth = 8, MaxIncludes = 16, MaxRecords = 100,
    };
    public RecordIncludeExecutionResult? Response { get; set; }
    public int ExecutionCalls { get; private set; }
    public ValueTask<OperationResult<RecordIncludeExecutionResult>> ExecuteIncludeAsync(
        RecordIncludeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ExecutionCalls++;
        return ValueTask.FromResult(OperationResults.Ok(Response!));
    }
}

internal sealed class DenyRelationTargetPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(request.Resource.Kind == PolicyResourceKind.RelationTarget
            ? PolicyDecision.Deny("secret.reason", "secret message")
            : PolicyDecision.Allow());
    }
}

internal sealed class HangingRelationTargetPolicyEvaluator : IPolicyEvaluator
{
    private static readonly TaskCompletionSource<PolicyDecision> Never = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
        request.Resource.Kind == PolicyResourceKind.RelationTarget
            ? new ValueTask<PolicyDecision>(Never.Task)
            : ValueTask.FromResult(PolicyDecision.Allow());
}

internal sealed class DenyOwnerIncludePolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Collection.Id == TypedIdOwner.Collection.Id && request.Resource.Kind == PolicyResourceKind.Query
            ? PolicyDecision.Deny("secret.policy.code", "secret policy message")
            : PolicyDecision.Allow());
}

[BaseCollection("typed-id-documents", typeof(TypedIdJsonContext))]
internal sealed partial record TypedIdDocument
{
    [BaseField("typed-id-document.owner")]
    [BaseRelation(
        "typed-id-document.owner",
        typeof(TypedIdOwner),
        LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne,
        InverseNavigationId = "typed-id-owner.documents",
        IncludeAllowed = true)]
    public required BaseRecordId<TypedIdOwner> OwnerId { get; init; }
}

[BaseCollection("typed-id-owners", typeof(TypedIdJsonContext))]
internal sealed partial record TypedIdOwner
{
    [BaseField("typed-id-owner.name")]
    public required string Name { get; init; }
}

[BaseCollection("typed-id-many-documents", typeof(TypedIdJsonContext))]
internal sealed partial record TypedIdManyDocument
{
    [BaseField("typed-id-many-document.members")]
    [BaseRelation(
        "typed-id-many-document.members",
        typeof(TypedIdOwner),
        LocalMultiplicity = BaseRelationMultiplicity.Many,
        InverseNavigationId = "typed-id-owner.many-documents",
        MinimumCount = 1,
        MaximumCount = 2,
        IncludeAllowed = true,
        IncludeFilterAllowed = true,
        IncludeSortAllowed = true,
        IncludeMaximumDepth = 2)]
    public required BaseRecordId<TypedIdOwner>[] Members { get; init; }
}

[JsonSerializable(typeof(TypedIdDocument))]
[JsonSerializable(typeof(TypedIdOwner))]
[JsonSerializable(typeof(TypedIdManyDocument))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class TypedIdJsonContext : JsonSerializerContext;
