using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var dataSource = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-aot-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    BaseCollection<SmokeRecord> items = BaseCollection.Define(
        "items",
        SmokeJsonContext.Default.SmokeRecord,
        schema => schema.String("title", "Title"));
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
        });
        builder.UseSqlite(options =>
        {
            options.StoreId = "smoke.sqlite";
            options.DataSource = dataSource;
            options.AdministrationEnabled = true;
        });
        builder.AddCollection(items);
        foreach (BaseCollection<JsonElement> collection in authorityCollections)
            builder.AddCollection(collection);
        builder.ReplacePolicyEvaluator<SmokePolicyEvaluator>();
    });

    await using var provider = services.BuildServiceProvider();
    IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
    BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "smoke.sqlite" })).Value!;
    Require((await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess(), "Schema apply failed.");
    Require((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess(), "Application initialization failed.");

    var runtime = provider.GetRequiredService<IBaseRecordRuntime>();
    var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Anonymous };
    var create = await runtime.CreateAsync("items", new RecordCreateRequest { Payload = Payload("hello") }, principal, Operation(BaseOperationKind.Create));
    Require(create.Status == OperationStatus.Created, "Create failed.");
    var journal = (ITransactionalMutationJournalStore)provider
        .GetRequiredService<IRecordStoreRegistry>()
        .GetStoreForCollection("items")!;
    var journalPage = await journal.ReadMutationJournalAsync(
        new BaseMutationJournalReadRequest { Limit = 10 });
    Require(journalPage.Entries.Length == 1, "Mutation journal append/read failed.");
    _ = JsonSerializer.Serialize(
        journalPage,
        HPD.Base.HPDBaseJsonSerializerContext.Default.BaseMutationJournalPage);

    var list = await runtime.ListAsync("items", new RecordQuery { Count = QueryCountMode.Exact }, principal, Operation(BaseOperationKind.List));
    Require(list.Status == OperationStatus.Ok && list.Value!.Count!.Total == 1, "List/count failed.");

    BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
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
        Sort = [new QuerySort("title", QuerySortDirection.Asc)],
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

static RecordPayload Payload(string title)
{
    using var document = JsonDocument.Parse($$"""{"Title":"{{title}}"}""");
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
        return ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }
}

internal sealed record SmokeRecord(string Title);

[System.Text.Json.Serialization.JsonSerializable(typeof(SmokeRecord))]
internal sealed partial class SmokeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
