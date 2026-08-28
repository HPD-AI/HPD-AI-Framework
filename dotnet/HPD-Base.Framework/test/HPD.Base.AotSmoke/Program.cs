using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.AotSmoke;
using HPD.Base.Testing;
using Microsoft.Extensions.DependencyInjection;

var collection = AotProject.Collection;
BaseSemanticActivationCertificationReport semanticCertification =
    await BaseSemanticActivationProviderCertification.RunAsync(
        new BaseInMemorySemanticActivationCertificationFixtureFactory(), TimeSpan.FromSeconds(10));
if (!semanticCertification.Passed
    || !BaseSemanticActivationCertificationContract.ValidateReport(semanticCertification)
    || !semanticCertification.Cases.Any(static item =>
        string.Equals(item.Id, "different-parent-race", StringComparison.Ordinal)
        && item.Status == OperationStatus.Ok)
    || !semanticCertification.Cases.Any(static item =>
        string.Equals(item.Id, "terminal-retirement", StringComparison.Ordinal)
        && item.Status == OperationStatus.Ok))
    throw new InvalidOperationException("InMemory Native AOT semantic activation certification failed.");
var value = new AotProject
{
    OrganizationId = "org_aot",
    Name = "AOT",
    State = AotProjectState.Active,
};
byte[] enumWire = JsonSerializer.SerializeToUtf8Bytes(value, AotApplicationJsonContext.Default.AotProject);
var wireRoundTrip = JsonSerializer.Deserialize(enumWire, AotApplicationJsonContext.Default.AotProject);
if (!System.Text.Encoding.UTF8.GetString(enumWire).Contains("\"state\":\"active-wire\"", StringComparison.Ordinal) ||
    wireRoundTrip?.State != AotProjectState.Active)
    throw new InvalidOperationException("Generated closed enum authority failed under Native AOT.");
try
{
    _ = JsonSerializer.Deserialize("{\"organizationId\":\"org\",\"name\":\"bad\",\"state\":0}", AotApplicationJsonContext.Default.AotProject);
    throw new InvalidOperationException("Numeric closed enum input was admitted under Native AOT.");
}
catch (JsonException) { }
byte[] recoverySeed = System.Security.Cryptography.SHA256.HashData("hpd-base-aot-recovery-key"u8);
BaseScheduleRecoveryVerificationKey recoveryKey = BaseScheduleRecoveryManifestContract.CreateVerificationKeyFromPrivateSeed(
    "aot-recovery", 1, recoverySeed, 1, 10_000);
BaseScheduleRecoveryManifest recoveryManifest = BaseScheduleRecoveryManifestContract.Sign(new BaseScheduleRecoveryManifest
{
    ApplicationId = "hpd.base.application", LogicalStoreId = "inmemory",
    BackupArtifactId = "aot-artifact", BackupArtifactChecksum = System.Security.Cryptography.SHA256.HashData("artifact"u8).ToImmutableArray(),
    SourceStoreInstanceId = "inmemory", SourceRestoreEpoch = 0, Floors = [],
    IssuedAt = 100, ExpiresAt = 1_000, Nonce = System.Security.Cryptography.SHA256.HashData("nonce"u8).ToImmutableArray(),
    SigningKeyId = recoveryKey.Id, SigningKeyVersion = recoveryKey.Version, ManifestChecksum = [], Signature = [],
}, recoveryKey, recoverySeed);
if (!BaseScheduleRecoveryManifestContract.Validate(recoveryManifest, new BaseScheduleRecoveryManifestValidation
{
    ApplicationId = "hpd.base.application", LogicalStoreId = "inmemory", BackupArtifactId = "aot-artifact",
    BackupArtifactChecksum = System.Security.Cryptography.SHA256.HashData("artifact"u8).ToImmutableArray(),
    AcceptedNow = 500, ExpectedScheduleKeyDigests = [],
}, [recoveryKey])) throw new InvalidOperationException("Native AOT schedule recovery verification failed.");
System.Security.Cryptography.CryptographicOperations.ZeroMemory(recoverySeed);
var lifecycleConsumer = new BaseSubjectLifecycleConsumerDefinition
{
    Id = "hpd.base.aot.subject.lifecycle", Version = 1, OwningModuleId = "hpd.base.aot.consumer",
    Audience = BaseSubjectLifecycleConsumerAudience.Service,
    ContractId = "hpd.base.aot.subject", ContractVersion = 1,
    ObservedStates = [BaseSubjectLifecycleState.Active, BaseSubjectLifecycleState.Inactive],
    DeliveryGrantId = "hpd.base.aot.subject.lifecycle.read",
    Limits = new BaseSubjectLifecycleConsumerLimits
    {
        MaximumFactsPerPage = 64, MaximumResultBytes = 131_072,
        MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(5),
    },
};

if (AotProject.Fields.OrganizationId.Id != "organization-id" ||
    AotProject.Fields.OrganizationId.WireName != "organizationId" ||
    !AotProject.Fields.Name.Operators.HasFlag(BaseFieldOperator.Order))
{
    throw new InvalidOperationException(
        "Generated application contracts must survive Native AOT.");
}

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<TimeProvider>(TimeProvider.System);
services.AddHPDBase(hpd =>
{
    hpd.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
    {
        Id = 1, Key = System.Security.Cryptography.SHA256.HashData("hpd-base-aot-subject-token-key"u8),
        IssueNotBefore = DateTimeOffset.UnixEpoch,
    });
    hpd.AddPolicyAuthority(new BasePolicyAuthorityDefinition
    {
        Id = "hpd.base.aot.allow", Version = 1, OwningModuleId = "hpd.base.aot",
        EvaluatorContractId = "hpd.base.aot.policy", EvaluatorContractVersion = 1, CompositionOrder = 0,
    }, new AotAllowPolicyEvaluator());
    foreach (string grantId in new[] { "hpd.base.aot.subject.private", "hpd.base.aot.subject.acquire", "hpd.base.aot.subject.validate", "hpd.base.aot.subject.rotate", "hpd.base.aot.subject.lifecycle.read", "base.subjectLifecycle.feed.read", "base.subjectLifecycle.feed.checkpoint", "hpd.base.aot.module.increment", "hpd.base.aot.module.records.source", "hpd.base.aot.subject.verify", "hpd.base.aot.semantic.ensure-operation", "hpd.base.aot.semantic.retire-operation", SemanticActivationSmoke.EnsureGrant, SemanticActivationSmoke.RetireGrant, SemanticActivationSmoke.MaintainGrant }.Concat(ActivationSmoke.GrantIds))
        hpd.AddStaticGrantAuthority(GrantDefinition(grantId, "hpd.base.aot"), Grant(grantId, "aot"));
    hpd.AddActivation(ActivationSmoke.Registration);
    hpd.AddActivation(ActivationSmoke.MigrationTargetRegistration);
    hpd.AddActivationMigration(ActivationSmoke.Migration);
    hpd.AddModuleGenerationCell(ModuleMutationSmoke.Cell);
    hpd.AddModuleGenerationCell(ModuleMutationSmoke.HostileCell);
    hpd.AddModuleMutation(ModuleMutationSmoke.Definition, ModuleMutationSmoke.Identity);
    hpd.AddModuleMutation(SubjectModuleMutationSmoke.Definition, SubjectModuleMutationSmoke.Identity);
    hpd.AddModuleMutation(SemanticEnsureMutationSmoke.Definition, SemanticEnsureMutationSmoke.Identity);
    hpd.AddModuleMutation(SemanticRetirementMutationSmoke.Definition, SemanticRetirementMutationSmoke.Identity);
    hpd.AddSemanticActivation(SemanticActivationSmoke.Registration);
    hpd.SetSemanticActivationRestoreSelection(new BaseSemanticActivationRestoreSelection
    {
        LogicalStoreId = HPDBaseInMemoryDefaults.DefaultStoreId, EnabledRestoreMode = null, SelectionGeneration = 1,
        Identity = BaseMutationRequestIdentity.Create("aot", "semantic-restore-selection", "semantic-restore-selection-1",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-semantic-restore-selection"u8))),
        Checksum = [],
    });
    hpd.AddCollection(collection);
    hpd.AddCollection(AotModuleRecord.Collection);
    hpd.AddCollection(AotPrivateSubjectRecord.Collection);
    hpd.AddCollection(AotSubjectConsumerRecord.Collection);
    hpd.AddExportedSubject(AotSubject.HPDBaseSubjectRegistration);
    hpd.AddSubjectLifecycleConsumer(lifecycleConsumer);
    hpd.AddRead(AotAcquireSubject.Definition);
    hpd.AddRead(AotBinaryRead.Definition);
    hpd.AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
    {
        Id = "hpd.base.aot.subject.acquire.v1",
        Version = 1,
        ContractId = "hpd.base.aot.subject",
        ContractVersion = 1,
        RegisteredReadId = "hpd.base.aot.subject.acquire",
        RequiredGrantId = "hpd.base.aot.subject.acquire",
        Audience = HPDBaseEndpointAudience.Application,
        MaximumResults = 1,
    });
});
await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateOnBuild = true });
OperationResult<BaseApplicationReadiness> initialized = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
if (!initialized.IsSuccess())
    throw new InvalidOperationException("InMemory application initialization failed: " + initialized.Error?.Code);
var session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "aot",
    CurrentTenantId = "tenant-a",
});
BaseCollectionSession<AotProject> projects = session.Collection(collection);
BaseInstalledActivationHandle<ActivationSmokeInput, ActivationSmokeResult> activation =
    session.Activations.Get(ActivationSmoke.Registration.Identity);
OperationResult<BaseActivationEnqueueResult> activationEnqueue = await activation.EnqueueAsync(
    new ActivationSmokeInput { Value = "native-aot" },
    BaseMutationRequestIdentity.Create("aot", "activation-enqueue", "activation-request-1",
        BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-activation-request"u8))));
if (!activationEnqueue.IsSuccess() || activationEnqueue.Value is not BaseActivationEnqueueResult activationCreated)
    throw new InvalidOperationException("Native AOT durable activation enqueue failed: " + activationEnqueue.Error?.Code);
if (activationCreated.State != BaseActivationState.Pending)
    throw new InvalidOperationException("Native AOT durable activation enqueue failed.");
OperationResult<BaseActivationEnqueueResult> migrationEnqueue = await activation.EnqueueAsync(
    new ActivationSmokeInput { Value = "native-aot-migration" },
    BaseMutationRequestIdentity.Create("aot", "activation-enqueue", "activation-migration-source-1",
        BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-activation-migration-source"u8))));
if (!migrationEnqueue.IsSuccess() || migrationEnqueue.Value is not BaseActivationEnqueueResult migrationCreated)
    throw new InvalidOperationException("Native AOT migration source enqueue failed: " + migrationEnqueue.Error?.Code);
var migrationPrincipal = new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "aot",
    CurrentTenantId = "tenant-a",
};
BaseResult<BaseActivationMigrationResult> activationMigration = await provider
    .GetRequiredService<IHPDBaseAdministration>()
    .MigrateActivationAsync(new BaseActivationAdministrationMigrationRequest
    {
        StoreId = HPDBaseInMemoryDefaults.DefaultStoreId,
        Principal = migrationPrincipal,
        Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
        MigrationId = ActivationSmoke.Migration.Definition.Id,
        MigrationVersion = ActivationSmoke.Migration.Definition.Version,
        ActivationId = migrationCreated.ActivationId,
        ExpectedGeneration = 1,
        Identity = BaseMutationRequestIdentity.Create("aot", "activation-migrate", "activation-migrate-1",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-activation-migrate"u8))),
    });
if (activationMigration is not BaseSuccess<BaseActivationMigrationResult> migrationSuccess
    || migrationSuccess.Value.SourceActivationId != migrationCreated.ActivationId
    || migrationSuccess.Value.SourceGeneration != 2
    || migrationSuccess.Value.ReplacementGeneration != 1)
    throw new InvalidOperationException("Native AOT generated activation migration failed: "
        + (activationMigration as BaseFailure<BaseActivationMigrationResult>)?.Error.Code);
BaseInstalledActivationWorkerHandle<ActivationSmokeInput, ActivationSmokeResult> activationWorker =
    session.Activations.GetWorker(ActivationSmoke.Registration.Identity);
OperationResult<BaseActivationDueObservation> activationObserved = await activationWorker.ObserveDueAsync();
if (!activationObserved.IsSuccess() || activationObserved.Value?.Earliest is null)
    throw new InvalidOperationException("Native AOT durable activation observation failed: " + activationObserved.Error?.Code);
OperationResult<BaseActivationDelivery<ActivationSmokeInput>?> activationClaimed = await activationWorker.TryClaimAsync(
    activationObserved.Value.Token,
    BaseMutationRequestIdentity.Create("aot", "activation-claim", "activation-claim-1",
        BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-activation-claim"u8))));
if (!activationClaimed.IsSuccess() || activationClaimed.Value is not { } activationDelivery
    || activationDelivery.Input.Value != "native-aot")
    throw new InvalidOperationException("Native AOT durable activation claim failed: " + activationClaimed.Error?.Code);
OperationResult<BaseActivationTransitionResult> activationCompleted = await activationWorker.CompleteAsync(
    activationDelivery, new ActivationSmokeResult { Value = "native-aot-complete" },
    BaseMutationRequestIdentity.Create("aot", "activation-complete", "activation-complete-1",
        BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-activation-complete"u8))));
if (!activationCompleted.IsSuccess() || activationCompleted.Value?.State != BaseActivationState.Succeeded)
    throw new InvalidOperationException("Native AOT durable activation completion failed: " + activationCompleted.Error?.Code);

async ValueTask EnqueueSemanticParent(string value, string requestId)
{
    OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
        new ActivationSmokeInput { Value = value },
        BaseMutationRequestIdentity.Create("aot-semantic-parent", "enqueue", requestId,
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(requestId)))));
    if (!enqueued.IsSuccess()) throw new InvalidOperationException("Native AOT semantic parent enqueue failed: " + enqueued.Error?.Code);
}
await EnqueueSemanticParent("ensure:logical-subject", "ensure-parent-1");
await EnqueueSemanticParent("ensure:logical-subject", "ensure-parent-2");
foreach (string phase in new[] { "first ensure parent", "second ensure parent", "semantic child" })
{
    OperationResult<BaseActivationDispatchResult> dispatched = await activationWorker.RunOneAsync();
    if (!dispatched.IsSuccess() || dispatched.Value is not { Empty: false, State: BaseActivationState.Succeeded })
        throw new InvalidOperationException($"InMemory Native AOT {phase} failed: {dispatched.Error?.Code}; state={dispatched.Value?.State}; empty={dispatched.Value?.Empty}");
}
await EnqueueSemanticParent("retire:logical-subject", "retire-parent-1");
OperationResult<BaseActivationDispatchResult> retiredSemantic = await activationWorker.RunOneAsync();
if (!retiredSemantic.IsSuccess() || retiredSemantic.Value is not { Empty: false, State: BaseActivationState.Succeeded })
    throw new InvalidOperationException("InMemory Native AOT semantic retirement failed: " + retiredSemantic.Error?.Code);
BaseMutationRequestIdentity moduleIdentity = BaseMutationRequestIdentity.Create(
    "aot", "module-increment", "module-request-1",
    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-module-request"u8)));
BaseInstalledModuleMutationHandle<ModuleMutationSmokeRequest, ModuleMutationSmokeResult> module =
    session.ModuleMutations.Get(ModuleMutationSmoke.Identity);
Guid moduleGuid = Guid.Parse("0f9a4bc4-f95f-4d9e-840c-35d6d81bed52");
Guid moduleCreateGuid = Guid.Parse("1f9a4bc4-f95f-4d9e-840c-35d6d81bed52");
Guid moduleDeleteGuid = Guid.Parse("2f9a4bc4-f95f-4d9e-840c-35d6d81bed52");
BaseCollectionSession<AotModuleRecord> moduleRecords = session.Collection(AotModuleRecord.Collection);
_ = (await moduleRecords.CreateAsync(RecordId.Create(moduleGuid.ToString("D")),
    new AotModuleRecord { Name = "module", Status = "pending" })).RequireValue();
_ = (await moduleRecords.CreateAsync(RecordId.Create(moduleDeleteGuid.ToString("D")),
    new AotModuleRecord { Name = "delete", Status = "pending" })).RequireValue();
BaseBinary modulePayload = BaseBinary.From([1, 2, 3, 4]);
BaseCanonicalJson moduleMetadata = BaseCanonicalJson.ParseAndValidate("{\"a\":1}"u8, new BaseCanonicalJsonLimits
{ MaximumCanonicalBytes = 256, MaximumDepth = 4, MaximumArrayItemsPerContainer = 8, MaximumObjectPropertiesPerContainer = 8, MaximumTotalNodes = 16, MaximumTotalStringUtf8Bytes = 64, MaximumTotalNameUtf8Bytes = 64 });
BaseResult<BaseModuleMutationExecutionResult<ModuleMutationSmokeResult>> moduleCommitResult =
    await module.ExecuteAsync(new ModuleMutationSmokeRequest { EventAt = DateTimeOffset.UnixEpoch, Id = moduleGuid, CreateId = moduleCreateGuid, DeleteId = moduleDeleteGuid, Payload = modulePayload, Metadata = moduleMetadata, Mode = AotModuleMode.Ready, EnableHostile = false, HostileId = " " }, moduleIdentity);
if (moduleCommitResult is BaseFailure<BaseModuleMutationExecutionResult<ModuleMutationSmokeResult>> moduleFailure)
    throw new InvalidOperationException($"InMemory L50 removal failed: {moduleFailure.Status}/{moduleFailure.Error.Code}/{moduleFailure.Error.Message}/{moduleFailure.Error.Detail}");
BaseModuleMutationExecutionResult<ModuleMutationSmokeResult> moduleCommitted = moduleCommitResult.RequireValue();
BaseModuleMutationExecutionResult<ModuleMutationSmokeResult> moduleDuplicate =
    (await module.ExecuteAsync(new ModuleMutationSmokeRequest { EventAt = DateTimeOffset.UnixEpoch, Id = moduleGuid, CreateId = moduleCreateGuid, DeleteId = moduleDeleteGuid, Payload = modulePayload, Metadata = moduleMetadata, Mode = AotModuleMode.Ready, EnableHostile = false, HostileId = " " }, moduleIdentity)).RequireValue();
if (moduleCommitted.Result.Generation.ToCanonicalString() != "1" || moduleCommitted.Result.Id != moduleGuid
    || moduleCommitted.Result.Mode != AotModuleMode.Ready || !moduleCommitted.Result.Payload.Equals(modulePayload)
    || moduleCommitted.Disposition != BaseMutationRequestDisposition.Committed
    || moduleDuplicate.Result.Generation.ToCanonicalString() != "1" || moduleDuplicate.Disposition != BaseMutationRequestDisposition.Duplicate)
    throw new InvalidOperationException("InMemory L50 generation commit or receipt replay failed.");
AotModuleRecord moduleRecord = (await moduleRecords.GetAsync(RecordId.Create(moduleGuid.ToString("D")))).RequireValue().Value;
if (moduleRecord.Status is not null || moduleRecord.ProcessedAt != DateTimeOffset.UnixEpoch)
    throw new InvalidOperationException("InMemory L50 optional-field removal or L69 value lift did not persist.");
Guid optionalModuleGuid = Guid.Parse("3f9a4bc4-f95f-4d9e-840c-35d6d81bed52");
Guid optionalCreateGuid = Guid.Parse("4f9a4bc4-f95f-4d9e-840c-35d6d81bed52");
Guid optionalDeleteGuid = Guid.Parse("5f9a4bc4-f95f-4d9e-840c-35d6d81bed52");
_ = (await moduleRecords.CreateAsync(RecordId.Create(optionalModuleGuid.ToString("D")),
    new AotModuleRecord { Name = "module-present", Status = "pending" })).RequireValue();
_ = (await moduleRecords.CreateAsync(RecordId.Create(optionalDeleteGuid.ToString("D")),
    new AotModuleRecord { Name = "delete-present", Status = "pending" })).RequireValue();
BaseMutationRequestIdentity optionalModuleIdentity = BaseMutationRequestIdentity.Create(
    "aot", "module-increment", "module-request-present-1",
    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-module-request-present"u8)));
BaseModuleMutationExecutionResult<ModuleMutationSmokeResult> optionalModuleCommitted =
    (await module.ExecuteAsync(new ModuleMutationSmokeRequest
    {
        EventAt = DateTimeOffset.UnixEpoch, Id = optionalModuleGuid, CreateId = optionalCreateGuid, DeleteId = optionalDeleteGuid,
        Payload = modulePayload, Metadata = moduleMetadata, Mode = AotModuleMode.Ready,
        EnableHostile = false, HostileId = " ", OptionalAt = DateTimeOffset.UnixEpoch,
        OptionalTarget = BaseRecordId<AotModuleRecord>.Create(optionalModuleGuid.ToString("D")),
    }, optionalModuleIdentity)).RequireValue();
if (optionalModuleCommitted.Disposition != BaseMutationRequestDisposition.Committed)
    throw new InvalidOperationException("InMemory L68 present optional-value execution failed.");
if (string.Equals(Environment.GetEnvironmentVariable("HPD_BASE_L68_ONLY"), "1", StringComparison.Ordinal))
{
    if (JsonSerializer.IsReflectionEnabledByDefault)
        throw new InvalidOperationException("L68 Native AOT proof enabled JSON reflection fallback.");
    return;
}
BaseResult<BaseRecord<AotModuleRecord>> moduleCreatedRecord = await moduleRecords.GetAsync(RecordId.Create(moduleCreateGuid.ToString("D")));
BaseResult<BaseRecord<AotModuleRecord>> moduleDeletedRecord = await moduleRecords.GetAsync(RecordId.Create(moduleDeleteGuid.ToString("D")));
if (moduleCreatedRecord.RequireValue().Value.Name != "created" || moduleDeletedRecord is BaseSuccess<BaseRecord<AotModuleRecord>>)
    throw new InvalidOperationException("InMemory typed record-ID create/delete did not persist.");
if (provider.GetRequiredService<HPDBaseInstalledFeatures>().Provider != "inmemory"
    || provider.GetRequiredService<IRecordStore>().Capabilities.StoreKind != BaseStoreKinds.InMemory)
{
    throw new InvalidOperationException(
        "The built-in InMemory provider must be the Native AOT-safe default.");
}

BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
    "aot", "create-project", "request-1",
    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-request"u8)));
BaseBatchBuilder initial = session.Atomic(identity);
initial.Create(collection, RecordId.Create("project-1"), value);
BaseBatchBuilder retry = session.Atomic(identity);
retry.Create(collection, RecordId.Create("project-1"), value);
BaseResult<BaseBatchResult> committed = await initial.CommitAsync();
BaseResult<BaseBatchResult> duplicate = await retry.CommitAsync();
if (committed is not BaseSuccess<BaseBatchResult> committedSuccess
    || committedSuccess.Value.RequestDisposition != BaseMutationRequestDisposition.Committed
    || duplicate is not BaseSuccess<BaseBatchResult> duplicateSuccess
    || duplicateSuccess.Value.RequestDisposition != BaseMutationRequestDisposition.Duplicate)
{
    throw new InvalidOperationException("InMemory identified request replay failed.");
}
_ = (await projects.CreateAsync(RecordId.Create("project-2"), value with { Name = "AOT 2" })).RequireValue();
BasePage<BaseRecord<AotProject>> firstPage = (await projects.Query()
    .OrderBy(AotProject.Fields.Name)
    .Take(1)
    .PageAsync()).RequireValue();
if (firstPage.Page.NextCursor is null)
    throw new InvalidOperationException("InMemory opaque cursor continuation failed.");

var principal = new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "aot",
    CurrentTenantId = "tenant-a",
};
IBaseRecordRuntime runtime = provider.GetRequiredService<IBaseRecordRuntime>();
OperationResult<RecordEnvelope> subjectCreate = await runtime.CreateAsync(
    AotPrivateSubjectRecord.Collection.Id,
    new RecordCreateRequest
    {
        RequestedId = RecordId.Create("subject-1"),
        Payload = JsonPayload("""{"active":true,"tombstoned":false,"tenant":"tenant-a","nonce":"AQIDBA=="}"""),
    },
    principal,
    Operation(BaseOperationKind.Create, AotPrivateSubjectRecord.Collection.Id));
if (!subjectCreate.IsSuccess())
    throw new InvalidOperationException("InMemory exported-subject creation failed: " + subjectCreate.Error?.Code);

BaseSession subjectSession = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
BaseBinary aotBinary = (await subjectSession.Reads.ToArrayAsync(
    AotBinaryRead.Handle, new AotBinaryRead { Nonce = BaseBinary.From([1, 2, 3, 4]) }))
    .RequireValue().Single().Nonce;
if (!aotBinary.Equals(BaseBinary.From([1, 2, 3, 4])))
    throw new InvalidOperationException("InMemory bounded binary registered read failed.");
BaseSubjectReference<AotSubject> subjectReference = (await subjectSession.Reads.ToArrayAsync(
    AotAcquireSubject.Handle,
    new AotAcquireSubject { SubjectId = BaseRecordId<AotPrivateSubjectRecord>.Create("subject-1") }))
    .RequireValue()
    .Single()
    .Reference;
OperationResult<RecordEnvelope> referenceCreate = await runtime.CreateAsync(
    AotSubjectConsumerRecord.Collection.Id,
    new RecordCreateRequest
    {
        RequestedId = RecordId.Create("consumer-1"),
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = JsonSerializer.SerializeToElement(
                new AotSubjectConsumerRecord { Subject = subjectReference },
                AotApplicationJsonContext.Default.AotSubjectConsumerRecord),
        },
    },
    principal,
    Operation(BaseOperationKind.Create, AotSubjectConsumerRecord.Collection.Id));
if (!referenceCreate.IsSuccess())
    throw new InvalidOperationException("InMemory subject-reference validation failed: " + referenceCreate.Error?.Code);

BaseInstalledModuleMutationHandle<SubjectModuleMutationSmokeRequest, SubjectModuleMutationSmokeResult> subjectOperation =
    subjectSession.ModuleMutations.Get(SubjectModuleMutationSmoke.Identity);
BaseMutationRequestIdentity subjectOperationIdentity = BaseMutationRequestIdentity.Create(
    "aot", "subject-verify", "subject-verify-active",
    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-subject-verify-active"u8)));
BaseModuleMutationExecutionResult<SubjectModuleMutationSmokeResult> subjectOperationResult =
    (await subjectOperation.ExecuteAsync(
        new SubjectModuleMutationSmokeRequest { Subject = subjectReference }, subjectOperationIdentity)).RequireValue();
BaseModuleMutationExecutionResult<SubjectModuleMutationSmokeResult> subjectOperationDuplicate =
    (await subjectOperation.ExecuteAsync(
        new SubjectModuleMutationSmokeRequest { Subject = subjectReference }, subjectOperationIdentity)).RequireValue();
BaseModuleMutationExecutionResult<SubjectModuleMutationSmokeResult> subjectOperationResolved =
    (await subjectOperation.ResolveAsync(subjectOperationIdentity)).RequireValue();
if (!subjectOperationResult.Result.Subject.Equals(subjectReference)
    || subjectOperationResult.Disposition != BaseMutationRequestDisposition.Committed
    || subjectOperationDuplicate.Disposition != BaseMutationRequestDisposition.Duplicate
    || subjectOperationResolved.Disposition != BaseMutationRequestDisposition.Duplicate)
    throw new InvalidOperationException("InMemory subject-only L50 authority or receipt replay failed.");

OperationResult<RecordEnvelope> deactivate = await runtime.PatchAsync(
    AotPrivateSubjectRecord.Collection.Id,
    RecordId.Create("subject-1"),
    new RecordPatchRequest { Patch = FieldPatch("active", false), RemovedFieldIds = [] },
    principal,
    Operation(BaseOperationKind.Patch, AotPrivateSubjectRecord.Collection.Id));
if (!deactivate.IsSuccess())
    throw new InvalidOperationException("InMemory subject deactivation failed: " + deactivate.Error?.Code);
BaseResult<BaseModuleMutationExecutionResult<SubjectModuleMutationSmokeResult>> inactiveSubjectOperation =
    await subjectOperation.ExecuteAsync(
        new SubjectModuleMutationSmokeRequest { Subject = subjectReference },
        BaseMutationRequestIdentity.Create("aot", "subject-verify", "subject-verify-inactive",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-subject-verify-inactive"u8))));
if (inactiveSubjectOperation is BaseSuccess<BaseModuleMutationExecutionResult<SubjectModuleMutationSmokeResult>>)
    throw new InvalidOperationException("InMemory subject-only L50 accepted inactive authority.");

BaseGeneratedSubjectLifecycleConsumerIdentity<AotSubject> lifecycleIdentity =
    BaseGeneratedSubjectLifecycleConsumers.Register<AotSubject>(lifecycleConsumer, AotSubject.HPDBaseSubjectRegistration);
BaseInstalledSubjectLifecycleConsumer<AotSubject> lifecycle = subjectSession.SubjectLifecycle.Get(lifecycleIdentity);
var lifecycleDeliveries = new List<BaseSubjectLifecycleDelivery<AotSubject>>();
await foreach (BaseSubjectLifecycleDelivery<AotSubject> delivery in lifecycle.ReadAsync(CancellationToken.None))
    lifecycleDeliveries.Add(delivery);
if (lifecycleDeliveries.Select(static delivery => delivery.Fact.Fact.Kind).ToArray()
        is not [BaseSubjectLifecycleFactKind.Created, BaseSubjectLifecycleFactKind.Transitioned]
    || lifecycleDeliveries[^1].Fact.Fact.Transitioned?.CurrentState != BaseSubjectLifecycleState.Inactive)
    throw new InvalidOperationException("InMemory Native AOT lifecycle delivery did not preserve canonical state ordering.");
BaseSubjectLifecycleDelivery<AotSubject> lifecycleThrough = lifecycleDeliveries[^1];
BaseSubjectLifecycleCheckpointResult lifecycleAdvanced =
    (await lifecycle.AdvanceAsync(lifecycleThrough.Checkpoint, lifecycleThrough.AdvanceIdentity)).RequireValue();
if (lifecycleAdvanced.CheckpointGeneration != 1 || lifecycleAdvanced.Duplicate)
    throw new InvalidOperationException("InMemory Native AOT lifecycle checkpoint did not advance exactly once.");

OperationResult<RecordEnvelope> invalidReference = await runtime.CreateAsync(
    AotSubjectConsumerRecord.Collection.Id,
    new RecordCreateRequest
    {
        RequestedId = RecordId.Create("consumer-2"),
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = JsonSerializer.SerializeToElement(
                new AotSubjectConsumerRecord { Subject = subjectReference },
                AotApplicationJsonContext.Default.AotSubjectConsumerRecord),
        },
    },
    principal,
    Operation(BaseOperationKind.Create, AotSubjectConsumerRecord.Collection.Id));
if (invalidReference.Error?.Code != BaseSubjectErrorCodes.ReferenceInvalid)
    throw new InvalidOperationException("InMemory stale/inactive subject reference was accepted.");

static OperationContext Operation(BaseOperationKind kind, string collectionId) => new()
{
    Operation = kind,
    CollectionId = collectionId,
    Now = DateTimeOffset.UtcNow,
};

static BaseGrantAuthorityDefinition GrantDefinition(string id, string owner) => new()
{
    Id = id, Version = 1, OwningModuleId = owner,
    SourceContractId = owner + ".static-grant", SourceContractVersion = 1,
};

static AccessGrant Grant(string id, string subjectId) => new()
{
    Id = id, ApplicationId = "hpd.base.application", ModuleId = id.Contains("subjectLifecycle", StringComparison.Ordinal) || id.Contains("subject.lifecycle", StringComparison.Ordinal) ? "hpd.base.aot.consumer" : "hpd.base.aot",
    Audience = id == "hpd.base.aot.activation.migrate"
        ? HPDBaseEndpointAudience.ControlPlane
        : HPDBaseEndpointAudience.Application,
    Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = subjectId, TenantId = "tenant-a" },
    Action = id == "hpd.base.aot.module.records.source" ? "hpd.base.aot.module.records"
        : id is SemanticActivationSmoke.EnsureGrant or SemanticActivationSmoke.RetireGrant or SemanticActivationSmoke.MaintainGrant
        ? SemanticActivationSmoke.DefinitionId
        : id.StartsWith("hpd.base.aot.activation.", StringComparison.Ordinal)
        ? "hpd.base.aot.activation"
        : id == "hpd.base.aot.subject.lifecycle.read" ? "hpd.base.aot.subject.lifecycle" : id,
    Scope = id == "hpd.base.aot.module.records.source"
        ? new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = "hpd.base.aot.module.records", TenantId = "tenant-a" }
        : id.Contains("subjectLifecycle", StringComparison.Ordinal) || id.Contains("subject.lifecycle", StringComparison.Ordinal)
        ? new ResourceScope { Kind = ResourceScopeKind.SubjectContract, SubjectContractId = "hpd.base.aot.subject", SubjectContractVersion = 1, TenantId = "tenant-a" }
        : new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-a" },
};

static RecordPayload JsonPayload(string json)
{
    using JsonDocument document = JsonDocument.Parse(json);
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

namespace HPD.Base.AotSmoke
{
    [BaseCollection("aot.projects", typeof(AotApplicationJsonContext))]
    internal sealed partial record AotProject
    {
        [BaseField("organization-id")]
        public required string OrganizationId { get; init; }

        [BaseField("name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
        public required string Name { get; init; }

        [BaseField("state", AllowedEnumLiterals = ["active-wire", "disabled-wire"])]
        [JsonConverter(typeof(BaseClosedEnumJsonConverter<AotProjectState>))]
        public required AotProjectState State { get; init; }
    }

    internal enum AotProjectState
    {
        [JsonStringEnumMemberName("active-wire")] Active,
        [JsonStringEnumMemberName("disabled-wire")] Disabled,
    }

    [JsonSerializable(typeof(AotProject))]
    [JsonSerializable(typeof(AotPrivateSubjectRecord))]
    [JsonSerializable(typeof(AotSubjectConsumerRecord))]
    [JsonSerializable(typeof(AotAcquireSubject))]
    [JsonSerializable(typeof(AotAcquireSubject.Row), TypeInfoPropertyName = "AotAcquireSubjectRow")]
    [JsonSerializable(typeof(AotBinaryRead))]
    [JsonSerializable(typeof(AotBinaryRead.Row), TypeInfoPropertyName = "AotBinaryReadRow")]
    [JsonSerializable(typeof(ActivationSmokeInput))]
    [JsonSerializable(typeof(ActivationSmokeResult))]
    [JsonSerializable(typeof(ActivationMigrationTargetInput))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal sealed partial class AotApplicationJsonContext : JsonSerializerContext;

    [BaseCollection("aot.subject.private", typeof(AotApplicationJsonContext),
        SystemOwnerModuleId = "hpd.base.aot.subjects")]
    internal sealed partial record AotPrivateSubjectRecord
    {
        [BaseField("subject.active")]
        public required bool Active { get; init; }

        [BaseField("subject.tombstoned")]
        public required bool Tombstoned { get; init; }

        [BaseField("subject.tenant")]
        public required string Tenant { get; init; }

        [BaseField("subject.nonce", MinimumBytes = 4, MaximumBytes = 4)]
        public required BaseBinary Nonce { get; init; }
    }

    [BaseRead("hpd.base.aot.binary", typeof(AotApplicationJsonContext),
        SourceAuthority = BaseRegisteredReadSourceAuthority.System,
        Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
        RequiredGrantId = "hpd.base.aot.subject.acquire",
        ConfidentialOutputFieldIds = ["hpd.base.aot.binary.nonce"],
        SystemSourceIds = ["aot.subject.private"])]
    internal sealed partial record AotBinaryRead
    {
        [BaseReadParameter("hpd.base.aot.binary.nonce", MinimumBytes = 4, MaximumBytes = 4)]
        public required BaseBinary Nonce { get; init; }

        public sealed partial record Row
        {
            [BaseReadField("hpd.base.aot.binary.nonce", MinimumBytes = 4, MaximumBytes = 4)]
            public required BaseBinary Nonce { get; init; }
        }

        public static void Configure(BaseReadDefinitionBuilder<AotBinaryRead, Row> read) => read
            .From(AotPrivateSubjectRecord.Collection, "subjects", out BaseReadSource<AotPrivateSubjectRecord> subject)
            .Where(subject.Field(AotPrivateSubjectRecord.Fields.Nonce).Equal(read.Parameter(Parameters.Nonce)))
            .Project(Row.Fields.Nonce, subject.Field(AotPrivateSubjectRecord.Fields.Nonce));
    }

    [BaseExportedSubject("hpd.base.aot.subject",
        OwningModuleId = "hpd.base.aot.subjects",
        PrivateRecordType = typeof(AotPrivateSubjectRecord),
        AcquisitionGrantId = "hpd.base.aot.subject.acquire",
        ValidationGrantId = "hpd.base.aot.subject.validate",
        AdministrationGrantId = "hpd.base.aot.subject.rotate",
        ValidationPlanId = "hpd.base.aot.subject.validate.v1",
        Scope = BaseSubjectScopeKind.Tenant,
        ActiveFieldId = "subject.active",
        TombstoneFieldId = "subject.tombstoned",
        ScopeFieldId = "subject.tenant")]
    internal sealed partial class AotSubject;

    [BaseCollection("aot.subject.consumers", typeof(AotApplicationJsonContext))]
    internal sealed partial record AotSubjectConsumerRecord
    {
        [BaseField("consumer.subject")]
        [BaseSubjectReference(typeof(AotSubject), Requirement = BaseSubjectReferenceRequirement.Active)]
        public required BaseSubjectReference<AotSubject> Subject { get; init; }
    }

    [BaseRead("hpd.base.aot.subject.acquire", typeof(AotApplicationJsonContext),
        SourceAuthority = BaseRegisteredReadSourceAuthority.System,
        Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
        RequiredGrantId = "hpd.base.aot.subject.acquire",
        SystemSourceIds = ["aot.subject.private"])]
    internal sealed partial record AotAcquireSubject
    {
        [BaseReadParameter("hpd.base.aot.subject.acquire.id")]
        public required BaseRecordId<AotPrivateSubjectRecord> SubjectId { get; init; }

        public sealed partial record Row
        {
            [BaseReadField("hpd.base.aot.subject.acquire.reference")]
            public required BaseSubjectReference<AotSubject> Reference { get; init; }
        }

        public static void Configure(BaseReadDefinitionBuilder<AotAcquireSubject, Row> read)
        {
            read.From(AotPrivateSubjectRecord.Collection, "subjects", out BaseReadSource<AotPrivateSubjectRecord> subject)
                .Where(subject.RecordId.Equal(read.Parameter(Parameters.SubjectId)))
                .ProjectSubjectReference(Row.Fields.Reference, subject, AotSubject.HPDBaseSubjectRegistration);
        }
    }

    internal sealed class AotAllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = request;
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo
                {
                    MatchedGrantIds =
                    [
                        "hpd.base.aot.subject.private",
                        "hpd.base.aot.subject.acquire",
                        "hpd.base.aot.subject.validate",
                        "hpd.base.aot.subject.rotate",
                    ],
                },
            });
        }
    }
}
