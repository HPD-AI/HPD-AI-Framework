using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using HPD.Base.Tests.Operations;

#pragma warning disable HPDBASE0461 // Manual parity and hostile graph cases intentionally cross the generated-only boundary.

namespace HPD.Base.Tests.Subjects;

public sealed class L45SubjectTransactionTests
{
    private sealed class UserSubject;

    [Fact]
    public async Task Capture_preserves_normal_duplicate_and_missing_record_outcomes_with_subject_contracts_installed()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        RecordCreateRequest create = Create("user-1", ("active", true), ("tenant", "tenant-a"));
        OperationResult<RecordEnvelope> initial = await runtime.CreateAsync(
            Private.Id,
            create,
            principal,
            Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(initial.IsSuccess(), initial.Error is null ? null : $"{initial.Error.Code}: {initial.Error.Message}");

        OperationResult<RecordEnvelope> duplicate = await runtime.CreateAsync(
            Private.Id,
            create,
            principal,
            Operation(BaseOperationKind.Create, Private.Id));
        OperationResult<RecordEnvelope> missingPatch = await runtime.PatchAsync(
            Private.Id,
            new RecordId("missing"),
            Patch(("active", false)),
            principal,
            Operation(BaseOperationKind.Patch, Private.Id));
        OperationResult<DeleteResult> missingDelete = await runtime.DeleteAsync(
            Private.Id,
            new RecordId("missing"),
            new RecordDeleteRequest(),
            principal,
            Operation(BaseOperationKind.Delete, Private.Id));

        Assert.Equal(OperationStatus.Conflict, duplicate.Status);
        Assert.NotEqual(BaseSubjectErrorCodes.ProviderContractInvalid, duplicate.Error?.Code);
        Assert.Equal(OperationStatus.NotFound, missingPatch.Status);
        Assert.Equal(OperationStatus.NotFound, missingDelete.Status);
    }

    [Fact]
    public async Task InMemory_validates_current_lifetime_and_rejects_stale_or_inactive_references()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();

        OperationResult<RecordEnvelope> privateCreate = await runtime.CreateAsync(
            Private.Id, Create("user-1", ("active", true), ("tenant", "tenant-a")), principal, Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(privateCreate.IsSuccess(), privateCreate.Error?.Code);
        JsonElement firstReference = await fixture.AcquireAsync("user-1");

        OperationResult<RecordEnvelope> accepted = await runtime.CreateAsync(
            Consumer.Id, Create("profile-1", ("owner", firstReference)), principal, Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.True(accepted.IsSuccess(), accepted.Error?.Code);

        OperationResult<RecordEnvelope> wrongTenant = await runtime.CreateAsync(
            Consumer.Id,
            Create("profile-wrong-tenant", ("owner", firstReference)),
            principal with { CurrentTenantId = "tenant-b" },
            Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.Equal(OperationStatus.ValidationFailed, wrongTenant.Status);
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, wrongTenant.Error?.Code);

        OperationResult<RecordEnvelope> deactivated = await runtime.PatchAsync(
            Private.Id, new RecordId("user-1"), Patch(("active", false)), principal, Operation(BaseOperationKind.Patch, Private.Id));
        Assert.True(deactivated.IsSuccess(), deactivated.Error?.Code);
        OperationResult<RecordEnvelope> inactive = await runtime.CreateAsync(
            Consumer.Id, Create("profile-2", ("owner", firstReference)), principal, Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.Equal(OperationStatus.ValidationFailed, inactive.Status);
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, inactive.Error?.Code);

        Assert.True((await runtime.DeleteAsync(Private.Id, new RecordId("user-1"), new RecordDeleteRequest(), principal,
            Operation(BaseOperationKind.Delete, Private.Id))).IsSuccess());
        Assert.True((await runtime.CreateAsync(Private.Id, Create("user-1", ("active", true), ("tenant", "tenant-a")), principal,
            Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        JsonElement secondReference = await fixture.AcquireAsync("user-1");
        Assert.NotEqual(firstReference.GetProperty("incarnation").GetString(), secondReference.GetProperty("incarnation").GetString());

        OperationResult<RecordEnvelope> stale = await runtime.CreateAsync(
            Consumer.Id, Create("profile-3", ("owner", firstReference)), principal, Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, stale.Error?.Code);
        Assert.True((await runtime.CreateAsync(Consumer.Id, Create("profile-4", ("owner", secondReference)), principal,
            Operation(BaseOperationKind.Create, Consumer.Id))).IsSuccess());
    }

    [Theory]
    [InlineData("deactivate", false)]
    [InlineData("deactivate", true)]
    [InlineData("rescope", false)]
    [InlineData("rescope", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    public async Task Mixed_atomic_batch_validates_against_final_subject_state_and_rolls_back_every_write(
        string lifecycle,
        bool lifecycleFirst)
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        Assert.True((await runtime.CreateAsync(Private.Id, Create("user-1", ("active", true), ("tenant", "tenant-a")), principal,
            Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        JsonElement reference = await fixture.AcquireAsync("user-1");

        var consumer = new BaseRecordBatchItem
        {
            ItemId = "consumer",
            CollectionId = Consumer.Id,
            Kind = BaseRecordMutationKind.Create,
            Create = Create("profile", ("owner", reference)),
        };
        BaseRecordBatchItem subject = lifecycle switch
        {
            "deactivate" => new BaseRecordBatchItem
            {
                ItemId = "deactivate", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Patch,
                RecordId = new RecordId("user-1"), Patch = Patch(("active", false)),
            },
            "rescope" => new BaseRecordBatchItem
            {
                ItemId = "rescope", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Patch,
                RecordId = new RecordId("user-1"), Patch = Patch(("tenant", "tenant-b")),
            },
            "delete" => new BaseRecordBatchItem
            {
                ItemId = "delete", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Delete,
                RecordId = new RecordId("user-1"), Delete = new RecordDeleteRequest(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(lifecycle)),
        };
        OperationResult<BaseRecordBatchResult> result = await runtime.BatchAsync(new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            Operations = lifecycleFirst ? [subject, consumer] : [consumer, subject],
        }, principal, Operation(BaseOperationKind.Batch, Consumer.Id));

        Assert.Equal(BaseRecordBatchOutcome.RolledBack, result.Value?.Outcome);
        Assert.Contains(result.Value!.Items, item => item.Error?.Code == BaseSubjectErrorCodes.ReferenceInvalid);
        Assert.Equal(OperationStatus.NotFound, (await runtime.GetAsync(Consumer.Id, new RecordId("profile"), principal, Operation(BaseOperationKind.Get, Consumer.Id))).Status);
        RecordEnvelope current = (await runtime.GetAsync(Private.Id, new RecordId("user-1"), principal, Operation(BaseOperationKind.Get, Private.Id))).Value!;
        Assert.True(current.Payload.Fields!["active"].GetBoolean());
        Assert.Equal("tenant-a", current.Payload.Fields["tenant"].GetString());
    }

    [Fact]
    public async Task Missing_exact_validation_grant_fails_before_store_resolution()
    {
        BaseGeneratedSubjectRegistration registration = BaseGeneratedSubjects.Register<UserSubject>(SubjectDefinition());
        FieldDefinition referenceField = Consumer.Fields!.Single() with
        {
            SubjectReference = Consumer.Fields!.Single().SubjectReference! with { ContractChecksum = registration.Checksum },
        };
        using ServiceProvider services = OperationTestServices.Build(
            fields: [referenceField],
            configureServices: registrations =>
            {
                registrations.AddSingleton(new BaseSubjectContractRegistry([registration]));
                registrations.AddSingleton<IBasePolicyOrchestrator>(new MissingGrantPolicy());
                registrations.AddSingleton<IBaseStoreExecutionResolver>(new ThrowingResolver());
            });
        JsonElement reference = JsonSerializer.Deserialize<JsonElement>(
            "{\"subjectId\":\"user-1\",\"authorityEpoch\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"incarnation\":\"BBBBBBBBBBBBBBBBBBBBBA\"}");

        OperationResult<RecordEnvelope> result = await services.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items", Create("consumer", ("owner", reference)), Principal(), Operation(BaseOperationKind.Create, "items"));

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, result.Error?.Code);
    }

    [Fact]
    public async Task Mixed_delete_recreate_uses_the_new_final_incarnation_and_rolls_back_to_the_old_lifetime()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        Assert.True((await runtime.CreateAsync(
            Private.Id,
            Create("user-1", ("active", true), ("tenant", "tenant-a")),
            principal,
            Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        JsonElement oldReference = await fixture.AcquireAsync("user-1");

        OperationResult<BaseRecordBatchResult> result = await runtime.BatchAsync(
            new BaseRecordBatchRequest
            {
                Mode = BaseRecordBatchExecutionMode.Atomic,
                Operations =
                [
                    new BaseRecordBatchItem
                    {
                        ItemId = "consumer", CollectionId = Consumer.Id, Kind = BaseRecordMutationKind.Create,
                        Create = Create("profile", ("owner", oldReference)),
                    },
                    new BaseRecordBatchItem
                    {
                        ItemId = "retire", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Delete,
                        RecordId = new RecordId("user-1"), Delete = new RecordDeleteRequest(),
                    },
                    new BaseRecordBatchItem
                    {
                        ItemId = "recreate", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Create,
                        Create = Create("user-1", ("active", true), ("tenant", "tenant-a")),
                    },
                ],
            },
            principal,
            Operation(BaseOperationKind.Batch, Consumer.Id));

        Assert.Equal(BaseRecordBatchOutcome.RolledBack, result.Value?.Outcome);
        Assert.Contains(result.Value!.Items, item => item.Error?.Code == BaseSubjectErrorCodes.ReferenceInvalid);
        JsonElement stillCurrent = await fixture.AcquireAsync("user-1");
        Assert.Equal(
            oldReference.GetProperty("incarnation").GetString(),
            stillCurrent.GetProperty("incarnation").GetString());
        Assert.Equal(OperationStatus.NotFound, (await runtime.GetAsync(
            Consumer.Id,
            new RecordId("profile"),
            principal,
            Operation(BaseOperationKind.Get, Consumer.Id))).Status);
    }

    [Fact]
    public async Task InMemory_rotation_rewrites_current_references_and_invalidates_the_old_epoch()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        Assert.True((await runtime.CreateAsync(
            Private.Id,
            Create("user-1", ("active", true), ("tenant", "tenant-a")),
            principal,
            Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        JsonElement oldReference = await fixture.AcquireAsync("user-1");
        Assert.True((await runtime.CreateAsync(
            Consumer.Id,
            Create("profile-1", ("owner", oldReference)),
            principal,
            Operation(BaseOperationKind.Create, Consumer.Id))).IsSuccess());

        OperationResult<BaseSubjectEpochRotationResult> rotation = await fixture.Store.RotateEpochAsync(
            new BaseSubjectEpochRotationRequest
            {
                ContractId = "example.user",
                ContractVersion = 1,
                ExpectedStateGeneration = 1,
                DestructiveIntent = "rotate-subject-authority-epoch",
            });

        Assert.True(rotation.IsSuccess(), rotation.Error?.Code);
        Assert.Equal(1, rotation.Value!.RewrittenReferences);
        RecordEnvelope rewritten = (await runtime.GetAsync(
            Consumer.Id,
            new RecordId("profile-1"),
            principal,
            Operation(BaseOperationKind.Get, Consumer.Id))).Value!;
        Assert.NotEqual(
            oldReference.GetProperty("authorityEpoch").GetString(),
            rewritten.Payload.Fields!["owner"].GetProperty("authorityEpoch").GetString());
        Assert.Equal(
            oldReference.GetProperty("incarnation").GetString(),
            rewritten.Payload.Fields["owner"].GetProperty("incarnation").GetString());
        OperationResult<RecordEnvelope> stale = await runtime.CreateAsync(
            Consumer.Id,
            Create("profile-2", ("owner", oldReference)),
            principal,
            Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, stale.Error?.Code);
        BaseMutationJournalPage journal = await fixture.Store.ReadMutationJournalAsync(
            new BaseMutationJournalReadRequest { Limit = 16 });
        Assert.Equal(BaseSubjectAuthorityPublicationKind.EpochRotation,
            journal.Entries[^1].SubjectAuthorityPublication?.Kind);
    }

    [Fact]
    public async Task Identified_duplicate_replays_the_stored_result_without_revalidating_a_retired_subject()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        Assert.True((await runtime.CreateAsync(
            Private.Id,
            Create("user-1", ("active", true), ("tenant", "tenant-a")),
            principal,
            Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        JsonElement reference = await fixture.AcquireAsync("user-1");
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "subject-tests",
            "identified-reference",
            "request-1",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("subject-receipt"u8)));
        var request = new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            RequestIdentity = identity,
            Operations =
            [
                new BaseRecordBatchItem
                {
                    ItemId = "consumer",
                    CollectionId = Consumer.Id,
                    Kind = BaseRecordMutationKind.Create,
                    Create = Create("profile-identified", ("owner", reference)),
                },
            ],
        };

        OperationResult<BaseRecordBatchResult> committed = await runtime.BatchAsync(
            request,
            principal,
            Operation(BaseOperationKind.Batch, Consumer.Id));
        Assert.Equal(BaseMutationRequestDisposition.Committed, committed.Value?.RequestDisposition);
        Assert.True((await runtime.PatchAsync(
            Private.Id,
            new RecordId("user-1"),
            Patch(("active", false)),
            principal,
            Operation(BaseOperationKind.Patch, Private.Id))).IsSuccess());

        OperationResult<BaseRecordBatchResult> duplicate = await runtime.BatchAsync(
            request,
            principal,
            Operation(BaseOperationKind.Batch, Consumer.Id));

        Assert.Equal(BaseMutationRequestDisposition.Duplicate, duplicate.Value?.RequestDisposition);
        Assert.Equal(committed.Value?.Items[0].Revision, duplicate.Value?.Items[0].Revision);
    }

    [Fact]
    public async Task Generated_graph_executes_subject_lifecycle_and_validation_through_SQLite()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l45-{Guid.NewGuid():N}.db");
        try
        {
            BaseCollection<L45SqlitePrivateUser> privateCollection = L45SqlitePrivateUser.Collection;
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .AddTestPolicyAuthority<GrantingPolicy>()
                .AddTestStaticGrant("system.private")
                .AddTestStaticGrant("example.user.validate")
                .AddTestStaticGrant("example.user.acquire")
                .ConfigureSchema(options =>
                {
                    options.ApplicationId = "l45.sqlite.application";
                    options.PlanProtectionKey = Enumerable.Repeat((byte)0x45, 32).ToArray();
                })
                .AddCollection(privateCollection)
                .AddCollection(L45SqliteProfile.Collection)
                .AddExportedSubject(L45SqliteUserSubject.HPDBaseSubjectRegistration)
                .AddRead(L45AcquireSqliteUser.Definition)
                .AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
                {
                    Id = "example.sqlite-user.acquire.v1",
                    Version = 1,
                    ContractId = "example.sqlite-user",
                    ContractVersion = 1,
                    RegisteredReadId = "example.sqlite-user.acquire",
                    RequiredGrantId = "example.user.acquire",
                    Audience = HPDBaseEndpointAudience.Application,
                    MaximumResults = 1,
                })
                .UseStore(SqliteStore.Configure(options =>
                {
                    options.StoreId = "l45-sqlite";
                    options.DataSource = database;
                })));

            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "l45-sqlite" });
            Assert.True(planned.IsSuccess(), planned.Error?.Code);
            OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
                new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact });
            Assert.True(applied.IsSuccess(), applied.Error?.Code);
            OperationResult<BaseApplicationReadiness> readiness = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
            Assert.True(readiness.IsSuccess(), readiness.Error?.Code);
            var publicationStore = (IBaseSubjectPublicationStore)provider.GetRequiredService<IRecordStoreRegistry>()
                .GetStoreForCollection(privateCollection.Id)!;
            BaseSubjectCurrentPublicationState publication = Assert.Single(
                (await publicationStore.ReadCurrentSubjectPublicationsAsync()).Value!);
            Assert.Equal(L45SqliteUserSubject.HPDBaseSubjectRegistration.Checksum, publication.ContractChecksum);

            IBaseRecordRuntime runtime = provider.GetRequiredService<IBaseRecordRuntime>();
            PrincipalContext principal = Principal();
            OperationResult<RecordEnvelope> createdSubject = await runtime.CreateAsync(privateCollection.Id,
                Create("user-1", ("active", true), ("tenant", "tenant-a")), principal,
                Operation(BaseOperationKind.Create, privateCollection.Id));
            Assert.True(createdSubject.IsSuccess(), createdSubject.Error?.Code);

            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
            BaseResult<L45AcquireSqliteUser.Row[]> acquired = await session.Reads.ToArrayAsync(
                L45AcquireSqliteUser.Handle,
                new L45AcquireSqliteUser { UserId = BaseRecordId<L45SqlitePrivateUser>.Create("user-1") });
            L45AcquireSqliteUser.Row[] rows = acquired.RequireValue();
            BaseSubjectReference<L45SqliteUserSubject> typedReference = Assert.Single(rows).Reference;
            JsonElement reference = JsonSerializer.SerializeToElement(typedReference);
            OperationResult<RecordEnvelope> accepted = await runtime.CreateAsync(L45SqliteProfile.Collection.Id,
                Create("profile-1", ("owner", reference)), principal,
                Operation(BaseOperationKind.Create, L45SqliteProfile.Collection.Id));
            Assert.True(accepted.IsSuccess(), accepted.Error?.Code);

            Assert.True((await runtime.PatchAsync(privateCollection.Id, new RecordId("user-1"), Patch(("active", false)),
                principal, Operation(BaseOperationKind.Patch, privateCollection.Id))).IsSuccess());
            OperationResult<RecordEnvelope> rejected = await runtime.CreateAsync(L45SqliteProfile.Collection.Id,
                Create("profile-2", ("owner", reference)), principal,
                Operation(BaseOperationKind.Create, L45SqliteProfile.Collection.Id));
            Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, rejected.Error?.Code);
            Assert.Equal(OperationStatus.NotFound, (await runtime.GetAsync(L45SqliteProfile.Collection.Id,
                new RecordId("profile-2"), principal, Operation(BaseOperationKind.Get, L45SqliteProfile.Collection.Id))).Status);

            Assert.True((await runtime.PatchAsync(
                privateCollection.Id,
                new RecordId("user-1"),
                Patch(("active", true)),
                principal,
                Operation(BaseOperationKind.Patch, privateCollection.Id))).IsSuccess());
            OperationResult<BaseRecordBatchResult> recreate = await runtime.BatchAsync(
                new BaseRecordBatchRequest
                {
                    Mode = BaseRecordBatchExecutionMode.Atomic,
                    Operations =
                    [
                        new BaseRecordBatchItem
                        {
                            ItemId = "profile-recreate", CollectionId = L45SqliteProfile.Collection.Id,
                            Kind = BaseRecordMutationKind.Create,
                            Create = Create("profile-3", ("owner", reference)),
                        },
                        new BaseRecordBatchItem
                        {
                            ItemId = "retire", CollectionId = privateCollection.Id,
                            Kind = BaseRecordMutationKind.Delete, RecordId = new RecordId("user-1"),
                            Delete = new RecordDeleteRequest(),
                        },
                        new BaseRecordBatchItem
                        {
                            ItemId = "recreate", CollectionId = privateCollection.Id,
                            Kind = BaseRecordMutationKind.Create,
                            Create = Create("user-1", ("active", true), ("tenant", "tenant-a")),
                        },
                    ],
                },
                principal,
                Operation(BaseOperationKind.Batch, L45SqliteProfile.Collection.Id));
            Assert.Equal(BaseRecordBatchOutcome.RolledBack, recreate.Value?.Outcome);
            Assert.Contains(recreate.Value!.Items, item => item.Error?.Code == BaseSubjectErrorCodes.ReferenceInvalid);
            Assert.Equal(OperationStatus.NotFound, (await runtime.GetAsync(
                L45SqliteProfile.Collection.Id,
                new RecordId("profile-3"),
                principal,
                Operation(BaseOperationKind.Get, L45SqliteProfile.Collection.Id))).Status);
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(database + "-wal")) File.Delete(database + "-wal");
            if (File.Exists(database + "-shm")) File.Delete(database + "-shm");
        }
    }

    private static SubjectFixture Build()
    {
        BaseGeneratedSubjectRegistration registration = BaseGeneratedSubjects.Register<UserSubject>(SubjectDefinition());
        BaseExportedSubjectDefinition subject = registration.Definition;
        CollectionDefinition[] collections = [Private, Consumer with
        {
            Fields = Consumer.Fields!.Select(field => field.SubjectReference is null ? field : field with
            {
                SubjectReference = field.SubjectReference with { ContractChecksum = registration.Checksum },
            }).ToArray(),
        }];
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionsContributor(collections));
        services.AddTestPolicyAuthority(new GrantingPolicy(), "system.private", "example.user.validate", "example.user.acquire");
        services.AddSingleton(new BaseSubjectContractRegistry([registration]));
        services.AddHPDBaseRuntime();
        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "subject-transaction",
            Collections = collections,
            CollectionIds = collections.Select(static value => value.Id).ToArray(),
            ExportedSubjects = [subject],
        });
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = store.Capabilities.StoreId,
            Store = store,
            CollectionIds = collections.Select(static value => value.Id).ToArray(),
        });
        return new SubjectFixture(provider, store, subject);
    }

    private static readonly CollectionDefinition Private = new()
    {
        Id = "private.users", Name = "private.users", Kind = BaseCollectionKinds.Document,
        System = true, Exposed = false, SystemOwnerModuleId = "example.auth",
        SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields =
        [
            new FieldDefinition { Id = "user.active", ApplicationName = "active", WireName = "active", Type = BaseFieldTypes.Boolean, Required = true, Nullable = false },
            new FieldDefinition { Id = "user.tenant", ApplicationName = "tenant", WireName = "tenant", Type = BaseFieldTypes.String, Required = true, Nullable = false },
        ],
    };

    private static readonly CollectionDefinition Consumer = new()
    {
        Id = "profiles", Name = "profiles", Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields =
        [
            new FieldDefinition
            {
                Id = "profile.owner", ApplicationName = "owner", WireName = "owner", Type = BaseFieldTypes.Object, Required = true, Nullable = false,
                SubjectReference = new BaseSubjectReferenceDefinition
                {
                    ContractId = "example.user", ContractVersion = 1, ContractChecksum = new string('0', 64),
                    Requirement = BaseSubjectReferenceRequirement.Active,
                    Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot,
                },
            },
        ],
    };

    private static BaseExportedSubjectDefinition SubjectDefinition() => new()
    {
        Id = "example.user", Version = 1, OwningModuleId = "example.auth",
        SubjectIdKind = BaseSubjectIdKind.OrdinalString, MaximumSubjectIdUtf8Bytes = 64,
        Scope = BaseSubjectScopeKind.Tenant, AcquisitionGrantId = "example.user.acquire",
        ValidationGrantId = "example.user.validate", AdministrationGrantId = "example.user.admin", Audiences = [HPDBaseEndpointAudience.Application],
        ValidationPlan = new BaseSubjectValidationPlanDefinition
        {
            Id = "example.user.validate.v1", Version = 1, ContractId = "example.user", ContractVersion = 1,
            ContractChecksum = new string('0', 64), PrivateCollectionId = Private.Id,
            SubjectId = BaseSubjectIdBinding.RecordId,
            Active = new BaseSubjectActiveBinding { Kind = BaseSubjectActiveBindingKind.RequiredBooleanField, FieldId = "user.active", ActiveValue = true },
            Scope = new BaseSubjectScopeBinding { Kind = BaseSubjectScopeBindingKind.RequiredTenantField, FieldId = "user.tenant" },
            Access = BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,
            Limits = BaseSubjectValidationLimits.Default,
        },
    };

    private static RecordCreateRequest Create(string id, params (string Name, object Value)[] fields) => new()
    {
        RequestedId = new RecordId(id), Payload = Payload(fields),
    };

    private static RecordPatchRequest Patch(params (string Name, object Value)[] fields) => new() { Patch = Payload(fields) };

    private static async ValueTask<JsonElement> AcquireAsync(
        IRelationalReadStore store,
        BaseExportedSubjectDefinition subject,
        string privateCollectionId,
        string id)
    {
        OperationResult<BaseRelationalReadExecutionResult> result = await store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
        {
            Plan = new BaseRelationalReadPlan
            {
                Id = "test.acquire", SchemaGeneration = 1,
                Sources = [new BaseRelationalReadSource { Id = "subjects", CollectionId = privateCollectionId }],
                Predicate = new BaseRelationalPredicate
                {
                    Kind = FilterNodeKind.Compare, Operator = FilterOperator.Equal,
                    Left = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = "subjects", FieldId = "base.recordId" },
                    Right = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Literal, Literal = BaseQueryValue.From(id) },
                },
                Projection = [new BaseRelationalReadProjection
                {
                    FieldId = "reference",
                    Operand = new BaseRelationalOperand
                    {
                        Kind = BaseRelationalOperandKind.SubjectReference, SourceId = "subjects",
                        SubjectContractId = subject.Id, SubjectContractVersion = subject.Version,
                    },
                }],
                Parameters = [],
                Budgets = new BaseRelationalReadBudgets { MaxResultRows = 1, MaxResultBytes = 4096, MaxOperations = 16 },
            },
            ParameterValues = [],
            SourcePolicies = [new BaseRelationalReadSourcePolicy { SourceId = "subjects", CollectionId = privateCollectionId }],
            Operation = Operation(BaseOperationKind.SubjectAcquire, privateCollectionId),
            AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1),
            MaxResultRows = 1, MaxResultBytes = 4096,
        });
        Assert.True(result.IsSuccess(), result.Error?.Code);
        QueryValue value = Assert.Single(Assert.Single(result.Value!.Result.Rows).Fields).Value;
        return JsonSerializer.Deserialize<JsonElement>(
            $$"""{"subjectId":"{{value.SubjectId}}","authorityEpoch":"{{value.SubjectAuthorityEpoch}}","incarnation":"{{value.SubjectIncarnation}}"}""");
    }

    private static RecordPayload Payload(params (string Name, object Value)[] fields) => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = fields.ToDictionary(static value => value.Name, static value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal),
    };

    private static PrincipalContext Principal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Service,
        SubjectKind = AccessSubjectKind.ServicePrincipal,
        SubjectId = "service-1",
        CurrentTenantId = "tenant-a",
    };

    private static OperationContext Operation(BaseOperationKind kind, string collectionId) => new()
    {
        ApplicationId = "test.application", Operation = kind, CollectionId = collectionId,
        Audience = HPDBaseEndpointAudience.Application, Mode = OperationMode.System,
    };

    private sealed class CollectionsContributor(CollectionDefinition[] collections) : IBaseDescriptorContributor
    {
        public string Id => "l45.collections";
        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            foreach (CollectionDefinition collection in collections) builder.AddCollection(collection);
        }
    }

    private sealed class GrantingPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PolicyDecision.Allow());
        }
    }

    private sealed class MissingGrantPolicy : IBasePolicyOrchestrator
    {
        public ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateReadAsync(BasePolicyRequest request, CancellationToken cancellationToken = default) =>
            Allow(cancellationToken);
        public ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateWriteAsync(BasePolicyRequest request, CancellationToken cancellationToken = default) =>
            Allow(cancellationToken);
        private static ValueTask<OperationResult<BasePolicyEvaluation>> Allow(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(OperationResults.Ok(new BasePolicyEvaluation
            {
                Decision = PolicyDecision.Allow() with { Audit = new PolicyAuditInfo { MatchedGrantIds = ["different.grant"] } },
            }));
        }
    }

    private sealed class ThrowingResolver : IBaseStoreExecutionResolver
    {
        public OperationResult<BaseResolvedMutationStore> Resolve(CollectionDefinition collection, BaseRecordMutationKind operation, OperationContext context) =>
            throw new InvalidOperationException("Provider resolution occurred before subject authorization.");
    }

    private sealed class SubjectFixture(ServiceProvider services, InMemoryRecordStore store, BaseExportedSubjectDefinition subject) : IAsyncDisposable
    {
        internal ServiceProvider Services { get; } = services;
        internal InMemoryRecordStore Store { get; } = store;
        internal async ValueTask<JsonElement> AcquireAsync(string id)
        {
            var operand = new BaseRelationalOperand
            {
                Kind = BaseRelationalOperandKind.SubjectReference, SourceId = "subjects",
                SubjectContractId = subject.Id, SubjectContractVersion = subject.Version,
            };
            OperationResult<BaseRelationalReadExecutionResult> result = await Store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
            {
                Plan = new BaseRelationalReadPlan
                {
                    Id = "test.acquire", SchemaGeneration = 1,
                    Sources = [new BaseRelationalReadSource { Id = "subjects", CollectionId = Private.Id }],
                    Predicate = new BaseRelationalPredicate
                    {
                        Kind = FilterNodeKind.Compare, Operator = FilterOperator.Equal,
                        Left = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = "subjects", FieldId = "base.recordId" },
                        Right = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Literal, Literal = BaseQueryValue.From(id) },
                    },
                    Projection = [new BaseRelationalReadProjection { FieldId = "reference", Operand = operand }],
                    Parameters = [],
                    Budgets = new BaseRelationalReadBudgets { MaxResultRows = 1, MaxResultBytes = 4096, MaxOperations = 16 },
                },
                ParameterValues = [], SourcePolicies = [new BaseRelationalReadSourcePolicy { SourceId = "subjects", CollectionId = Private.Id }],
                Operation = Operation(BaseOperationKind.SubjectAcquire, Private.Id),
                AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1), MaxResultRows = 1, MaxResultBytes = 4096,
            });
            Assert.True(result.IsSuccess(), result.Error?.Code);
            QueryValue value = Assert.Single(Assert.Single(result.Value!.Result.Rows).Fields).Value;
            return JsonSerializer.Deserialize<JsonElement>($$"""{"subjectId":"{{value.SubjectId}}","authorityEpoch":"{{value.SubjectAuthorityEpoch}}","incarnation":"{{value.SubjectIncarnation}}"}""");
        }
        public ValueTask DisposeAsync()
        {
            Services.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

[BaseCollection("l45.private-users", typeof(L45SqliteJsonContext), SystemOwnerModuleId = "example.auth")]
internal sealed partial record L45SqlitePrivateUser
{
    [BaseField("user.active")]
    public required bool Active { get; init; }

    [BaseField("user.tenant")]
    public required string Tenant { get; init; }
}

[BaseExportedSubject("example.sqlite-user", OwningModuleId = "example.auth",
    PrivateRecordType = typeof(L45SqlitePrivateUser), AcquisitionGrantId = "example.user.acquire",
    ValidationGrantId = "example.user.validate", AdministrationGrantId = "example.user.admin", ValidationPlanId = "example.sqlite-user.validate.v1",
    Scope = BaseSubjectScopeKind.Tenant, ActiveFieldId = "user.active", ScopeFieldId = "user.tenant")]
internal sealed partial class L45SqliteUserSubject;

[BaseCollection("l45.profiles", typeof(L45SqliteJsonContext))]
internal sealed partial record L45SqliteProfile
{
    [BaseField("profile.owner")]
    [BaseSubjectReference(typeof(L45SqliteUserSubject), Requirement = BaseSubjectReferenceRequirement.Active)]
    public required BaseSubjectReference<L45SqliteUserSubject> Owner { get; init; }
}

[BaseRead("example.sqlite-user.acquire", typeof(L45SqliteJsonContext),
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    RequiredGrantId = "example.user.acquire",
    SystemSourceIds = ["l45.private-users"])]
internal sealed partial record L45AcquireSqliteUser
{
    [BaseReadParameter("example.sqlite-user.acquire.user-id")]
    public required BaseRecordId<L45SqlitePrivateUser> UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("example.sqlite-user.acquire.reference")]
        public required BaseSubjectReference<L45SqliteUserSubject> Reference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<L45AcquireSqliteUser, Row> read)
    {
        read.From(L45SqlitePrivateUser.Collection, "users", out BaseReadSource<L45SqlitePrivateUser> user)
            .Where(user.RecordId.Equal(read.Parameter(Parameters.UserId)))
            .ProjectSubjectReference(Row.Fields.Reference, user, L45SqliteUserSubject.HPDBaseSubjectRegistration);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(L45SqlitePrivateUser))]
[JsonSerializable(typeof(L45SqliteProfile))]
[JsonSerializable(typeof(L45AcquireSqliteUser))]
[JsonSerializable(typeof(L45AcquireSqliteUser.Row), TypeInfoPropertyName = "L45AcquireSqliteUserRow")]
internal sealed partial class L45SqliteJsonContext : JsonSerializerContext;
