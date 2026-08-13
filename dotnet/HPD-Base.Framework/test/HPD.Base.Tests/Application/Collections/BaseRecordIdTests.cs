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
        var metadata = new TypedIdJsonContext(
            BaseSerializerGeneratedContract.CreateOptions(System.Text.Json.JsonNamingPolicy.CamelCase)).TypedIdDocument;
        var manual = HPD.Base.BaseCollection.Define(
            "manual-typed-id-documents",
            metadata,
            schema => schema.Relation(
                    "typed-id-document.owner",
                    "typed-id-document.owner",
                    "OwnerId",
                    BaseJsonProperty<TypedIdDocument, BaseRecordId<TypedIdOwner>>.Bind(metadata, "ownerId"),
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
                .UseStore(SqliteStore.Configure(options => options.DataSource = database)));
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
    public async Task InMemoryIncludeReturnsDeclaredTargetFromOnePublishedState()
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
    public async Task InMemoryBatchesMultipleRootsAndExpandsNestedRelationsBeforeFieldSelection()
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
    public async Task InMemoryRelationMutationChecksTargetAndRestrictsItsDeletionInTheAtomicSnapshot()
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
    public async Task InMemoryIdentifiedAtomicRequestReplaysWithoutMutatingAgain()
    {
        var services = new ServiceCollection().AddLogging();
        var observer = new ReceiptMutationObserver();
        services.AddSingleton<IBaseCommittedMutationObserver>(observer);
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder.AddCollection(TypedIdOwner.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "receipt-user",
        });
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "tenant_1", "create-owner", "request_1",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("request_1"u8)));

        BaseBatchBuilder first = session.Atomic(identity);
        first.Create(TypedIdOwner.Collection, new RecordId("owner_1"), new TypedIdOwner { Name = "Owner" });
        BaseBatchBuilder retry = session.Atomic(identity);
        retry.Create(TypedIdOwner.Collection, new RecordId("owner_1"), new TypedIdOwner { Name = "Owner" });

        BaseSuccess<BaseBatchResult> committed = (BaseSuccess<BaseBatchResult>)await first.CommitAsync();
        BaseSuccess<BaseBatchResult> duplicate = (BaseSuccess<BaseBatchResult>)await retry.CommitAsync();

        committed.Value.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.Value.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.Value.RequireCommitted().Should().NotBeNull();
        observer.Count.Should().Be(1);

        BaseBatchBuilder structuralConflict = session.Atomic(identity);
        structuralConflict.Create(TypedIdOwner.Collection, new RecordId("owner_2"), new TypedIdOwner { Name = "Different" });
        ((BaseFailure<BaseBatchResult>)await structuralConflict.CommitAsync()).Error.Code
            .Should().Be(BaseMutationRequestErrorCodes.FingerprintConflict);

        BaseMutationRequestIdentity conflictingIdentity = BaseMutationRequestIdentity.Create(
            "tenant_1", "create-owner", "request_1",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("different"u8)));
        BaseBatchBuilder conflict = session.Atomic(conflictingIdentity);
        conflict.Create(TypedIdOwner.Collection, new RecordId("owner_2"), new TypedIdOwner { Name = "Different" });
        BaseFailure<BaseBatchResult> conflictResult = (BaseFailure<BaseBatchResult>)await conflict.CommitAsync();
        conflictResult.Error.Code.Should().Be(BaseMutationRequestErrorCodes.FingerprintConflict);
    }

    [Fact]
    public async Task InMemoryReceiptExpiryUsesTheHostClockAndReexecutesAsNew()
    {
        var clock = new ReceiptTimeProvider(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder
            .ConfigureRuntime(options => options.Mutations.ReceiptLifetime = TimeSpan.FromHours(1))
            .AddCollection(TypedIdOwner.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "tenant_1", "create-owner", "expiring-request",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("expiry"u8)));

        (await ReceiptBatch(provider, identity).CommitAsync()).Should().BeOfType<BaseSuccess<BaseBatchResult>>();
        clock.Advance(TimeSpan.FromHours(2));
        BaseResult<BaseBatchResult> retried = await ReceiptBatch(provider, identity).CommitAsync();

        retried.Should().BeOfType<BaseSuccess<BaseBatchResult>>()
            .Which.Value.Outcome.Should().Be(BaseRecordBatchOutcome.RolledBack);
    }

    [Fact]
    public async Task ReceiptOverflowRollsBackRecordsAndLeavesNoIdentityReservation()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder
            .ConfigureRuntime(options => options.Mutations.MaxReceiptBytes = 4_096)
            .AddCollection(TypedIdOwner.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "tenant_1", "create-owner", "overflow-request",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("overflow"u8)));
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "receipt-user",
        });
        BaseBatchBuilder oversized = session.Atomic(identity);
        oversized.Create(TypedIdOwner.Collection, new RecordId("owner_1"), new TypedIdOwner { Name = new string('x', 8_192) });

        BaseFailure<BaseBatchResult> rejected = (BaseFailure<BaseBatchResult>)await oversized.CommitAsync();
        BaseBatchBuilder retry = session.Atomic(identity);
        retry.Create(TypedIdOwner.Collection, new RecordId("owner_1"), new TypedIdOwner { Name = "Owner" });
        BaseSuccess<BaseBatchResult> committed = (BaseSuccess<BaseBatchResult>)await retry.CommitAsync();

        rejected.Error.Code.Should().Be(BaseMutationRequestErrorCodes.ReceiptTooLarge);
        committed.Value.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
    }

    [Fact]
    public async Task ReceiptDisclosureAuthorizationPrecedesBothDigestComparisons()
    {
        var services = new ServiceCollection().AddLogging();
        var policy = new ToggleReceiptPolicyEvaluator();
        services.AddSingleton<IPolicyEvaluator>(policy);
        services.AddHPDBase(builder => builder.AddCollection(TypedIdOwner.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPolicyEvaluator>().Should().BeSameAs(policy);
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "receipt-user",
        });
        BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData("request"u8));
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "tenant_1", "create-owner", "request_1", fingerprint);
        BaseBatchBuilder initial = session.Atomic(identity);
        initial.Create(TypedIdOwner.Collection, new RecordId("owner_1"), new TypedIdOwner { Name = "Owner" });
        (await initial.CommitAsync()).Should().BeOfType<BaseSuccess<BaseBatchResult>>();

        policy.Allow = false;
        BaseSession deniedSession = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "denied-receipt-user",
        });
        BaseBatchBuilder matching = deniedSession.Atomic(identity);
        matching.Create(TypedIdOwner.Collection, new RecordId("owner_1"), new TypedIdOwner { Name = "Owner" });
        BaseMutationRequestIdentity differentFingerprint = BaseMutationRequestIdentity.Create(
            "tenant_1", "create-owner", "request_1",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("different"u8)));
        BaseBatchBuilder conflicting = deniedSession.Atomic(differentFingerprint);
        conflicting.Create(TypedIdOwner.Collection, new RecordId("owner_2"), new TypedIdOwner { Name = "Different" });

        BaseResult<BaseBatchResult> matchingResult = await matching.CommitAsync();
        policy.DenyCalls.Should().BeGreaterThan(0);
        BaseResult<BaseBatchResult> conflictingResult = await conflicting.CommitAsync();
        BaseFailure<BaseBatchResult> matchingDenied = matchingResult.Should().BeOfType<BaseFailure<BaseBatchResult>>().Subject;
        BaseFailure<BaseBatchResult> conflictingDenied = conflictingResult.Should().BeOfType<BaseFailure<BaseBatchResult>>().Subject;

        matchingDenied.Status.Should().Be(conflictingDenied.Status);
        matchingDenied.Error.Code.Should().Be("base.policy.denied");
        conflictingDenied.Error.Code.Should().Be(matchingDenied.Error.Code);
        conflictingDenied.Error.Code.Should().NotBe(BaseMutationRequestErrorCodes.FingerprintConflict);
    }

    [Fact]
    public async Task SqliteIdentifiedAtomicRequestSurvivesProviderRestart()
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-receipt-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
                "tenant_1", "create-owner", "request_1",
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("sqlite-request"u8)));

            await using (ServiceProvider firstProvider = BuildSqliteReceiptProvider(database))
            {
                IBaseSchemaManager schemas = firstProvider.GetRequiredService<IBaseSchemaManager>();
                BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
                (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
                (await firstProvider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
                BaseBatchBuilder first = ReceiptBatch(firstProvider, identity);
                ((BaseSuccess<BaseBatchResult>)await first.CommitAsync()).Value.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
            }

            await using (ServiceProvider secondProvider = BuildSqliteReceiptProvider(database))
            {
                (await secondProvider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
                BaseBatchBuilder retry = ReceiptBatch(secondProvider, identity);
                ((BaseSuccess<BaseBatchResult>)await retry.CommitAsync()).Value.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            }
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task SqliteReceiptAuthorizationAlsoPrecedesConflictDisclosure()
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-receipt-policy-" + Guid.NewGuid().ToString("N") + ".db");
        var policy = new ToggleReceiptPolicyEvaluator();
        try
        {
            await using ServiceProvider provider = BuildSqliteReceiptProvider(database, policy);
            provider.GetRequiredService<IPolicyEvaluator>().Should().BeSameAs(policy);
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
                "tenant_1", "create-owner", "request_1",
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("sqlite-policy"u8)));
            (await ReceiptBatch(provider, identity).CommitAsync()).Should().BeOfType<BaseSuccess<BaseBatchResult>>();

            policy.Allow = false;
            BaseMutationRequestIdentity conflict = BaseMutationRequestIdentity.Create(
                "tenant_1", "create-owner", "request_1",
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("sqlite-conflict"u8)));
            BaseResult<BaseBatchResult> deniedResult = await ReceiptBatch(provider, conflict).CommitAsync();
            policy.DenyCalls.Should().BeGreaterThan(0);
            BaseFailure<BaseBatchResult> denied = deniedResult.Should().BeOfType<BaseFailure<BaseBatchResult>>().Subject;

            denied.Status.Should().Be(OperationStatus.PolicyDenied);
            denied.Error.Code.Should().Be("base.policy.denied");
            denied.Error.Code.Should().NotBe(BaseMutationRequestErrorCodes.FingerprintConflict);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task SqliteReceiptOverflowRollsBackRecordsAndLeavesNoIdentityReservation()
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-receipt-overflow-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using ServiceProvider provider = BuildSqliteReceiptProvider(database, maxReceiptBytes: 4_096);
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
                "tenant_1", "create-owner", "overflow-request",
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("sqlite-overflow"u8)));
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectId = "receipt-user",
            });
            BaseBatchBuilder oversized = session.Atomic(identity);
            oversized.Create(TypedIdOwner.Collection, new RecordId("owner_1"), new TypedIdOwner { Name = new string('x', 8_192) });

            BaseFailure<BaseBatchResult> rejected = (BaseFailure<BaseBatchResult>)await oversized.CommitAsync();
            BaseSuccess<BaseBatchResult> committed = (BaseSuccess<BaseBatchResult>)await ReceiptBatch(provider, identity).CommitAsync();

            rejected.Error.Code.Should().Be(BaseMutationRequestErrorCodes.ReceiptTooLarge);
            committed.Value.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    private static ServiceProvider BuildSqliteReceiptProvider(
        string database,
        IPolicyEvaluator? policy = null,
        int? maxReceiptBytes = null)
    {
        var services = new ServiceCollection().AddLogging();
        if (policy is null) services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        else services.AddSingleton(policy);
        services.AddHPDBase(builder => builder
            .ConfigureRuntime(options =>
            {
                if (maxReceiptBytes is { } maximum) options.Mutations.MaxReceiptBytes = maximum;
            })
            .ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x41, 32).ToArray())
            .AddCollection(TypedIdOwner.Collection)
            .UseStore(SqliteStore.Configure(options => options.DataSource = database)));
        return services.BuildServiceProvider();
    }

    private static BaseBatchBuilder ReceiptBatch(ServiceProvider provider, BaseMutationRequestIdentity identity)
    {
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "receipt-user",
        });
        BaseBatchBuilder batch = session.Atomic(identity);
        batch.Create(TypedIdOwner.Collection, new RecordId("owner_1"), new TypedIdOwner { Name = "Owner" });
        return batch;
    }

    private sealed class ReceiptTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;
        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan duration) => _value += duration;
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
                .UseStore(SqliteStore.Configure(options => options.DataSource = database)));
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
            .UseStore(TestStoreProvider.Create(store)));
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

        store.Response = Response([Child("one") with
        {
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = new Dictionary<string, JsonElement>
                {
                    ["provider-secret"] = JsonDocument.Parse("\"must-not-leak\"").RootElement.Clone(),
                },
            },
        }],
            [new() { CollectionId = TypedIdManyDocument.Collection.Id }, new() { CollectionId = TypedIdOwner.Collection.Id }]);
        OperationResult<RecordPage> leakedField = await ExecuteAsync();
        leakedField.Error!.Code.Should().Be("base.include.invalid");
        leakedField.Error.Message.Should().NotContain("provider-secret").And.NotContain("must-not-leak");

        store.Response = Response([Child("one")],
            [new() { CollectionId = TypedIdManyDocument.Collection.Id }, new() { CollectionId = TypedIdOwner.Collection.Id }]) with
        {
            Page = Response([Child("one")],
                [new() { CollectionId = TypedIdManyDocument.Collection.Id }, new() { CollectionId = TypedIdOwner.Collection.Id }]).Page with
            {
                Items = [Response([Child("one")], []).Page.Items[0] with
                {
                    Includes = [new RecordIncludeResult { NavigationId = "typed-id-many-document.members", Kind = RecordIncludeKind.One, Record = Child("one") }],
                }],
            },
        };
        (await ExecuteAsync()).Error!.Code.Should().Be("base.include.invalid");

        store.Response = Response([Child("one") with { CollectionId = "wrong-collection" }],
            [new() { CollectionId = TypedIdManyDocument.Collection.Id }, new() { CollectionId = TypedIdOwner.Collection.Id }]);
        (await ExecuteAsync()).Error!.Code.Should().Be("base.include.invalid");

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
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeFalse();
        root.ExecutionCalls.Should().Be(0);
    }

    [Fact]
    public async Task IncludePolicyDenialUsesOneNonDisclosingStableFailure()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, DenyOwnerIncludePolicyEvaluator>();
        services.AddHPDBase(builder => builder
            .AddCollection(TypedIdOwner.Collection)
            .AddCollection(TypedIdDocument.Collection)
            .AddCollection(TypedIdManyDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "include-policy" };
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
        BaseRecordId<TypedIdOwner> owner = BaseRecordId<TypedIdOwner>.Create("secret-owner");
        (await session.Collection(TypedIdOwner.Collection).CreateAsync(owner.Value, new TypedIdOwner { Name = "Secret" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
        (await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document"), new TypedIdDocument { OwnerId = owner })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();
        (await session.Collection(TypedIdManyDocument.Collection).CreateAsync(new RecordId("many-document"), new TypedIdManyDocument { Members = [owner] })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdManyDocument>>>();

        OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdDocument.Collection.Id,
            new RecordQuery
            {
                Include = [new RecordInclude { NavigationId = "typed-id-document.owner" }],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            }, principal,
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });

        result.IsSuccess().Should().BeTrue(result.Error?.Code);
        RecordIncludeResult included = result.Value!.Items.Should().ContainSingle().Which.Includes.Should().ContainSingle().Which;
        included.Kind.Should().Be(RecordIncludeKind.None);
        included.Record.Should().BeNull();
        included.Records.Should().BeNull();

        OperationResult<RecordPage> many = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdManyDocument.Collection.Id,
            new RecordQuery
            {
                Include = [new RecordInclude { NavigationId = "typed-id-many-document.members" }],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            }, principal,
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdManyDocument.Collection.Id });
        many.IsSuccess().Should().BeTrue(many.Error?.Code);
        RecordIncludeResult deniedMany = many.Value!.Items.Single().Includes!.Single();
        deniedMany.Kind.Should().Be(RecordIncludeKind.Many);
        deniedMany.Records.Should().BeEmpty();
        deniedMany.Record.Should().BeNull();

        OperationResult<RecordPage> malformedBelowDenied = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdDocument.Collection.Id,
            new RecordQuery
            {
                Include = [new RecordInclude
                {
                    NavigationId = "typed-id-document.owner",
                    Includes = [new RecordInclude { NavigationId = "undeclared-navigation" }],
                }],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            }, principal,
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });
        malformedBelowDenied.Error!.Code.Should().Be("base.include.invalid");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidNestedIncludeBelowDeniedParentRemainsNonDisclosingAndCarriesCompleteEvidence(bool sqlite)
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-denied-nested-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton<IPolicyEvaluator, DenyOwnerIncludePolicyEvaluator>();
            services.AddHPDBase(builder =>
            {
                builder.AddCollection(TypedIdOwner.Collection)
                    .AddCollection(TypedIdDocument.Collection)
                    .AddCollection(TypedIdManyDocument.Collection);
                if (sqlite)
                {
                    builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x7A, 32).ToArray());
                    builder.UseStore(SqliteStore.Configure(options => options.DataSource = database));
                }
            });
            await using ServiceProvider provider = services.BuildServiceProvider();
            if (sqlite)
            {
                IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
                BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
                (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            }
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "denied-nested" };
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
            BaseRecordId<TypedIdOwner> owner = BaseRecordId<TypedIdOwner>.Create("owner");
            (await session.Collection(TypedIdOwner.Collection).CreateAsync(owner.Value, new TypedIdOwner { Name = "Secret" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
            (await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document"), new TypedIdDocument { OwnerId = owner })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();

            OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
                TypedIdDocument.Collection.Id,
                new RecordQuery
                {
                    Include = [new RecordInclude
                    {
                        NavigationId = "typed-id-document.owner",
                        Includes = [new RecordInclude { NavigationId = "typed-id-owner.many-documents" }],
                    }],
                    Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
                }, principal,
                new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });

            result.IsSuccess().Should().BeTrue($"{result.Error?.Code}: {result.Error?.Message}");
            RecordIncludeResult denied = result.Value!.Items.Single().Includes!.Single();
            denied.Kind.Should().Be(RecordIncludeKind.None);
            denied.Record.Should().BeNull();
            denied.Records.Should().BeNull();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { database, database + "-wal", database + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncludesNeverExposeFieldsHiddenFromTheTargetSchemaView(bool sqlite)
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-include-visibility-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            CollectionDefinition hiddenDefinition = TypedIdOwner.Collection.Definition with
            {
                Fields = TypedIdOwner.Collection.Definition.Fields!.Select(field => field.Id == TypedIdOwner.Fields.Name.Id
                    ? field with { Visibility = new FieldVisibilityAnnotation { Visibility = VisibilityLevel.Admin } }
                    : field).ToArray(),
            };
            BaseCollection<TypedIdOwner> hiddenOwner = TypedIdOwner.Collection.WithDefinition(hiddenDefinition);
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
            services.AddHPDBase(builder =>
            {
                builder.AddCollection(hiddenOwner).AddCollection(TypedIdDocument.Collection);
                if (sqlite)
                {
                    builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x79, 32).ToArray());
                    builder.UseStore(SqliteStore.Configure(options => options.DataSource = database));
                }
            });
            await using ServiceProvider provider = services.BuildServiceProvider();
            if (sqlite)
            {
                IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
                BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
                (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            }
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "include-visibility" };
            PrincipalContext administrator = new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "include-administrator" };
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(administrator);
            BaseRecordId<TypedIdOwner> ownerId = BaseRecordId<TypedIdOwner>.Create("owner-hidden");
            (await session.Collection(hiddenOwner).CreateAsync(ownerId.Value, new TypedIdOwner { Name = "must-not-leak" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
            (await session.Collection(TypedIdDocument.Collection).CreateAsync(new RecordId("document"), new TypedIdDocument { OwnerId = ownerId })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();

            OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
                TypedIdDocument.Collection.Id,
                new RecordQuery
                {
                    Include = [new RecordInclude { NavigationId = "typed-id-document.owner" }],
                    Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
                },
                principal,
                new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });

            result.IsSuccess().Should().BeTrue(result.Error?.Code);
            result.Value!.Items.Single().Includes!.Single().Record!.Payload.Fields.Should().BeEmpty();

            OperationResult<RecordPage> nested = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
                TypedIdDocument.Collection.Id,
                new RecordQuery
                {
                    Include = [new RecordInclude
                    {
                        NavigationId = "typed-id-document.owner",
                        Includes = [new RecordInclude
                        {
                            NavigationId = "typed-id-owner.documents",
                            Includes = [new RecordInclude { NavigationId = "typed-id-document.owner" }],
                        }],
                    }],
                    Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
                },
                principal,
                new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdDocument.Collection.Id });

            nested.IsSuccess().Should().BeTrue($"{nested.Error?.Code}: {nested.Error?.Message}");
            RecordEnvelope nestedOwner = nested.Value!.Items.Single().Includes!.Single().Record!
                .Includes!.Single().Records!.Single().Includes!.Single().Record!;
            nestedOwner.Payload.Fields.Should().BeEmpty();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { database, database + "-wal", database + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task HiddenInverseRelationCannotBeResolvedThroughTheCanonicalRegistry()
    {
        CollectionDefinition hiddenDocumentDefinition = TypedIdDocument.Collection.Definition with
        {
            Fields = TypedIdDocument.Collection.Definition.Fields!.Select(field => field.Id == TypedIdDocument.Fields.OwnerId.Id
                ? field with { Visibility = new FieldVisibilityAnnotation { Visibility = VisibilityLevel.Admin } }
                : field).ToArray(),
        };
        BaseCollection<TypedIdDocument> hiddenDocument = TypedIdDocument.Collection.WithDefinition(hiddenDocumentDefinition);
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBase(builder => builder
            .AddCollection(TypedIdOwner.Collection)
            .AddCollection(hiddenDocument)
            .AddCollection(TypedIdManyDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        PrincipalContext administrator = new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "inverse-admin" };
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(administrator);
        BaseRecordId<TypedIdOwner> owner = BaseRecordId<TypedIdOwner>.Create("owner");
        (await session.Collection(TypedIdOwner.Collection).CreateAsync(owner.Value, new TypedIdOwner { Name = "Owner" })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdOwner>>>();
        (await session.Collection(hiddenDocument).CreateAsync(new RecordId("hidden-document"), new TypedIdDocument { OwnerId = owner })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdDocument>>>();
        (await session.Collection(TypedIdManyDocument.Collection).CreateAsync(new RecordId("root"), new TypedIdManyDocument { Members = [owner] })).Should().BeOfType<BaseSuccess<BaseRecord<TypedIdManyDocument>>>();

        PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "inverse-user" };
        OperationResult<RecordPage> result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            TypedIdManyDocument.Collection.Id,
            new RecordQuery
            {
                Include = [new RecordInclude
                {
                    NavigationId = "typed-id-many-document.members",
                    Includes = [new RecordInclude { NavigationId = "typed-id-owner.documents" }],
                }],
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 10 },
            }, principal,
            new OperationContext { Operation = BaseOperationKind.List, CollectionId = TypedIdManyDocument.Collection.Id });

        result.Error!.Code.Should().Be("base.include.invalid");
    }
}

internal sealed class CrossStoreIncludeInstaller(HostileIncludeStore root, FakeRecordStore target) : IHPDBaseBuilderExtension
{
    public string Id => "cross-store-include";
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

internal sealed class ReceiptMutationObserver : IBaseCommittedMutationObserver
{
    public int Count { get; private set; }
    public ValueTask ObserveAsync(BaseRecordMutationEvent mutation, CancellationToken cancellationToken = default)
    {
        Count++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class ToggleReceiptPolicyEvaluator : IPolicyEvaluator
{
    public bool Allow { get; set; } = true;
    public int DenyCalls { get; private set; }

    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Allow)
            return ValueTask.FromResult(PolicyDecision.Allow());
        DenyCalls++;
        return ValueTask.FromResult(PolicyDecision.Deny("base.policy.denied", "The operation is not permitted."));
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
