using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.Sqlite.AotSmoke;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

string temporaryDirectory = Path.GetFullPath(Path.GetTempPath());
if (OperatingSystem.IsMacOS() && temporaryDirectory.StartsWith("/var/", StringComparison.Ordinal))
    temporaryDirectory = "/private" + temporaryDirectory;
var dataSource = Path.Combine(temporaryDirectory, "hpd-base-sqlite-aot-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    BaseCollection<SmokeRecord> items = SmokeRecord.Collection;
    BaseCollection<SmokePrivateSubjectRecord> privateSubjects = SmokePrivateSubjectRecord.Collection;
    var lifecycleConsumer = new BaseSubjectLifecycleConsumerDefinition
    {
        Id = "hpd.base.sqlite.aot.subject.lifecycle", Version = 1,
        OwningModuleId = "hpd.base.sqlite.aot.consumer", Audience = BaseSubjectLifecycleConsumerAudience.Service,
        ContractId = "hpd.base.sqlite.aot.subject", ContractVersion = 1,
        ObservedStates = [BaseSubjectLifecycleState.Active, BaseSubjectLifecycleState.Inactive],
        DeliveryGrantId = "hpd.base.sqlite.aot.subject.lifecycle.read",
        Limits = new BaseSubjectLifecycleConsumerLimits
        {
            MaximumFactsPerPage = 64, MaximumResultBytes = 131_072,
            MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(5),
        },
    };
    BaseCollection<JsonElement>[] authorityCollections =
    [
        AuthorityCollection("authority.revisions", BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge),
        AuthorityCollection("authority.validations", BaseCollectionMutationMode.AppendOnly),
        AuthorityCollection("authority.audit", BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge),
        AuthorityCollection("authority.intents", BaseCollectionMutationMode.AppendOnly),
        AuthorityCollection("authority.desired", BaseCollectionMutationMode.Mutable),
        AuthorityCollection("authority.outbox", BaseCollectionMutationMode.AppendOnly),
    ];
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddHPDBase(builder =>
    {
        builder.ConfigureSchema(options =>
        {
            options.ApplicationId = "hpd.base.sqlite.aot";
            options.PlanProtectionKey = Enumerable.Repeat((byte)0x61, 32).ToArray();
        });
        builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
        {
            Id = 1,
            Key = Enumerable.Repeat((byte)0x62, 32).ToArray(),
            IssueNotBefore = DateTimeOffset.UnixEpoch,
        });
        builder.UseStore(SqliteStore.Configure(options =>
        {
            options.StoreId = "smoke.sqlite";
            options.DataSource = dataSource;
            options.AdministrationEnabled = true;
        }));
        builder.AddCollection(items);
        builder.AddCollection(privateSubjects);
        builder.AddCollection(SmokeSubjectConsumerRecord.Collection);
        builder.AddExportedSubject(SmokeSubject.HPDBaseSubjectRegistration);
        builder.AddSubjectLifecycleConsumer(lifecycleConsumer);
        builder.AddRead(SmokeAcquireSubject.Definition);
        builder.AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
        {
            Id = "hpd.base.sqlite.aot.subject.acquire.v1",
            Version = 1,
            ContractId = "hpd.base.sqlite.aot.subject",
            ContractVersion = 1,
            RegisteredReadId = "hpd.base.sqlite.aot.subject.acquire",
            RequiredGrantId = "hpd.base.sqlite.aot.subject.acquire",
            Audience = HPDBaseEndpointAudience.Application,
            MaximumResults = 1,
        });
        foreach (BaseCollection<JsonElement> collection in authorityCollections)
            builder.AddCollection(collection);
        builder.AddPolicyAuthority<SmokePolicyEvaluator>(new BasePolicyAuthorityDefinition
        {
            Id = "hpd.base.sqlite.aot.allow", Version = 1, OwningModuleId = "hpd.base.sqlite.aot",
            EvaluatorContractId = "hpd.base.sqlite.aot.policy", EvaluatorContractVersion = 1, CompositionOrder = 0,
        });
        foreach (string grantId in new[] { "hpd.base.sqlite.aot.subject.private", "hpd.base.sqlite.aot.subject.acquire", "hpd.base.sqlite.aot.subject.validate", "hpd.base.sqlite.aot.subject.rotate", "hpd.base.sqlite.aot.subject.lifecycle.read", "base.subjectLifecycle.feed.read", "base.subjectLifecycle.feed.checkpoint", "hpd.base.sqlite.aot.module.increment" })
            builder.AddStaticGrantAuthority(GrantDefinition(grantId, "hpd.base.sqlite.aot"), Grant(grantId, "sqlite-aot-service"));
        builder.AddModuleGenerationCell(ModuleMutationSmoke.Cell);
        builder.AddModuleMutation(ModuleMutationSmoke.Definition, ModuleMutationSmoke.Identity);
    });

    await using var provider = services.BuildServiceProvider();
    IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
    BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "smoke.sqlite" })).Value!;
    Require((await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess(), "Schema apply failed.");
    Require((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess(), "Application initialization failed.");

    var runtime = provider.GetRequiredService<IBaseRecordRuntime>();
    var principal = new PrincipalContext
    {
        AuthenticationState = PrincipalAuthenticationState.Service,
        SubjectKind = AccessSubjectKind.ServicePrincipal,
        SubjectId = "sqlite-aot-service",
        CurrentTenantId = "tenant-a",
    };
    var create = await runtime.CreateAsync("items", new RecordCreateRequest { Payload = Payload("hello") }, principal, Operation(BaseOperationKind.Create));
    Require(create.Status == OperationStatus.Created, "Create failed.");
    var journal = (ITransactionalMutationJournalStore)provider
        .GetRequiredService<IRecordStoreRegistry>()
        .GetStoreForCollection("items")!;
    var journalPage = await journal.ReadMutationJournalAsync(
        new BaseMutationJournalReadRequest { Limit = 10 });
    Require(
        journalPage.Entries.Count(static entry => entry.Kind == BaseMutationJournalEntryKind.RecordMutation) == 1
        && journalPage.Entries.Count(static entry => entry.Kind == BaseMutationJournalEntryKind.SubjectAuthorityPublication) == 1,
        "Shared record/control mutation journal append/read failed.");
    _ = JsonSerializer.Serialize(
        journalPage,
        HPD.Base.HPDBaseJsonSerializerContext.Default.BaseMutationJournalPage);

    var list = await runtime.ListAsync("items", new RecordQuery { Count = QueryCountMode.Exact }, principal, Operation(BaseOperationKind.List));
    Require(list.Status == OperationStatus.Ok && list.Value!.Count!.Total == 1, "List/count failed.");

    BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
    BaseMutationRequestIdentity moduleIdentity = BaseMutationRequestIdentity.Create(
        "aot", "module-increment", "module-request-1",
        BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("sqlite-aot-module-request"u8)));
    BaseInstalledModuleMutationHandle<ModuleMutationSmokeRequest, ModuleMutationSmokeResult> module =
        session.ModuleMutations.Get(ModuleMutationSmoke.Identity);
    BaseModuleMutationExecutionResult<ModuleMutationSmokeResult> moduleCommitted =
        (await module.ExecuteAsync(new ModuleMutationSmokeRequest { Marker = "aot" }, moduleIdentity)).RequireValue();
    BaseModuleMutationExecutionResult<ModuleMutationSmokeResult> moduleDuplicate =
        (await module.ExecuteAsync(new ModuleMutationSmokeRequest { Marker = "aot" }, moduleIdentity)).RequireValue();
    Require(moduleCommitted.Result.Generation == "1" && moduleCommitted.Disposition == BaseMutationRequestDisposition.Committed
        && moduleDuplicate.Result.Generation == "1" && moduleDuplicate.Disposition == BaseMutationRequestDisposition.Duplicate,
        "SQLite L50 generation commit or receipt replay failed.");
    BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
        "aot", "create-item", "request-1",
        BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-request"u8)));
    BaseBatchBuilder firstRequest = session.Atomic(identity);
    firstRequest.Create(items, new RecordId("receipt-item"), new SmokeRecord("receipt"));
    BaseBatchBuilder duplicateRequest = session.Atomic(identity);
    duplicateRequest.Create(items, new RecordId("receipt-item"), new SmokeRecord("receipt"));
    BaseResult<BaseBatchResult> committedResult = await firstRequest.CommitAsync();
    BaseResult<BaseBatchResult> duplicateResult = await duplicateRequest.CommitAsync();
    Require(committedResult is BaseSuccess<BaseBatchResult>,
        "Identified request failed: " + (committedResult as BaseFailure<BaseBatchResult>)?.Error.Code);
    Require(duplicateResult is BaseSuccess<BaseBatchResult>,
        "Duplicate request failed: " + (duplicateResult as BaseFailure<BaseBatchResult>)?.Error.Code);
    BaseSuccess<BaseBatchResult> committed = (BaseSuccess<BaseBatchResult>)committedResult;
    BaseSuccess<BaseBatchResult> duplicate = (BaseSuccess<BaseBatchResult>)duplicateResult;
    Require(committed.Value.RequestDisposition == BaseMutationRequestDisposition.Committed, "Identified request did not commit.");
    Require(duplicate.Value.RequestDisposition == BaseMutationRequestDisposition.Duplicate, "Identified request did not replay its receipt.");

    OperationResult<RecordEnvelope> createdSubject = await runtime.CreateAsync(
        privateSubjects.Id,
        new RecordCreateRequest
        {
            RequestedId = new RecordId("subject-1"),
            Payload = JsonObjectPayload(("active", "true"), ("tombstoned", "false"), ("tenant", "\"tenant-a\"")),
        },
        principal,
        Operation(BaseOperationKind.Create, privateSubjects.Id));
    Require(createdSubject.IsSuccess(), "Exported subject creation failed: " + createdSubject.Error?.Code);

    BaseResult<SmokeAcquireSubject.Row[]> acquiredSubject = await session.Reads.ToArrayAsync(
        SmokeAcquireSubject.Handle,
        new SmokeAcquireSubject { SubjectId = BaseRecordId<SmokePrivateSubjectRecord>.Create("subject-1") });
    BaseSubjectReference<SmokeSubject> subjectReference = acquiredSubject.RequireValue().Single().Reference;
    OperationResult<RecordEnvelope> acceptedReference = await runtime.CreateAsync(
        SmokeSubjectConsumerRecord.Collection.Id,
        new RecordCreateRequest
        {
            RequestedId = new RecordId("consumer-1"),
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = JsonSerializer.SerializeToElement(
                    new SmokeSubjectConsumerRecord { Subject = subjectReference },
                    SmokeSubjectJsonContext.Default.SmokeSubjectConsumerRecord),
            },
        },
        principal,
        Operation(BaseOperationKind.Create, SmokeSubjectConsumerRecord.Collection.Id));
    Require(acceptedReference.IsSuccess(), "Validated subject reference mutation failed: " + acceptedReference.Error?.Code);

    OperationResult<RecordEnvelope> deactivatedSubject = await runtime.PatchAsync(
        privateSubjects.Id,
        new RecordId("subject-1"),
        new RecordPatchRequest { Patch = FieldPatch("active", false) },
        principal,
        Operation(BaseOperationKind.Patch, privateSubjects.Id));
    Require(deactivatedSubject.IsSuccess(), "Subject deactivation failed: " + deactivatedSubject.Error?.Code);
    BaseGeneratedSubjectLifecycleConsumerIdentity<SmokeSubject> lifecycleIdentity =
        BaseGeneratedSubjectLifecycleConsumers.Register<SmokeSubject>(lifecycleConsumer, SmokeSubject.HPDBaseSubjectRegistration);
    BaseInstalledSubjectLifecycleConsumer<SmokeSubject> lifecycle = session.SubjectLifecycle.Get(lifecycleIdentity);
    var lifecycleDeliveries = new List<BaseSubjectLifecycleDelivery<SmokeSubject>>();
    await foreach (BaseSubjectLifecycleDelivery<SmokeSubject> delivery in lifecycle.ReadAsync(CancellationToken.None))
        lifecycleDeliveries.Add(delivery);
    Require(lifecycleDeliveries.Select(static delivery => delivery.Fact.Fact.Kind).ToArray()
            is [BaseSubjectLifecycleFactKind.Created, BaseSubjectLifecycleFactKind.Transitioned]
        && lifecycleDeliveries[^1].Fact.Fact.Transitioned?.CurrentState == BaseSubjectLifecycleState.Inactive,
        "SQLite Native AOT lifecycle delivery did not preserve canonical state ordering.");
    BaseSubjectLifecycleDelivery<SmokeSubject> lifecycleThrough = lifecycleDeliveries[^1];
    BaseSubjectLifecycleCheckpointResult lifecycleAdvanced =
        (await lifecycle.AdvanceAsync(lifecycleThrough.Checkpoint, lifecycleThrough.AdvanceIdentity)).RequireValue();
    Require(lifecycleAdvanced.CheckpointGeneration == 1 && !lifecycleAdvanced.Duplicate,
        "SQLite Native AOT lifecycle checkpoint did not advance exactly once.");
    OperationResult<RecordEnvelope> rejectedReference = await runtime.CreateAsync(
        SmokeSubjectConsumerRecord.Collection.Id,
        new RecordCreateRequest
        {
            RequestedId = new RecordId("consumer-2"),
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = JsonSerializer.SerializeToElement(
                    new SmokeSubjectConsumerRecord { Subject = subjectReference },
                    SmokeSubjectJsonContext.Default.SmokeSubjectConsumerRecord),
            },
        },
        principal,
        Operation(BaseOperationKind.Create, SmokeSubjectConsumerRecord.Collection.Id));
    Require(
        rejectedReference.Error?.Code == BaseSubjectErrorCodes.ReferenceInvalid,
        "Inactive subject reference was not rejected through the production SQLite transaction path.");

    IHPDBaseAdministration subjectAdministration = provider.GetRequiredService<IHPDBaseAdministration>();
    BaseResult<BaseSubjectEpochRotationResult> deniedRotation = await subjectAdministration
        .RotateSubjectEpochAsync(
            "smoke.sqlite",
            principal with { SubjectId = "sqlite-aot-no-rotate" },
            new BaseSubjectEpochRotationRequest
            {
                ContractId = "hpd.base.sqlite.aot.subject",
                ContractVersion = 1,
                ExpectedStateGeneration = 1,
                DestructiveIntent = "rotate-subject-authority-epoch",
            });
    Require(
        deniedRotation is BaseFailure<BaseSubjectEpochRotationResult> denied
        && denied.Status == OperationStatus.PolicyDenied
        && denied.Error.Code == BaseAdministrationErrorCodes.Unauthorized,
        "Subject rotation without the exact administration grant was not denied.");

    BaseResult<BaseSubjectEpochRotationResult> rotation = await subjectAdministration
        .RotateSubjectEpochAsync(
            "smoke.sqlite",
            principal,
            new BaseSubjectEpochRotationRequest
            {
                ContractId = "hpd.base.sqlite.aot.subject",
                ContractVersion = 1,
                ExpectedStateGeneration = 1,
                DestructiveIntent = "rotate-subject-authority-epoch",
            });
    BaseSubjectEpochRotationResult rotated = rotation.RequireValue();
    Require(
        rotated.PreviousStateGeneration == 1
        && rotated.PublishedStateGeneration == 2
        && rotated.RewrittenReferences == 1,
        "Exported subject authority rotation did not publish and rewrite through the exact administration grant.");

    OperationResult<RecordEnvelope> desired = await runtime.CreateAsync(
        "authority.desired",
        new RecordCreateRequest { RequestedId = new RecordId("desired"), Payload = JsonPayload("generation", "1") },
        principal,
        Operation(BaseOperationKind.Create, "authority.desired"));
    Require(desired.Status == OperationStatus.Created, "Authority desired-state seed failed.");
    BaseMutationRequestIdentity authorityIdentity = BaseMutationRequestIdentity.Create(
        "aot", "authority-accept", "authority-request-1",
        BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("authority-request"u8)));
    BaseRecordBatchRequest authorityRequest = AuthorityRequest(authorityIdentity, desired.Value!.Metadata.Revision!.Value);
    OperationResult<BaseRecordBatchResult> authorityCommit = await runtime.BatchAsync(
        authorityRequest,
        principal,
        Operation(BaseOperationKind.Batch, "authority.revisions"));
    OperationResult<BaseRecordBatchResult> authorityDuplicate = await runtime.BatchAsync(
        authorityRequest,
        principal,
        Operation(BaseOperationKind.Batch, "authority.revisions"));
    Require(
        authorityCommit.Value?.RequestDisposition == BaseMutationRequestDisposition.Committed
        && authorityCommit.Value.Items.Length == 6,
        "Complete authority graph did not commit atomically.");
    Require(
        authorityDuplicate.Value?.RequestDisposition == BaseMutationRequestDisposition.Duplicate,
        "Complete authority graph receipt did not replay.");

    var cursorQuery = new RecordQuery
    {
        Sort = [new QuerySort("item.title", QuerySortDirection.Asc)],
        Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 1 },
    };
    OperationResult<RecordPage> cursorPage = await runtime.ListAsync("items", cursorQuery, principal, Operation(BaseOperationKind.List));
    Require(cursorPage.Value?.Page.NextCursor is not null, "SQLite cursor continuation was not produced.");

    IHPDBaseApplication application = provider.GetRequiredService<IHPDBaseApplication>();
    var artifact = new MemoryStream();
    BaseBackupManifest manifest = (await application.Administration.CreateBackupAsync(
        artifact,
        new BaseBackupRequest { StoreId = "smoke.sqlite", Principal = principal })).RequireValue();
    Require(manifest.NativeSqliteVersion == "3.53.4", "Unexpected native SQLite dependency graph.");
    artifact.Position = 0;
    Require((await application.Administration.ValidateBackupAsync(
        artifact,
        new BaseBackupValidationRequest
        {
            StoreId = "smoke.sqlite",
            Principal = principal,
            ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
        })) is BaseSuccess<BaseBackupManifest>, "Backup validation failed.");
    artifact.Position = 0;
    BaseRestoreResult restored = (await application.Administration.RestoreAsync(
        artifact,
        new BaseRestoreRequest
        {
            StoreId = "smoke.sqlite",
            Principal = principal,
            ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
            ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
            IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
            RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
            ConfirmDestructiveReplacement = true,
        })).RequireValue();
    Require(restored.RestoreEpoch == manifest.RestoreEpoch + 1, "Restore epoch did not advance.");
    BaseBatchBuilder postRestoreRetry = session.Atomic(identity);
    postRestoreRetry.Create(items, new RecordId("receipt-item"), new SmokeRecord("receipt"));
    BaseResult<BaseBatchResult> restoredReceipt = await postRestoreRetry.CommitAsync();
    Require(
        restoredReceipt is BaseSuccess<BaseBatchResult> restoredDuplicate
        && restoredDuplicate.Value.RequestDisposition == BaseMutationRequestDisposition.Duplicate,
        "Restore did not preserve the durable atomic receipt.");

    var relational = provider.GetRequiredService<IRelationalMetadataProvider>();
    var descriptor = await relational.GetStoreAsync(Operation(BaseOperationKind.List), VisibilityLevel.Admin);
    Require(descriptor.Status == OperationStatus.Ok && descriptor.Value is not null, "Relational descriptor failed.");
    _ = JsonSerializer.Serialize(descriptor.Value, HPDBaseSqliteJsonSerializerContext.Default.RelationalStoreDescriptor);
    _ = JsonSerializer.Serialize(
        new HPD.Base.Sqlite.HPDBaseSqliteOptions
        {
            StoreId = "serialized",
            Collections = [new CollectionDefinition
            {
                Id = "items", Name = "items", Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve
            }]
        },
        HPDBaseSqliteJsonSerializerContext.Default.HPDBaseSqliteOptions);

    var delete = await runtime.DeleteAsync("items", create.Value!.Id, new RecordDeleteRequest { ExpectedRevision = create.Value.Metadata.Revision, ReturnPrevious = true }, principal, Operation(BaseOperationKind.Delete));
    Require(delete.Status == OperationStatus.Deleted && delete.Value!.Previous is not null, "Delete failed.");
    Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection fallback must be disabled.");
}
finally
{
    foreach (var candidate in new[] { dataSource, dataSource + "-wal", dataSource + "-shm" })
    {
        if (File.Exists(candidate)) File.Delete(candidate);
    }
}

static OperationContext Operation(BaseOperationKind kind, string collectionId = "items") => new() { Operation = kind, CollectionId = collectionId, Now = DateTimeOffset.UtcNow };
static BaseGrantAuthorityDefinition GrantDefinition(string id, string owner) => new()
{
    Id = id, Version = 1, OwningModuleId = owner,
    SourceContractId = owner + ".static-grant", SourceContractVersion = 1,
};
static AccessGrant Grant(string id, string subjectId) => new()
{
    Id = id, ApplicationId = "hpd.base.sqlite.aot", ModuleId = id == "hpd.base.sqlite.aot.module.increment" ? "hpd.base.sqlite.aot.module" : id.Contains("subjectLifecycle", StringComparison.Ordinal) || id.Contains("subject.lifecycle", StringComparison.Ordinal) ? "hpd.base.sqlite.aot.consumer" : "hpd.base.sqlite.aot",
    Audience = HPDBaseEndpointAudience.Application,
    Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = subjectId, TenantId = "tenant-a" },
    Action = id == "hpd.base.sqlite.aot.subject.lifecycle.read" ? "hpd.base.sqlite.aot.subject.lifecycle" : id,
    Scope = id.Contains("subjectLifecycle", StringComparison.Ordinal) || id.Contains("subject.lifecycle", StringComparison.Ordinal)
        ? new ResourceScope { Kind = ResourceScopeKind.SubjectContract, SubjectContractId = "hpd.base.sqlite.aot.subject", SubjectContractVersion = 1, TenantId = "tenant-a" }
        : new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-a" },
};

static BaseCollection<JsonElement> AuthorityCollection(string id, BaseCollectionMutationMode mode) =>
    BaseCollection<JsonElement>.Create(
        new CollectionDefinition
        {
            Id = id,
            Name = id,
            Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose,
            UnknownFields = UnknownFieldPolicy.Preserve,
            MutationMode = mode,
        },
        HPDBaseJsonSerializerContext.Default.JsonElement,
        static _ => { });

static BaseRecordBatchRequest AuthorityRequest(
    BaseMutationRequestIdentity identity,
    RevisionToken desiredRevision) => new()
    {
        Mode = BaseRecordBatchExecutionMode.Atomic,
        RequestIdentity = identity,
        Operations =
        [
            AuthorityCreate("revision", "authority.revisions", "revision-1", JsonPayload("kind", "\"accepted\"")),
            AuthorityCreate("validation", "authority.validations", "validation-1", JsonPayload("valid", "true")),
            AuthorityCreate("audit", "authority.audit", "audit-1", JsonPayload("action", "\"accepted\"")),
            AuthorityCreate("intent", "authority.intents", "intent-1", JsonPayload("state", "\"pending\"")),
            new BaseRecordBatchItem
            {
                ItemId = "desired",
                CollectionId = "authority.desired",
                Kind = BaseRecordMutationKind.Replace,
                RecordId = new RecordId("desired"),
                Replace = new RecordReplaceRequest
                {
                    ExpectedRevision = desiredRevision,
                    Payload = JsonPayload("generation", "2"),
                },
            },
            AuthorityCreate("outbox", "authority.outbox", "outbox-1", JsonPayload("delivery", "\"pending\"")),
        ],
    };

static BaseRecordBatchItem AuthorityCreate(
    string itemId,
    string collectionId,
    string recordId,
    RecordPayload payload) => new()
    {
        ItemId = itemId,
        CollectionId = collectionId,
        Kind = BaseRecordMutationKind.Create,
        Create = new RecordCreateRequest { RequestedId = new RecordId(recordId), Payload = payload },
    };

static RecordPayload JsonPayload(string name, string jsonValue)
{
    using JsonDocument document = JsonDocument.Parse($$"""{"{{name}}":{{jsonValue}}}""");
    return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
}

static RecordPayload JsonObjectPayload(params (string Name, string JsonValue)[] fields)
{
    string body = string.Join(',', fields.Select(static field => $"\"{field.Name}\":{field.JsonValue}"));
    using JsonDocument document = JsonDocument.Parse("{" + body + "}");
    return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
}

static RecordPayload FieldPatch(string name, bool value)
{
    using JsonDocument document = JsonDocument.Parse(value ? "true" : "false");
    return new RecordPayload
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [name] = document.RootElement.Clone(),
        },
    };
}

static RecordPayload Payload(string title)
{
    using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
    return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class SmokePolicyEvaluator : IPolicyEvaluator
{
    public SmokePolicyEvaluator() { }

    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool mayRotate = !string.Equals(request.Principal.SubjectId, "sqlite-aot-no-rotate", StringComparison.Ordinal);
        if (!mayRotate)
            return ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Deny, Outcome = PolicyOutcome.Denied });
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed,
            Audit = new PolicyAuditInfo
            {
                MatchedGrantIds = mayRotate
                    ?
                    [
                        "hpd.base.sqlite.aot.subject.private",
                        "hpd.base.sqlite.aot.subject.acquire",
                        "hpd.base.sqlite.aot.subject.validate",
                        "hpd.base.sqlite.aot.subject.rotate",
                    ]
                    :
                    [
                        "hpd.base.sqlite.aot.subject.private",
                        "hpd.base.sqlite.aot.subject.acquire",
                        "hpd.base.sqlite.aot.subject.validate",
                    ],
            },
        });
    }
}

[BaseCollection("items", typeof(SmokeJsonContext))]
internal sealed partial record SmokeRecord
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal SmokeRecord(string title) => Title = title;

    [BaseField("item.title")]
    public required string Title { get; init; }
}

[BaseCollection("subject.private", typeof(SmokeSubjectJsonContext),
    SystemOwnerModuleId = "hpd.base.sqlite.aot.subjects")]
internal sealed partial record SmokePrivateSubjectRecord
{
    [BaseField("subject.active")]
    public required bool Active { get; init; }

    [BaseField("subject.tombstoned")]
    public required bool Tombstoned { get; init; }

    [BaseField("subject.tenant")]
    public required string Tenant { get; init; }
}

[BaseExportedSubject("hpd.base.sqlite.aot.subject",
    OwningModuleId = "hpd.base.sqlite.aot.subjects",
    PrivateRecordType = typeof(SmokePrivateSubjectRecord),
    AcquisitionGrantId = "hpd.base.sqlite.aot.subject.acquire",
    ValidationGrantId = "hpd.base.sqlite.aot.subject.validate",
    AdministrationGrantId = "hpd.base.sqlite.aot.subject.rotate",
    ValidationPlanId = "hpd.base.sqlite.aot.subject.validate.v1",
    Scope = BaseSubjectScopeKind.Tenant,
    ActiveFieldId = "subject.active",
    TombstoneFieldId = "subject.tombstoned",
    ScopeFieldId = "subject.tenant")]
internal sealed partial class SmokeSubject;

[BaseCollection("subject.consumers", typeof(SmokeSubjectJsonContext))]
internal sealed partial record SmokeSubjectConsumerRecord
{
    [BaseField("consumer.subject")]
    [BaseSubjectReference(typeof(SmokeSubject), Requirement = BaseSubjectReferenceRequirement.Active)]
    public required BaseSubjectReference<SmokeSubject> Subject { get; init; }
}

[BaseRead("hpd.base.sqlite.aot.subject.acquire", typeof(SmokeSubjectJsonContext),
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    RequiredGrantId = "hpd.base.sqlite.aot.subject.acquire",
    SystemSourceIds = ["subject.private"])]
internal sealed partial record SmokeAcquireSubject
{
    [BaseReadParameter("hpd.base.sqlite.aot.subject.acquire.id")]
    public required BaseRecordId<SmokePrivateSubjectRecord> SubjectId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("hpd.base.sqlite.aot.subject.acquire.reference")]
        public required BaseSubjectReference<SmokeSubject> Reference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<SmokeAcquireSubject, Row> read)
    {
        read.From(SmokePrivateSubjectRecord.Collection, "subjects", out BaseReadSource<SmokePrivateSubjectRecord> subject)
            .Where(subject.RecordId.Equal(read.Parameter(Parameters.SubjectId)))
            .ProjectSubjectReference(Row.Fields.Reference, subject, SmokeSubject.HPDBaseSubjectRegistration);
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(SmokePrivateSubjectRecord))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SmokeSubjectConsumerRecord))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SmokeAcquireSubject))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SmokeAcquireSubject.Row), TypeInfoPropertyName = "SmokeAcquireSubjectRow")]
internal sealed partial class SmokeSubjectJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(SmokeRecord))]
internal sealed partial class SmokeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
