using System.Text;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.Testing;
using HPD.Payments.Adapters.InMemory;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Base;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.History;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;

await using BaseTestHost host = await BaseTestHost.CreateAsync(ConfigureBase);
var principal = new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "payments-certifier",
    CurrentTenantId = "tenant-certification",
};
BaseSession session = host.Session(principal, options => options.Audience = HPDBaseEndpointAudience.ControlPlane);
var persistence = new BaseInMemoryPaymentsPersistence(session);
var ownerPort = persistence.CreateOwnerPort(new PaymentsFactJsonCodec<CertificationFact>(
    "l5-09i-certification-fact-v1", CertificationJsonContext.Default.CertificationFact));
BaseSupportingPersistencePort supporting = persistence.Supporting;

ScopeId scope = ScopeId.Create("tenant-certification", "l5-09i", "inmemory");
AtomicDomain local = new(Id("domain", "local"), AtomicDomainKind.Local, Revision.Create("topology", 1));
AtomicDomain distributed = new(Id("domain", "distributed"), AtomicDomainKind.DistributedOwner, Revision.Create("topology", 1));
CanonicalDigestProfileId digestProfile = new("l5-09i", ContractVersion.Create(1, 0), "all", "ordinal", "utc", "canonical", "certification");

SemanticId Id(string kind, string localId) => SemanticId.Create(scope, "certification", kind, localId);
CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(digestProfile, Encoding.UTF8.GetBytes(value));
OwnerReference Initial(string localId, FrozenAuthority authority = FrozenAuthority.MeasuredFact) =>
    new(authority, Id("owner", localId), OwnerGeneration.Create(1));

// H7 before-commit: rollback is visible as Indeterminate and leaves no fact behind.
OwnerReference rollbackOwner = Initial("rollback");
var rollbackRequest = new OwnerAppendRequest<CertificationFact>(rollbackOwner, Digest("rollback"), local, new("rollback", 1));
host.Faults.FailNextAtomicCommit();
OwnerAppendReceipt<CertificationFact> rollback = await ownerPort.CompareBindAppendAsync(rollbackRequest);
Require(rollback.Disposition == OwnerAppendDisposition.Indeterminate && rollback.ObservedGeneration == rollbackOwner.Generation,
    "A failed atomic commit was flattened or leaked a generation.");
await ThrowsAsync<KeyNotFoundException>(() => History(ownerPort, rollbackOwner, 8));
OwnerAppendReceipt<CertificationFact> afterRollback = await ownerPort.CompareBindAppendAsync(rollbackRequest);
Require(afterRollback.Disposition == OwnerAppendDisposition.Appended && afterRollback.ObservedGeneration.Value == 2,
    "Retry after confirmed rollback did not append exactly once.");

// H7 after-commit/before-ack: ambiguity remains Indeterminate, then exact replay converges.
OwnerReference ambiguousOwner = Initial("ambiguous");
var ambiguousRequest = new OwnerAppendRequest<CertificationFact>(ambiguousOwner, Digest("ambiguous"), local, new("ambiguous", 2));
host.Faults.MakeNextAtomicCommitIndeterminate();
OwnerAppendReceipt<CertificationFact> ambiguous = await ownerPort.CompareBindAppendAsync(ambiguousRequest);
Require(ambiguous.Disposition == OwnerAppendDisposition.Indeterminate && ambiguous.ObservedGeneration.Value == 2,
    "Committed response loss was flattened or lost its observed generation.");
OwnerAppendReceipt<CertificationFact> ambiguousReplay = await ownerPort.CompareBindAppendAsync(ambiguousRequest);
Require(ambiguousReplay.Disposition == OwnerAppendDisposition.Replay && ambiguousReplay.Fact == ambiguousRequest.Fact,
    "Exact retry after ambiguous commit did not converge as replay.");

// Identity substitution and unauthorized graph/domain use fail closed.
OwnerAppendReceipt<CertificationFact> substitution = await ownerPort.CompareBindAppendAsync(
    new(ambiguousRequest.ExpectedOwner, ambiguousRequest.SemanticDigest, ambiguousRequest.Domain,
        new CertificationFact("substitution", 999)));
Require(substitution.Disposition == OwnerAppendDisposition.Conflict, "Digest-bound payload substitution was accepted.");
Require((await ownerPort.CompareBindAppendAsync(new(Initial("distributed"), Digest("distributed"), distributed,
    new CertificationFact("distributed", 1)))).Disposition == OwnerAppendDisposition.Unsupported,
    "InMemory claimed a distributed atomic domain.");
using var cancelledSource = new CancellationTokenSource();
await cancelledSource.CancelAsync();
await ThrowsAsync<OperationCanceledException>(() => ownerPort.CompareBindAppendAsync(
    new(Initial("cancelled"), Digest("cancelled"), local, new CertificationFact("cancelled", 1)), cancelledSource.Token).AsTask());

var intruder = new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "intruder",
    CurrentTenantId = "tenant-certification",
};
var intruderPort = new BaseInMemoryPaymentsPersistence(host.Session(intruder,
    options => options.Audience = HPDBaseEndpointAudience.ControlPlane)).CreateOwnerPort(
        new PaymentsFactJsonCodec<CertificationFact>("l5-09i-certification-fact-v1", CertificationJsonContext.Default.CertificationFact));
OwnerAppendReceipt<CertificationFact> denied = await intruderPort.CompareBindAppendAsync(
    new(Initial("denied"), Digest("denied"), local, new CertificationFact("denied", 1)));
Require(denied.Disposition is OwnerAppendDisposition.Rejected or OwnerAppendDisposition.Unsupported,
    "An ungranted principal mutated Payments state.");

// Deterministic compare-bind race: one append, all other contenders conflict.
OwnerReference raceOwner = Initial("race");
OwnerAppendReceipt<CertificationFact>[] race = await Task.WhenAll(Enumerable.Range(0, 64).Select(index =>
    ownerPort.CompareBindAppendAsync(new(raceOwner, Digest("race-" + index), local,
        new CertificationFact("race-" + index, index))).AsTask()));
Require(race.Count(item => item.Disposition == OwnerAppendDisposition.Appended) == 1
    && race.Count(item => item.Disposition == OwnerAppendDisposition.Conflict) == 63,
    "The 64-way compare-bind race did not conserve exactly one winner.");

// History is ordered/bounded and a continuation must be bound to its exact owner request.
OwnerReference ownerA = Initial("history-a");
OwnerAppendReceipt<CertificationFact> a1 = await ownerPort.CompareBindAppendAsync(new(ownerA, Digest("a1"), local, new("a1", 1)));
OwnerReference ownerA2 = a1.Owner;
OwnerAppendReceipt<CertificationFact> a2 = await ownerPort.CompareBindAppendAsync(new(ownerA2, Digest("a2"), local, new("a2", 2)));
OwnerReference ownerB = Initial("history-b");
OwnerAppendReceipt<CertificationFact> b1 = await ownerPort.CompareBindAppendAsync(new(ownerB, Digest("b1"), local, new("b1", 1)));
OwnerAppendReceipt<CertificationFact> b2 = await ownerPort.CompareBindAppendAsync(new(b1.Owner, Digest("b2"), local, new("b2", 2)));
OwnerHistoryPage<CertificationFact> aPage = await ownerPort.ReadHistoryAsync(Request(a2.Owner, 1));
Require(aPage.Facts.SequenceEqual([new CertificationFact("a1", 1)]) && !aPage.Continuation.IsEmpty,
    "Bounded history did not return the first ordered fact.");
await ThrowsAsync<ArgumentException>(() => ownerPort.ReadHistoryAsync(Request(b2.Owner, 1), aPage.Continuation).AsTask());
await ThrowsAsync<ArgumentException>(() => ownerPort.ReadHistoryAsync(Request(a2.Owner, 2), aPage.Continuation).AsTask());

// Endpoint guards, custody monotonicity/jumps, replay and residue are Payments laws.
OwnerReference left = a2.Owner;
OwnerReference rightInitial = Initial("right", FrozenAuthority.Obligation);
OwnerAppendReceipt<CertificationFact> rightAppend = await ownerPort.CompareBindAppendAsync(
    new(rightInitial, Digest("right"), local, new("right", 1)));
var relation = new SupportingRelation(Id("relation", "valid"), SupportingRelationKind.Application,
    left, rightAppend.Owner, Revision.Create("relation", 1));
Require((await supporting.GuardedRelateAsync(relation, local)).Observation == PersistenceObservation.Observed,
    "A relation with exact current endpoints was rejected.");
var staleRelation = new SupportingRelation(Id("relation", "stale"), SupportingRelationKind.Match,
    ownerA, rightAppend.Owner, Revision.Create("relation", 1));
Require((await supporting.GuardedRelateAsync(staleRelation, local)).Observation == PersistenceObservation.Failed,
    "A relation with a stale endpoint was accepted.");

ClassificationMark mark = ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable);
CustodyInstance Custody(ulong generation, CustodyState state, int revision) => new(Id("custody", "instance"), left,
    Id("controller", "one"), OwnerGeneration.Create(generation), mark, Revision.Create("policy", 1),
    Revision.Create("hold", 1), state, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch.AddMinutes(revision)));
CustodyInstance custodyOne = Custody(1, CustodyState.Held, 1);
CustodyInstance custodyJump = Custody(7, CustodyState.Residual, 7);
Require((await supporting.RecordCustodyAsync(custodyOne, local)).Observation == PersistenceObservation.Observed,
    "Initial custody was rejected.");
Require((await supporting.RecordCustodyAsync(custodyJump, local)).DomainReceipt.Limitation == "residue-retained",
    "A valid monotone custody jump or residue was lost.");
Require((await supporting.RecordCustodyAsync(custodyJump, local)).Observation == PersistenceObservation.Observed,
    "Exact custody replay did not converge.");
Require((await supporting.RecordCustodyAsync(Custody(3, CustodyState.VerifiedAbsent, 3), local)).Observation == PersistenceObservation.Failed,
    "A regressing custody generation was accepted.");
CustodyPage custodyPage = await supporting.ReadCustodyAsync(left, OwnerGeneration.Create(7), 8);
Require(custodyPage.Items.Count == 1 && custodyPage.Items[0].State == CustodyState.Residual,
    "Latest residue was not preserved as custody truth.");

BaseAdministrationCapability lifecycle = host.GetRequiredService<IHPDBaseAdministration>().Capability;
Require(!lifecycle.Backup && !lifecycle.Restore && !lifecycle.Durable && lifecycle.AdministrativePurge,
    "InMemory lifecycle/provenance claims were overstated.");

Console.WriteLine("L5-09I independent certification passed: rollback/ambiguity H7, replay, auth/domain rejection, 64-way conservation, request-bound history, endpoint guards, custody monotonicity/residue, and volatile lifecycle.");

OwnerHistoryRequest Request(OwnerReference owner, int maximum) => new(owner,
    new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
        NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(owner)]), maximum);
Task History(IOwnerPersistencePort<CertificationFact> port, OwnerReference owner, int maximum) =>
    port.ReadHistoryAsync(Request(owner, maximum)).AsTask();

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void ConfigureBase(HPDBaseBuilder builder)
{
    AddOperationGrant(builder, "hpd.payments.owner-fact.append");
    AddOperationGrant(builder, "hpd.payments.relation.persist");
    AddOperationGrant(builder, "hpd.payments.continuation.persist");
    AddOperationGrant(builder, "hpd.payments.custody.persist");
    AddSourceGrant(builder, "hpd.payments.owner-fact.source", PaymentsOwnerFactEvent.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.owner-fact-head.source", PaymentsOwnerFactHead.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.relation.source", PaymentsRelationRecord.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.continuation.source", PaymentsContinuationRecord.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.custody.source", PaymentsCustodyRecord.Collection.Id);
    builder.AddPaymentsOwnerFactPersistence();
    builder.AddPaymentsSupportingPersistence();
}

static void AddOperationGrant(HPDBaseBuilder builder, string grantId) => builder.AddStaticGrantAuthority(
    new BaseGrantAuthorityDefinition { Id = grantId, Version = 1, OwningModuleId = "hpd.payments",
        SourceContractId = "hpd.payments.certification.grants", SourceContractVersion = 1 },
    new AccessGrant { Id = grantId, ApplicationId = "hpd.base.application", ModuleId = "hpd.payments",
        Audience = HPDBaseEndpointAudience.ControlPlane,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-certifier", TenantId = "tenant-certification" },
        Action = grantId, Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-certification" } });

static void AddSourceGrant(HPDBaseBuilder builder, string grantId, string collectionId) => builder.AddStaticGrantAuthority(
    new BaseGrantAuthorityDefinition { Id = grantId, Version = 1, OwningModuleId = "hpd.payments",
        SourceContractId = "hpd.payments.certification.grants", SourceContractVersion = 1 },
    new AccessGrant { Id = grantId, ApplicationId = "hpd.base.application", ModuleId = "hpd.payments",
        Audience = HPDBaseEndpointAudience.ControlPlane,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-certifier", TenantId = "tenant-certification" },
        Action = collectionId, Scope = new ResourceScope { Kind = ResourceScopeKind.Collection,
            CollectionId = collectionId, TenantId = "tenant-certification" } });

sealed record CertificationFact(string Id, int Amount);

[JsonSerializable(typeof(CertificationFact))]
internal sealed partial class CertificationJsonContext : JsonSerializerContext;
