using HPD.Base;
#if !PAYMENTS_SQLITE
using HPD.Payments.Adapters.InMemory;
#endif
using HPD.Payments.Runtime.Base;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.History;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Contracts.MeasuredFact;
using HPD.Payments.Contracts.MeasurementGeneration;
using HPD.Payments.Contracts.Valuation;
using HPD.Payments.Runtime.UsageValuation;
using HPD.Payments.Runtime.Billing;
using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Runtime.ExternalEffects;
using HPD.Payments.Runtime.Card;
using HPD.Payments.Contracts.HeldPosition.QuotaWallet;
using HPD.Payments.Runtime.QuotaWallet;
using HPD.Payments.Runtime.Entitlement;
using HPD.Payments.Contracts.EntitlementGrantRemovalFact;
using HPD.Payments.Contracts.RestrictionFact;
using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Runtime.DurableWork;
using HPD.Payments.Runtime.Repair;
using HPD.Payments.Runtime.Settlement;
using HPD.Payments.Profiles.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json.Serialization;
#if PAYMENTS_SQLITE
using HPD.Base.Sqlite;
using HPD.Payments.Adapters.Sqlite;
#endif

#if PAYMENTS_SQLITE
string sqlitePath = Path.Combine(Path.GetTempPath(), "hpd-payments-07s-" + Guid.NewGuid().ToString("N") + ".db");
using var sqliteCleanup = new SqliteDatabaseCleanup(sqlitePath);
#endif
var services = new ServiceCollection();
services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
services.AddHPDBase(builder =>
{
#if PAYMENTS_SQLITE
    builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x71, 32).ToArray());
    builder.UseStore(SqliteStore.Configure(options =>
    {
        options.StoreId = "hpd.payments.sqlite";
        options.DataSource = sqlitePath;
        options.AdministrationEnabled = true;
    }));
#endif
    builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition
    {
        Id = "hpd.payments.base.policy", Version = 1, OwningModuleId = "hpd.payments",
        EvaluatorContractId = "hpd.payments.base.policy", EvaluatorContractVersion = 1, CompositionOrder = 0,
    }, new AllowPolicy());
    builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
    {
        Id = "hpd.payments.owner-ledger.advance", Version = 1, OwningModuleId = "hpd.payments",
        SourceContractId = "hpd.payments.base.grants", SourceContractVersion = 1,
    }, new AccessGrant
    {
        Id = "hpd.payments.owner-ledger.advance",
        ApplicationId = "hpd.base.application", ModuleId = "hpd.payments", Audience = HPDBaseEndpointAudience.ControlPlane,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-worker", TenantId = "tenant-one" },
        Action = "hpd.payments.owner-ledger.advance", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-one" },
    });
    AddSourceGrant(builder, "hpd.payments.ledger-head.source", PaymentsLedgerHead.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.owner-state.source", PaymentsOwnerState.Collection.Id);
    builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
    {
        Id = "hpd.payments.owner-fact.append", Version = 1, OwningModuleId = "hpd.payments",
        SourceContractId = "hpd.payments.base.grants", SourceContractVersion = 1,
    }, new AccessGrant
    {
        Id = "hpd.payments.owner-fact.append", ApplicationId = "hpd.base.application", ModuleId = "hpd.payments",
        Audience = HPDBaseEndpointAudience.ControlPlane,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-worker", TenantId = "tenant-one" },
        Action = "hpd.payments.owner-fact.append", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-one" },
    });
    AddSourceGrant(builder, "hpd.payments.owner-fact.source", PaymentsOwnerFactEvent.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.owner-fact-head.source", PaymentsOwnerFactHead.Collection.Id);
    AddOperationGrant(builder, "hpd.payments.relation.persist");
    AddOperationGrant(builder, "hpd.payments.continuation.persist");
    AddOperationGrant(builder, "hpd.payments.custody.persist");
    AddSourceGrant(builder, "hpd.payments.relation.source", PaymentsRelationRecord.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.continuation.source", PaymentsContinuationRecord.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.custody.source", PaymentsCustodyRecord.Collection.Id);
    builder.AddPaymentsModuleMutations();
    builder.AddPaymentsOwnerFactPersistence();
    builder.AddPaymentsSupportingPersistence();
});

static void AddOperationGrant(HPDBaseBuilder builder, string grantId) =>
    builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
    {
        Id = grantId, Version = 1, OwningModuleId = "hpd.payments",
        SourceContractId = "hpd.payments.base.grants", SourceContractVersion = 1,
    }, new AccessGrant
    {
        Id = grantId, ApplicationId = "hpd.base.application", ModuleId = "hpd.payments",
        Audience = HPDBaseEndpointAudience.ControlPlane,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-worker", TenantId = "tenant-one" },
        Action = grantId, Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-one" },
    });

static void AddSourceGrant(HPDBaseBuilder builder, string grantId, string collectionId) =>
    builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
    {
        Id = grantId, Version = 1, OwningModuleId = "hpd.payments",
        SourceContractId = "hpd.payments.base.grants", SourceContractVersion = 1,
    }, new AccessGrant
    {
        Id = grantId, ApplicationId = "hpd.base.application", ModuleId = "hpd.payments",
        Audience = HPDBaseEndpointAudience.ControlPlane,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-worker", TenantId = "tenant-one" },
        Action = collectionId,
        Scope = new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = collectionId, TenantId = "tenant-one" },
    });
await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
#if PAYMENTS_SQLITE
IBaseSchemaManager schemaManager = provider.GetRequiredService<IBaseSchemaManager>();
OperationResult<BaseSchemaPlan> schemaPlanResult = await schemaManager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "hpd.payments.sqlite" });
BaseSchemaPlan schemaPlan = schemaPlanResult.IsSuccess() && schemaPlanResult.Value is not null
    ? schemaPlanResult.Value : throw new InvalidOperationException(schemaPlanResult.Error?.Code ?? "Base SQLite schema planning failed.");
if (!(await schemaManager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = schemaPlan.ProtectedArtifact })).IsSuccess())
    throw new InvalidOperationException("Base SQLite schema application failed.");
#endif
OperationResult<BaseApplicationReadiness> initialized = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
if (!initialized.IsSuccess()) throw new InvalidOperationException(initialized.Error?.Code);

var principal = new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "payments-worker",
    CurrentTenantId = "tenant-one",
};
BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal, options => options.Audience = HPDBaseEndpointAudience.ControlPlane);
var request = new AdvanceOwnerGenerationRequest { OwnerId = "owner-one", OperationId = "payment-one" };
BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
    "hpd.payments", "owner-ledger", "payment-one", BaseMutationRequestFingerprint.Create(new byte[32]));
BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> first =
    await PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, request, identity);
if (first is not BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>
    || first.RequireValue().Result.OwnerGeneration.ToCanonicalString() != "1"
    || first.RequireValue().Result.LedgerGeneration.ToCanonicalString() != "1")
    throw new InvalidOperationException(first is BaseFailure<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> failure ? failure.Error.Code : "First Payments execution failed.");

BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> duplicate =
    await PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, request, identity);
if (duplicate is not BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>
    || duplicate.RequireValue().Disposition != BaseMutationRequestDisposition.Duplicate)
    throw new InvalidOperationException(duplicate is BaseFailure<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> failure ? failure.Error.Code : "Payments replay failed.");

var secondRequest = request with
{
    OperationId = "payment-two",
    ExpectedOwnerGeneration = first.RequireValue().Result.OwnerGeneration,
    ExpectedLedgerGeneration = first.RequireValue().Result.LedgerGeneration,
};
BaseMutationRequestIdentity secondIdentity = BaseMutationRequestIdentity.Create(
    "hpd.payments", "owner-ledger", "payment-two", BaseMutationRequestFingerprint.Create(new byte[32]));
BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> second =
    await PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, secondRequest, secondIdentity);
if (second is not BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>
    || second.RequireValue().Result.OwnerGeneration.ToCanonicalString() != "2"
    || second.RequireValue().Result.LedgerGeneration.ToCanonicalString() != "2")
    throw new InvalidOperationException(second is BaseFailure<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> failure ? failure.Error.Code : "Guarded Payments execution failed.");

AdvanceOwnerGenerationResult generationTwo = second.RequireValue().Result;
var contenderA = secondRequest with
{
    OperationId = "payment-three-a",
    ExpectedOwnerGeneration = generationTwo.OwnerGeneration,
    ExpectedLedgerGeneration = generationTwo.LedgerGeneration,
};
var contenderB = contenderA with { OperationId = "payment-three-b" };
Task<BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>> contenderATask =
    PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, contenderA, BaseMutationRequestIdentity.Create(
        "hpd.payments", "owner-ledger", "payment-three-a", BaseMutationRequestFingerprint.Create(new byte[32]))).AsTask();
Task<BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>> contenderBTask =
    PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, contenderB, BaseMutationRequestIdentity.Create(
        "hpd.payments", "owner-ledger", "payment-three-b", BaseMutationRequestFingerprint.Create(new byte[32]))).AsTask();
await Task.WhenAll(contenderATask, contenderBTask);
BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>[] contenders =
    [await contenderATask, await contenderBTask];
if (contenders.Count(static result => result is BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>) != 1
    || contenders.Count(static result => result is BaseFailure<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>) != 1)
    throw new InvalidOperationException("Concurrent generation guards did not produce exactly one winner.");
AdvanceOwnerGenerationResult winner = contenders
    .OfType<BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>>()
    .Single().Value.Result;
if (winner.OwnerGeneration.ToCanonicalString() != "3" || winner.LedgerGeneration.ToCanonicalString() != "3")
    throw new InvalidOperationException("Concurrent generation winner did not publish generation three.");

#if PAYMENTS_SQLITE
var persistence = new BaseSqlitePaymentsPersistence(session);
#else
var persistence = new BaseInMemoryPaymentsPersistence(session);
#endif
var factPort = persistence.CreateOwnerPort(new PaymentsFactJsonCodec<BridgeFact>("bridge-fact-v1", BridgeJsonContext.Default.BridgeFact));
ScopeId paymentScope = ScopeId.Create("tenant-one", "test", "measured-fact");
SemanticId factOwnerId = SemanticId.Create(paymentScope, "bridge", "owner", "one");
var expectedOwner = new OwnerReference(FrozenAuthority.MeasuredFact, factOwnerId, OwnerGeneration.Create(1));
var atomicDomain = new AtomicDomain(SemanticId.Create(paymentScope, "bridge", "domain", "local"), AtomicDomainKind.Local, Revision.Create("topology", 1));
CanonicalDigestProfileId digestProfile = new("bridge", ContractVersion.Create(1, 0), "all", "ordinal", "utc", "canonical", "test");
CanonicalDigest firstDigest = CanonicalDigest.Sha256(digestProfile, "fact-one"u8);
var firstFact = new BridgeFact("one", 10);
var firstAppend = new OwnerAppendRequest<BridgeFact>(expectedOwner, firstDigest, atomicDomain, firstFact);
OwnerAppendReceipt<BridgeFact> factFirst = await factPort.CompareBindAppendAsync(firstAppend);
OwnerAppendReceipt<BridgeFact> factReplay = await factPort.CompareBindAppendAsync(firstAppend);
if (factFirst.Disposition != OwnerAppendDisposition.Appended || factFirst.ObservedGeneration.Value != 2
    || factReplay.Disposition != OwnerAppendDisposition.Replay || factReplay.Fact != firstFact)
    throw new InvalidOperationException("Base-backed fact append/replay failed.");
OwnerAppendReceipt<BridgeFact> digestCollision = await factPort.CompareBindAppendAsync(new(
    expectedOwner, firstDigest, atomicDomain, new BridgeFact("substituted", 999)));
if (digestCollision.Disposition != OwnerAppendDisposition.Conflict)
    throw new InvalidOperationException($"Base-backed digest identity reuse did not fail closed: {digestCollision.Disposition}/{digestCollision.Code}.");
var distributedDomain = new AtomicDomain(SemanticId.Create(paymentScope, "bridge", "domain", "distributed"),
    AtomicDomainKind.DistributedOwner, Revision.Create("topology", 1));
if ((await factPort.CompareBindAppendAsync(new(expectedOwner, CanonicalDigest.Sha256(digestProfile, "unsupported"u8),
        distributedDomain, new BridgeFact("unsupported", 1)))).Disposition != OwnerAppendDisposition.Unsupported)
    throw new InvalidOperationException("Base-backed E-LOCAL boundary was not explicit.");

var currentOwner = new OwnerReference(FrozenAuthority.MeasuredFact, factOwnerId, factFirst.ObservedGeneration);
var secondFact = new BridgeFact("two", 20);
OwnerAppendReceipt<BridgeFact> secondAppend = await factPort.CompareBindAppendAsync(new(
    currentOwner, CanonicalDigest.Sha256(digestProfile, "fact-two"u8), atomicDomain, secondFact));
OwnerAppendReceipt<BridgeFact> stale = await factPort.CompareBindAppendAsync(new(
    expectedOwner, CanonicalDigest.Sha256(digestProfile, "fact-stale"u8), atomicDomain, new BridgeFact("stale", 99)));
if (secondAppend.Disposition != OwnerAppendDisposition.Appended
    || stale.Disposition != OwnerAppendDisposition.Conflict)
    throw new InvalidOperationException($"Base-backed fact successor/conflict failed: second={secondAppend.Disposition}/{secondAppend.Code}, stale={stale.Disposition}/{stale.Code}.");
RequireGeneration(secondAppend, 3);
RequireGeneration(stale, 3);

var through = new OwnerReference(FrozenAuthority.MeasuredFact, factOwnerId, secondAppend.ObservedGeneration);
var frame = new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
    NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(through)]);
OwnerHistoryPage<BridgeFact> history = await factPort.ReadHistoryAsync(new OwnerHistoryRequest(through, frame, 10));
if (!history.Facts.SequenceEqual([firstFact, secondFact]) || history.ThroughGeneration.Value != 3)
    throw new InvalidOperationException("Base-backed bounded fact history failed.");
OwnerHistoryPage<BridgeFact> pageOne = await factPort.ReadHistoryAsync(new OwnerHistoryRequest(through, frame, 1));
OwnerHistoryPage<BridgeFact> pageTwo = await factPort.ReadHistoryAsync(new OwnerHistoryRequest(through, frame, 1), pageOne.Continuation);
if (!pageOne.Facts.SequenceEqual([firstFact]) || pageOne.Continuation.IsEmpty
    || !pageTwo.Facts.SequenceEqual([secondFact]) || !pageTwo.Continuation.IsEmpty)
    throw new InvalidOperationException("Base-backed fact history continuation failed.");

SemanticId raceOwnerId = SemanticId.Create(paymentScope, "bridge", "owner", "race");
var raceOwner = new OwnerReference(FrozenAuthority.MeasuredFact, raceOwnerId, OwnerGeneration.Create(1));
Task<OwnerAppendReceipt<BridgeFact>> raceA = factPort.CompareBindAppendAsync(new(raceOwner,
    CanonicalDigest.Sha256(digestProfile, "race-a"u8), atomicDomain, new BridgeFact("race-a", 1))).AsTask();
Task<OwnerAppendReceipt<BridgeFact>> raceB = factPort.CompareBindAppendAsync(new(raceOwner,
    CanonicalDigest.Sha256(digestProfile, "race-b"u8), atomicDomain, new BridgeFact("race-b", 1))).AsTask();
OwnerAppendReceipt<BridgeFact>[] race = await Task.WhenAll(raceA, raceB);
if (race.Count(value => value.Disposition == OwnerAppendDisposition.Appended) != 1
    || race.Count(value => value.Disposition == OwnerAppendDisposition.Conflict) != 1)
    throw new InvalidOperationException("Base-backed fact contention did not conserve one winner.");
SemanticId h7OwnerId = SemanticId.Create(paymentScope, "bridge", "owner", "h7");
var h7Owner = new OwnerReference(FrozenAuthority.MeasuredFact, h7OwnerId, OwnerGeneration.Create(1));
OwnerAppendReceipt<BridgeFact>[] h7 = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => factPort.CompareBindAppendAsync(new(
    h7Owner, CanonicalDigest.Sha256(digestProfile, Encoding.UTF8.GetBytes("h7-" + index)), atomicDomain,
    new BridgeFact("h7-" + index, index + 1))).AsTask()));
if (h7.Count(value => value.Disposition == OwnerAppendDisposition.Appended) != 1
    || h7.Count(value => value.Disposition == OwnerAppendDisposition.Conflict) != 31)
    throw new InvalidOperationException("Base-backed H7 contention did not conserve exactly one append.");

BaseSupportingPersistencePort supportingPort = persistence.Supporting;
SemanticId rightOwnerId = SemanticId.Create(paymentScope, "bridge", "owner", "right");
var rightExpected = new OwnerReference(FrozenAuthority.Obligation, rightOwnerId, OwnerGeneration.Create(1));
OwnerAppendReceipt<BridgeFact> rightAppend = await factPort.CompareBindAppendAsync(new(rightExpected,
    CanonicalDigest.Sha256(digestProfile, "right"u8), atomicDomain, new BridgeFact("right", 1)));
var rightCurrent = new OwnerReference(FrozenAuthority.Obligation, rightOwnerId, rightAppend.ObservedGeneration);
var relation = new SupportingRelation(SemanticId.Create(paymentScope, "bridge", "relation", "one"), SupportingRelationKind.Application,
    through, rightCurrent, Revision.Create("relation", 1));
PersistenceReceipt relationReceipt = await supportingPort.GuardedRelateAsync(relation, atomicDomain);
var staleRelation = new SupportingRelation(SemanticId.Create(paymentScope, "bridge", "relation", "stale"), SupportingRelationKind.Match,
    expectedOwner, rightCurrent, Revision.Create("relation", 1));
PersistenceReceipt staleRelationReceipt = await supportingPort.GuardedRelateAsync(staleRelation, atomicDomain);
if (relationReceipt.Observation != PersistenceObservation.Observed || staleRelationReceipt.Observation != PersistenceObservation.Failed)
    throw new InvalidOperationException($"Base-backed exact endpoint relation guards failed: current={relationReceipt.Observation}/{relationReceipt.DomainReceipt.Limitation}, stale={staleRelationReceipt.Observation}/{staleRelationReceipt.DomainReceipt.Limitation}.");

for (int index = 0; index < 3; index++)
{
    var declaration = new ContinuationDeclaration(through, SemanticId.Create(paymentScope, "bridge", "continuation", "c" + index),
        CanonicalDigest.Sha256(digestProfile, Encoding.UTF8.GetBytes("continuation-" + index)));
    if ((await supportingPort.CommitDiscoverableAsync(declaration, atomicDomain)).Observation != PersistenceObservation.Observed)
        throw new InvalidOperationException("Base-backed continuation commit failed.");
}
ContinuationDiscoveryPage continuationOne = await supportingPort.DiscoverAsync(atomicDomain, 2);
ContinuationDiscoveryPage continuationTwo = await supportingPort.DiscoverAsync(atomicDomain, 2, continuationOne.Continuation);
if (continuationOne.Items.Count != 2 || continuationOne.Continuation.IsEmpty || continuationTwo.Items.Count != 1 || !continuationTwo.Continuation.IsEmpty)
    throw new InvalidOperationException("Base-backed bounded continuation discovery failed.");
await ThrowsAsync<ArgumentException>(() => supportingPort.DiscoverAsync(atomicDomain, 1, continuationOne.Continuation).AsTask());

ClassificationMark mark = ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable);
var residual = new CustodyInstance(SemanticId.Create(paymentScope, "bridge", "custody", "residual"), through,
    SemanticId.Create(paymentScope, "bridge", "controller", "one"), OwnerGeneration.Create(1), mark, Revision.Create("policy", 1),
    Revision.Create("hold", 1), CustodyState.Residual, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch));
var absent = new CustodyInstance(SemanticId.Create(paymentScope, "bridge", "custody", "absent"), through,
    SemanticId.Create(paymentScope, "bridge", "controller", "one"), OwnerGeneration.Create(1), mark, Revision.Create("policy", 1),
    Revision.Create("hold", 1), CustodyState.VerifiedAbsent, NamedTime.Create(TimeKind.Verify, DateTimeOffset.UnixEpoch));
PersistenceReceipt residueReceipt = await supportingPort.RecordCustodyAsync(residual, atomicDomain);
await supportingPort.RecordCustodyAsync(absent, atomicDomain);
int swept = await supportingPort.SweepVerifiedAbsentAsync(provider.GetRequiredService<IHPDBaseAdministration>(), principal, OwnerGeneration.Create(1));
CustodyPage custody = await supportingPort.ReadCustodyAsync(through, OwnerGeneration.Create(1), 10);
if (residueReceipt.DomainReceipt.Limitation != "residue-retained" || swept != 1 || custody.Items.Count != 1
    || custody.Items.Single(item => item.InstanceId == residual.InstanceId).State != CustodyState.Residual)
    throw new InvalidOperationException("Base-backed custody or named residue preservation failed.");

BaseAdministrationCapability administrationCapability = provider.GetRequiredService<IHPDBaseAdministration>().Capability;
#if PAYMENTS_SQLITE
if (administrationCapability.Backup || administrationCapability.Restore || !administrationCapability.Durable
    || !administrationCapability.AdministrativePurge)
    throw new InvalidOperationException("Base SQLite lifecycle capability did not match the exact unkeyed bridge graph.");
#else
if (administrationCapability.Backup || administrationCapability.Restore || administrationCapability.Durable
    || !administrationCapability.AdministrativePurge)
    throw new InvalidOperationException("Base InMemory lifecycle capability was overstated or lost its bounded purge boundary.");
#endif

var admissions = new UsageValuationAdmissions(
    persistence.CreateOwnerPort(PaymentsUsageValuationFactCodecs.MeasuredFact),
    persistence.CreateOwnerPort(PaymentsUsageValuationFactCodecs.MeasurementGeneration),
    persistence.CreateOwnerPort(PaymentsUsageValuationFactCodecs.Valuation));
SemanticId InScope(string authority, string kind, string local) => SemanticId.Create(ScopeId.Create("tenant-one", "test", authority), "bridge", kind, local);
AtomicDomain DomainIn(string authority) => new(InScope(authority, "domain", "local"), AtomicDomainKind.Local, Revision.Create("topology", 1));
SemanticId measuredId = InScope("measured-fact", "fact", "usage-one");
SemanticId measuredSubject = InScope("measured-fact", "subject", "customer-one");
NamedTime from = NamedTime.Create(TimeKind.Source, DateTimeOffset.UnixEpoch);
NamedTime until = NamedTime.Create(TimeKind.Source, DateTimeOffset.UnixEpoch.AddHours(1));
var measuredCommand = new AdmitMeasuredFactCommand(measuredId, measuredSubject, InScope("measured-fact", "source", "meter-one"),
    CanonicalDigest.Sha256(digestProfile, "p14d-measured"u8), MeasuredQuantity.Create(12.5m, "kwh"), from, until,
    Revision.Create("meter", 1), OwnerGeneration.Create(1));
NamedTime acceptedAt = NamedTime.Create(TimeKind.Accepted, DateTimeOffset.UnixEpoch.AddHours(2));
OwnerAppendReceipt<MeasuredFactRecord> measured = await admissions.AdmitMeasuredAsync(measuredCommand, DomainIn("measured-fact"), acceptedAt, ContractVersion.Create(1, 0));
OwnerAppendReceipt<MeasuredFactRecord> measuredReplay = await admissions.AdmitMeasuredAsync(measuredCommand, DomainIn("measured-fact"), acceptedAt, ContractVersion.Create(1, 0));
if (measured.Disposition != OwnerAppendDisposition.Appended || measuredReplay.Disposition != OwnerAppendDisposition.Replay)
    throw new InvalidOperationException("Base-backed measured-fact admission/replay failed.");
var sourceCut = new HPD.Payments.Primitives.Time.HistoricalCut(HPD.Payments.Primitives.Time.HistoricalFrameKind.AsKnownAt,
    NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch.AddHours(2)), [], ContractVersion.Create(1, 0));
SemanticId generationId = InScope("measurement-generation", "generation", "hour-one");
var generationCommand = new CreateMeasurementGenerationCommand(generationId, measuredSubject,
    NamedTime.Create(TimeKind.Effective, from.Value), NamedTime.Create(TimeKind.Effective, until.Value), sourceCut,
    new MeasurementAlgebraContract(MeasurementAlgebraKind.Sum, Revision.Create("sum", 1), true, false, true, false),
    [measuredId], GenerationCompleteness.Complete, OwnerGeneration.Create(1));
NamedTime calculatedAt = NamedTime.Create(TimeKind.Calculated, DateTimeOffset.UnixEpoch.AddHours(2));
OwnerAppendReceipt<MeasurementGenerationFact> generation = await admissions.AdmitGenerationAsync(generationCommand, [measured.Fact!],
    CanonicalDigest.Sha256(digestProfile, "p14d-generation"u8), DomainIn("measurement-generation"), calculatedAt);
if (generation.Fact?.Result != 12.5m || generation.Fact.Unit != "kwh")
    throw new InvalidOperationException("Base-backed exact measurement generation failed.");
var rounding = new RoundingContract(2, MidpointRounding.ToEven, "line");
Revision algorithmRevision = Revision.Create("unit-rate", 1); Revision pricingRevision = Revision.Create("price", 7);
var manifest = new ValuationInputManifest(InScope("valuation", "manifest", "one"), generationId, sourceCut, pricingRevision,
    algorithmRevision, rounding, ReproducibilityKind.ExactRecomputable, [generationId, measuredId], CanonicalDigest.Sha256(digestProfile, "p14d-manifest"u8));
var algorithm = new UnitRateValuationAlgorithm(algorithmRevision, pricingRevision, 0.20m, "USD");
AdmitValuationCommand valuationCommand = algorithm.Calculate(InScope("valuation", "valuation", "one"), manifest, generation.Fact!, OwnerGeneration.Create(1), calculatedAt);
OwnerAppendReceipt<ValuationFact> valuation = await admissions.AdmitValuationAsync(valuationCommand,
    CanonicalDigest.Sha256(digestProfile, "p14d-valuation"u8), DomainIn("valuation"), acceptedAt);
if (valuation.Fact?.Admission.Result.Precise != 2.500m || valuation.Fact.Admission.Result.Rounded != 2.50m)
    throw new InvalidOperationException("Base-backed revision-bound valuation failed.");

OwnerHistoryPage<MeasuredFactRecord> measuredHistory = await persistence.CreateOwnerPort(PaymentsUsageValuationFactCodecs.MeasuredFact).ReadHistoryAsync(
    new OwnerHistoryRequest(measured.Owner, new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
        NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(measured.Owner)]), 8));
if (measuredHistory.Facts.Count != 1 || measuredHistory.Facts[0] != measured.Fact)
    throw new InvalidOperationException("Base-backed measured-fact codec did not reconstruct exact history.");

SemanticId billingManifestId = InScope("issuance-fact", "manifest", "invoice-one");
var billingManifest = new BillingManifest(billingManifestId,
    [InScope("obligation", "fact", "due-one"), InScope("obligation", "fact", "credit-one")], sourceCut,
    Revision.Create("tax", 1), Revision.Create("fx", 1), Revision.Create("rounding", 1), BillingClosureKind.Progressive,
    CanonicalDigest.Sha256(digestProfile, "billing-manifest"u8));
IOwnerPersistencePort<BillingManifest> billingPort = persistence.CreateOwnerPort(PaymentsBillingFactCodecs.Manifest);
var billingOwner = new OwnerReference(FrozenAuthority.IssuanceFact, billingManifestId, OwnerGeneration.Create(1));
var billingRequest = new OwnerAppendRequest<BillingManifest>(billingOwner, billingManifest.Digest, DomainIn("issuance-fact"), billingManifest);
OwnerAppendReceipt<BillingManifest> billingAppend = await billingPort.CompareBindAppendAsync(billingRequest);
OwnerAppendReceipt<BillingManifest> billingReplay = await billingPort.CompareBindAppendAsync(billingRequest);
OwnerHistoryPage<BillingManifest> billingHistory = await billingPort.ReadHistoryAsync(new OwnerHistoryRequest(billingAppend.Owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
        NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(billingAppend.Owner)]), 8));
if (billingAppend.Disposition != OwnerAppendDisposition.Appended || billingReplay.Disposition != OwnerAppendDisposition.Replay ||
    billingHistory.Facts.Count != 1 || billingHistory.Facts[0].ManifestId.ToString() != billingManifest.ManifestId.ToString() ||
    billingHistory.Facts[0].Digest.ToString() != billingManifest.Digest.ToString() ||
    !billingHistory.Facts[0].ObligationFactIds.Select(static id => id.ToString()).SequenceEqual(billingManifest.ObligationFactIds.Select(static id => id.ToString())))
    throw new InvalidOperationException($"Base-backed billing manifest append/replay/history failed: append={billingAppend.Disposition} replay={billingReplay.Disposition} count={billingHistory.Facts.Count}.");

ScopeId effectScope = ScopeId.Create("tenant-one", "test", "external-effect");
SemanticId EffectId(string kind, string local, string? providerName = null, string? account = null) =>
    SemanticId.Create(effectScope, "payment", kind, local, providerName, account);
var effectOperation = new ExternalEffectOperation(EffectId("operation", "pay-one"), EffectId("attempt", "one"),
    EffectId("account", "simulator-main", "simulator", "local"), "pay-one-attempt-one",
    CanonicalDigest.Sha256(digestProfile, "payment-request"u8), Revision.Create("credential", 1), Revision.Create("configuration", 1));
ExternalEffectProtocolState possibleEffect = ExternalEffectProtocolState.Create(effectOperation, CanonicalDigest.Sha256(digestProfile, "not-dispatched"u8))
    .BeginDispatch(CanonicalDigest.Sha256(digestProfile, "dispatching"u8)).State
    .MarkPossibleDispatch(CanonicalDigest.Sha256(digestProfile, "possible-dispatch"u8)).State;
IOwnerPersistencePort<ExternalEffectProtocolState> effectPort = persistence.CreateOwnerPort(PaymentsExternalEffectFactCodecs.State);
var effectOwner = new OwnerReference(FrozenAuthority.ExternalEffect, effectOperation.OperationId, OwnerGeneration.Create(1));
var effectRequest = new OwnerAppendRequest<ExternalEffectProtocolState>(effectOwner, possibleEffect.LatestFactDigest,
    DomainIn("external-effect"), possibleEffect);
OwnerAppendReceipt<ExternalEffectProtocolState> effectAppend = await effectPort.CompareBindAppendAsync(effectRequest);
OwnerAppendReceipt<ExternalEffectProtocolState> effectReplay = await effectPort.CompareBindAppendAsync(effectRequest);
OwnerHistoryPage<ExternalEffectProtocolState> effectHistory = await effectPort.ReadHistoryAsync(new OwnerHistoryRequest(effectAppend.Owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
        NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(effectAppend.Owner)]), 8));
if (effectAppend.Disposition != OwnerAppendDisposition.Appended || effectReplay.Disposition != OwnerAppendDisposition.Replay ||
    effectHistory.Facts.Count != 1 || effectHistory.Facts[0].State != ExternalEffectState.PossibleDispatch ||
    effectHistory.Facts[0].PermitsDispatch || !effectHistory.Facts[0].RequiresResolution)
    throw new InvalidOperationException("Base-backed external payment ambiguity was not preserved.");

SemanticId cardLifecycleId = InScope("value-movement", "card", "lifecycle-one");
SemanticId CardOperation(string local) => InScope("value-movement", "operation", local);
CardLifecycleState cardState = CardLifecycleState.Authorize(cardLifecycleId, 100m, "usd", OwnerGeneration.Create(1), CardOperation("authorize"))
    .Apply(CardLifecycleChangeKind.Capture, 70m, CardOperation("capture"))
    .Apply(CardLifecycleChangeKind.Void, 30m, CardOperation("void"))
    .Apply(CardLifecycleChangeKind.Refund, 10m, CardOperation("refund"))
    .Apply(CardLifecycleChangeKind.OpenDispute, 20m, CardOperation("dispute"))
    .Apply(CardLifecycleChangeKind.Chargeback, 5m, CardOperation("chargeback"));
IOwnerPersistencePort<CardLifecycleState> cardPort = persistence.CreateOwnerPort(PaymentsCardLifecycleFactCodecs.State);
var cardOwner = new OwnerReference(FrozenAuthority.ValueMovement, cardLifecycleId, OwnerGeneration.Create(1));
CanonicalDigest cardDigest = CanonicalDigest.Sha256(digestProfile, "card-lifecycle"u8);
var cardRequest = new OwnerAppendRequest<CardLifecycleState>(cardOwner, cardDigest, DomainIn("value-movement"), cardState);
OwnerAppendReceipt<CardLifecycleState> cardAppend = await cardPort.CompareBindAppendAsync(cardRequest);
OwnerAppendReceipt<CardLifecycleState> cardReplay = await cardPort.CompareBindAppendAsync(cardRequest);
OwnerHistoryPage<CardLifecycleState> cardHistory = await cardPort.ReadHistoryAsync(new OwnerHistoryRequest(cardAppend.Owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
        NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(cardAppend.Owner)]), 8));
if (cardAppend.Disposition != OwnerAppendDisposition.Appended || cardReplay.Disposition != OwnerAppendDisposition.Replay || cardHistory.Facts.Count != 1 ||
    cardHistory.Facts[0].Captured != 70m || cardHistory.Facts[0].Voided != 30m || cardHistory.Facts[0].Refunded != 10m ||
    cardHistory.Facts[0].ChargedBack != 5m || cardHistory.Facts[0].Disputed != 15m || cardHistory.Facts[0].Capturable != 0m)
    throw new InvalidOperationException("Base-backed card lifecycle conservation failed.");

SemanticId walletLotId = InScope("held-position", "wallet-lot", "paid-one");
SemanticId WalletOperation(string local) => InScope("held-position", "operation", local);
DateTimeOffset walletNow = DateTimeOffset.UnixEpoch.AddDays(10);
WalletLotState walletState = WalletLotState.Create(new WalletLot(walletLotId, 10, "usd", WalletLotOriginKind.Paid,
    walletNow.AddDays(-5), walletNow.AddDays(5), OwnerGeneration.Create(1)), WalletOperation("create"))
    .Apply(WalletLotChangeKind.Reserve, 6, WalletOperation("reserve"), walletNow)
    .Apply(WalletLotChangeKind.Consume, 2, WalletOperation("consume"), walletNow)
    .Apply(WalletLotChangeKind.RetainResidue, 1, WalletOperation("residue"), walletNow)
    .Apply(WalletLotChangeKind.Release, 3, WalletOperation("release"), walletNow, nonOccurrenceProven: true)
    .Apply(WalletLotChangeKind.CorrectCredit, 2, WalletOperation("correction"), walletNow)
    .Apply(WalletLotChangeKind.TransferOut, 4, WalletOperation("transfer"), walletNow);
IOwnerPersistencePort<WalletLotState> walletPort = persistence.CreateOwnerPort(PaymentsWalletLotFactCodecs.State);
var walletOwner = new OwnerReference(FrozenAuthority.HeldPosition, walletLotId, OwnerGeneration.Create(1));
CanonicalDigest walletDigest = CanonicalDigest.Sha256(digestProfile, "wallet-lot-state"u8);
var walletRequest = new OwnerAppendRequest<WalletLotState>(walletOwner, walletDigest, DomainIn("held-position"), walletState);
OwnerAppendReceipt<WalletLotState> walletAppend = await walletPort.CompareBindAppendAsync(walletRequest);
OwnerAppendReceipt<WalletLotState> walletReplay = await walletPort.CompareBindAppendAsync(walletRequest);
OwnerHistoryPage<WalletLotState> walletHistory = await walletPort.ReadHistoryAsync(new OwnerHistoryRequest(walletAppend.Owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
        NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(walletAppend.Owner)]), 8));
if (walletAppend.Disposition != OwnerAppendDisposition.Appended || walletReplay.Disposition != OwnerAppendDisposition.Replay || walletHistory.Facts.Count != 1 ||
    walletHistory.Facts[0].TotalCredited != 12 || walletHistory.Facts[0].Available != 5 || walletHistory.Facts[0].Consumed != 2 ||
    walletHistory.Facts[0].TransferredOut != 4 || walletHistory.Facts[0].Residual != 1)
    throw new InvalidOperationException("Base-backed RES-009 wallet lot conservation failed.");

SemanticId entitlementSubject = InScope("entitlement-grant-removal", "account", "subject-one");
SemanticId EntitlementId(string kind, string local) => InScope("entitlement-grant-removal", kind, local);
var entitlementState = EntitlementRestrictionState.Create(entitlementSubject, OwnerGeneration.Create(1));
var entitlementValue = CanonicalDigest.Sha256(digestProfile, "premium"u8);
var entitlementGrantId = EntitlementId("fact", "grant");
entitlementState = entitlementState.Apply(new EntitlementCommand(entitlementGrantId, entitlementSubject, "premium", entitlementValue,
    EntitlementId("evidence", "agreement"), EntitlementOperation.Grant, EntitlementPrecedence.Initial, entitlementState.Generation,
    NamedTime.Create(TimeKind.Effective, DateTimeOffset.UnixEpoch.AddSeconds(10))), DateTimeOffset.UnixEpoch.AddSeconds(5));
var entitlementRestrictionId = EntitlementId("fact", "overdue-restriction");
var entitlementOwner = EntitlementId("owner", "collections");
entitlementState = entitlementState.Apply(new RestrictionCommand(entitlementRestrictionId, entitlementSubject, entitlementOwner, "service-access",
    EntitlementId("evidence", "overdue"), RestrictionOperation.Restrict, entitlementState.Generation,
    NamedTime.Create(TimeKind.Effective, DateTimeOffset.UnixEpoch.AddSeconds(20))), DateTimeOffset.UnixEpoch.AddSeconds(15));
IOwnerPersistencePort<EntitlementRestrictionState> entitlementPort = persistence.CreateOwnerPort(PaymentsEntitlementRestrictionFactCodecs.State);
var entitlementOwnerReference = new OwnerReference(FrozenAuthority.EntitlementGrantRemovalFact, entitlementSubject, OwnerGeneration.Create(1));
var entitlementRequest = new OwnerAppendRequest<EntitlementRestrictionState>(entitlementOwnerReference,
    CanonicalDigest.Sha256(digestProfile, "entitlement-restriction"u8), DomainIn("entitlement-grant-removal"), entitlementState);
var entitlementAppend = await entitlementPort.CompareBindAppendAsync(entitlementRequest);
var entitlementReplay = await entitlementPort.CompareBindAppendAsync(entitlementRequest);
var entitlementHistory = await entitlementPort.ReadHistoryAsync(new OwnerHistoryRequest(entitlementAppend.Owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch),
        [new HPD.Payments.Supporting.History.OwnerCut(entitlementAppend.Owner)]), 8));
if (entitlementAppend.Disposition != OwnerAppendDisposition.Appended || entitlementReplay.Disposition != OwnerAppendDisposition.Replay ||
    entitlementHistory.Facts.Count != 1 || entitlementHistory.Facts[0].Resolve("premium", "service-access", DateTimeOffset.UnixEpoch.AddSeconds(21),
        DateTimeOffset.UnixEpoch.AddSeconds(21), TimeSpan.FromSeconds(10), EnforcementFailMode.Closed).Kind != EnforcementDecisionKind.Deny)
    throw new InvalidOperationException("Base-backed entitlement/restriction temporal projection failed.");

SemanticId durableWorkId = InScope("work-requirement", "work", "execution-one");
DurableExecutionSnapshot durableSnapshot = DurableExecutionSnapshot.Restore(durableWorkId, WorkDisposition.RetryRequired, 2,
    OwnerGeneration.Create(2), true, PublicationDisposition.RedeliveryRequired, 2, false,
    GovernedRepairState.ClosedWithResidue, 1);
IOwnerPersistencePort<DurableExecutionSnapshot> durablePort = persistence.CreateOwnerPort(PaymentsDurableExecutionFactCodecs.Snapshot);
var durableOwner = new OwnerReference(FrozenAuthority.WorkRequirement, durableWorkId, OwnerGeneration.Create(1));
var durableRequest = new OwnerAppendRequest<DurableExecutionSnapshot>(durableOwner,
    CanonicalDigest.Sha256(digestProfile, "durable-execution"u8), DomainIn("work-requirement"), durableSnapshot);
var durableAppend = await durablePort.CompareBindAppendAsync(durableRequest);
var durableReplay = await durablePort.CompareBindAppendAsync(durableRequest);
var durableHistory = await durablePort.ReadHistoryAsync(new OwnerHistoryRequest(durableAppend.Owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch),
        [new HPD.Payments.Supporting.History.OwnerCut(durableAppend.Owner)]), 8));
if (durableAppend.Disposition != OwnerAppendDisposition.Appended || durableReplay.Disposition != OwnerAppendDisposition.Replay ||
    durableHistory.Facts.Count != 1 || !durableHistory.Facts[0].WorkRequiresReconciliation ||
    durableHistory.Facts[0].PublicationDisposition != PublicationDisposition.RedeliveryRequired ||
    durableHistory.Facts[0].RepairState != GovernedRepairState.ClosedWithResidue)
    throw new InvalidOperationException("Base-backed durable work/publication/repair projection failed.");

SemanticId settlementMovementId = InScope("value-movement", "movement", "payout-one");
SemanticId SettlementEvidence(string local) => InScope("value-movement", "settlement-evidence", local);
SettlementAccountingState settlementState = SettlementAccountingState.Create(settlementMovementId, 100m, "usd",
    OwnerGeneration.Create(1), SettlementEvidence("expected"))
    .Observe(SettlementAccountingObservationKind.Included, SettlementEvidence("included"), 100m)
    .Observe(SettlementAccountingObservationKind.AccountingAcknowledged, SettlementEvidence("accounting-export"));
IOwnerPersistencePort<SettlementAccountingState> settlementPort = persistence.CreateOwnerPort(PaymentsSettlementAccountingFactCodecs.State);
var settlementOwner = new OwnerReference(FrozenAuthority.ValueMovement, settlementMovementId, OwnerGeneration.Create(1));
var settlementRequest = new OwnerAppendRequest<SettlementAccountingState>(settlementOwner,
    CanonicalDigest.Sha256(digestProfile, "settlement-accounting"u8), DomainIn("value-movement"), settlementState);
var settlementAppend = await settlementPort.CompareBindAppendAsync(settlementRequest);
var settlementReplay = await settlementPort.CompareBindAppendAsync(settlementRequest);
var settlementHistory = await settlementPort.ReadHistoryAsync(new OwnerHistoryRequest(settlementAppend.Owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch),
        [new HPD.Payments.Supporting.History.OwnerCut(settlementAppend.Owner)]), 8));
if (settlementAppend.Disposition != OwnerAppendDisposition.Appended || settlementReplay.Disposition != OwnerAppendDisposition.Replay ||
    settlementHistory.Facts.Count != 1 || settlementHistory.Facts[0].IncludedMagnitude != 100m ||
    !settlementHistory.Facts[0].AccountingAcknowledged || settlementHistory.Facts[0].Residual)
    throw new InvalidOperationException("Base-backed settlement/accounting projection failed.");

SemanticId cutoverId = InScope("work-requirement", "cutover", "embedded-distributed");
SemanticId cutoverOwnerId = InScope("work-requirement", "owner", "history-one");
CanonicalDigest cutoverFactOne = CanonicalDigest.Sha256(digestProfile, "cutover-one"u8);
CanonicalDigest cutoverFactTwo = CanonicalDigest.Sha256(digestProfile, "cutover-two"u8);
CutoverHistoryEntry[] cutoverHistory =
[
    new(cutoverOwnerId, OwnerGeneration.Create(1), cutoverFactOne),
    new(cutoverOwnerId, OwnerGeneration.Create(2), cutoverFactTwo),
];
DistributedCutoverProtocol cutoverState = DistributedCutoverProtocol.Plan(cutoverId,
    InScope("work-requirement", "profile", "embedded"), InScope("work-requirement", "profile", "distributed"))
    .BeginDualRead(cutoverHistory, cutoverHistory, OwnerGeneration.Create(1))
    .Promote(cutoverHistory, cutoverHistory, OwnerGeneration.Create(2), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(10))
    .Complete(cutoverHistory, cutoverHistory, OwnerGeneration.Create(2), residuePresent: false);
IOwnerPersistencePort<DistributedCutoverProtocol> cutoverPort = persistence.CreateOwnerPort(DistributedCutoverFactCodecs.State);
var cutoverOwner = new OwnerReference(FrozenAuthority.WorkRequirement, cutoverId, OwnerGeneration.Create(1));
var cutoverRequest = new OwnerAppendRequest<DistributedCutoverProtocol>(cutoverOwner,
    CanonicalDigest.Sha256(digestProfile, "embedded-distributed-cutover"u8), DomainIn("work-requirement"), cutoverState);
var cutoverAppend = await cutoverPort.CompareBindAppendAsync(cutoverRequest);
var cutoverReplay = await cutoverPort.CompareBindAppendAsync(cutoverRequest);
var cutoverStoredHistory = await cutoverPort.ReadHistoryAsync(new OwnerHistoryRequest(cutoverAppend.Owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch),
        [new HPD.Payments.Supporting.History.OwnerCut(cutoverAppend.Owner)]), 8));
if (cutoverAppend.Disposition != OwnerAppendDisposition.Appended || cutoverReplay.Disposition != OwnerAppendDisposition.Replay ||
    cutoverStoredHistory.Facts.Count != 1 || cutoverStoredHistory.Facts[0].State != DistributedCutoverState.Completed ||
    cutoverStoredHistory.Facts[0].ComparedThrough != OwnerGeneration.Create(2))
    throw new InvalidOperationException("Base-backed embedded/distributed cutover projection failed.");

#if PAYMENTS_SQLITE
Console.WriteLine("Payments L51/07S SQLite bridge passed through L5-14K embedded/distributed cutover.");
#else
Console.WriteLine("Payments L51/07I integration passed through L5-14K embedded/distributed cutover.");
#endif

static void RequireGeneration<TFact>(OwnerAppendReceipt<TFact> receipt, ulong expected) where TFact : notnull
{
    if (receipt.ObservedGeneration.Value != expected) throw new InvalidOperationException("Base-backed owner generation was incorrect.");
}

static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class AllowPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
}

sealed record BridgeFact(string Id, decimal Amount);

[JsonSerializable(typeof(BridgeFact))]
internal sealed partial class BridgeJsonContext : JsonSerializerContext;

#if PAYMENTS_SQLITE
sealed class SqliteDatabaseCleanup(string path) : IDisposable
{
    public void Dispose()
    {
        foreach (string suffix in new[] { "", "-shm", "-wal" })
        {
            string candidate = path + suffix;
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }
}
#endif
