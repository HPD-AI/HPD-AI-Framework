using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.History;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;

var failures = new List<string>();
void Check(bool condition, string message) { if (!condition) failures.Add(message); }
void Reject(Action action, string message) { try { action(); failures.Add(message); } catch (ArgumentException) { } }

var scope = ScopeId.Create("tenant", "live", "work");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "persistence", kind, local);
var generation = OwnerGeneration.Create(1);
var owner = new OwnerReference(FrozenAuthority.WorkRequirement, Id("work", "w1"), generation);
var profile = new CanonicalDigestProfileId("persistence", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
var digest = CanonicalDigest.Sha256(profile, "fact"u8);
var local = new AtomicDomain(Id("domain", "local"), AtomicDomainKind.Local, Revision.Create("topology", 1));
var distributed = new AtomicDomain(Id("domain", "owner"), AtomicDomainKind.DistributedOwner, Revision.Create("topology", 1));
var fact = new WorkRequirementFact(owner.SubjectId, Id("fact", "source"), digest, ContractVersion.Create(1, 0), Revision.Create("deployment", 1), NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 3);
var request = new OwnerAppendRequest<WorkRequirementFact>(owner, digest, local, fact);
var fake = new FakeOwnerPort();
var appended = await fake.CompareBindAppendAsync(request).ConfigureAwait(false);
Check(appended.Disposition == OwnerAppendDisposition.Appended && ReferenceEquals(appended.Fact, fact), "Fake owner port lost the exact authority fact.");
var replay = await fake.CompareBindAppendAsync(request).ConfigureAwait(false);
Check(replay.Disposition == OwnerAppendDisposition.Replay, "Fake owner port did not expose replay.");
var conflictingOwner = new OwnerReference(owner.Authority, owner.SubjectId, OwnerGeneration.Create(2));
var conflict = await fake.CompareBindAppendAsync(new OwnerAppendRequest<WorkRequirementFact>(conflictingOwner, digest, local, fact)).ConfigureAwait(false);
Check(conflict.Disposition == OwnerAppendDisposition.Conflict, "Fake owner port flattened an owner-generation conflict.");

var frame = new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), new[] { new HPD.Payments.Supporting.History.OwnerCut(owner) });
var page = await fake.ReadHistoryAsync(new OwnerHistoryRequest(owner, frame, 1)).ConfigureAwait(false);
Check(page.Facts.Count == 1 && ReferenceEquals(page.Facts[0], fact), "Owner history lost the authority fact.");
Reject(() => _ = new OwnerHistoryRequest(owner, new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), new[] { new HPD.Payments.Supporting.History.OwnerCut(conflictingOwner) }), 1), "History admitted a frame without the exact owner cut.");

var target = new OwnerReference(FrozenAuthority.PublicationObligation, Id("publication", "p1"), generation);
var relation = new SupportingRelation(Id("relation", "r1"), SupportingRelationKind.DerivedFrom, owner, target, Revision.Create("relation", 1));
var supporting = new FakeSupportingPort(digest);
var relationReceipt = await supporting.GuardedRelateAsync(relation, distributed).ConfigureAwait(false);
Check(relationReceipt.Observation == PersistenceObservation.Unsupported, "Distributed relation fake overclaimed uncertified atomicity.");

var continuation = new ContinuationDeclaration(owner, Id("continuation", "c1"), digest);
var continuationReceipt = await supporting.CommitDiscoverableAsync(continuation, local).ConfigureAwait(false);
Check(continuationReceipt.Observation == PersistenceObservation.Observed, "Local continuation was not explicitly discoverable.");
var discovered = await supporting.DiscoverAsync(local, 8).ConfigureAwait(false);
Check(discovered.Items.Count == 1 && discovered.Items[0].ContinuationId == continuation.ContinuationId, "Continuation discovery lost its declaration.");

var custody = new CustodyInstance(Id("custody", "copy1"), owner, Id("controller", "store1"), generation,
    ClassificationMark.Create(DataClassification.Restricted, RetentionKind.Durable), Revision.Create("retention", 1), Revision.Create("holds", 1),
    CustodyState.Residual, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch));
var custodyReceipt = await supporting.RecordCustodyAsync(custody, local).ConfigureAwait(false);
Check(custodyReceipt.Observation == PersistenceObservation.Observed, "Custody observation was not retained.");
var custodyPage = await supporting.ReadCustodyAsync(owner, generation, 8).ConfigureAwait(false);
Check(custodyPage.Items.Count == 1 && custodyPage.Items[0].State == CustodyState.Residual, "Custody residue was flattened.");

var receipt = new AtomicDomainReceipt(local, "compare-bind", PersistenceObservation.Observed, NamedTime.Create(TimeKind.Verify, DateTimeOffset.UnixEpoch), digest, "none");
Check(receipt.Domain.Kind == AtomicDomainKind.Local, "Atomic receipt lost E-LOCAL scope.");
Check(new[] { AtomicDomainKind.Local, AtomicDomainKind.DistributedOwner, AtomicDomainKind.DistributedRelation, AtomicDomainKind.DistributedContinuation }.Distinct().Count() == 4,
    "Frozen E/D domain vocabulary is incomplete.");

var persistenceReferences = typeof(IOwnerPersistencePort<>).Assembly.GetReferencedAssemblies().Select(static x => x.Name).ToHashSet(StringComparer.Ordinal);
Check(persistenceReferences.Contains("HPD.Payments.Primitives") && persistenceReferences.Contains("HPD.Payments.Contracts") && persistenceReferences.Contains("HPD.Payments.Supporting"), "Persistence lacks an inward dependency.");
Check(!persistenceReferences.Any(static x => x is not null && (x.StartsWith("HPD.Payments.Adapters", StringComparison.Ordinal) || x.StartsWith("HPD.Payments.Connectors", StringComparison.Ordinal) || x == "HPD.Payments.Runtime")), "Persistence references an outward adapter/provider/runtime assembly.");
var closedOwnerPorts = typeof(IOwnerPersistencePort<>).Assembly.GetExportedTypes().Count(static x => x.IsInterface && x.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IOwnerPersistencePort<>)));
Check(closedOwnerPorts == 17, "Persistence does not expose exactly one closed fact port for each frozen authority.");
foreach (var inward in new[] { typeof(ScopeId).Assembly, typeof(WorkRequirementFact).Assembly, typeof(OwnerReference).Assembly })
    Check(!inward.GetReferencedAssemblies().Any(static x => x.Name == "HPD.Payments.Persistence"), $"{inward.GetName().Name} references Persistence in the forbidden direction.");

if (failures.Count != 0)
{
    foreach (var failure in failures) await Console.Error.WriteLineAsync(failure).ConfigureAwait(false);
    return 1;
}

Console.WriteLine($"Persistence port proofs passed: {Enum.GetValues<AtomicDomainKind>().Length - 1} frozen atomic domains and all port invariants.");
return 0;

sealed class FakeOwnerPort : IOwnerPersistencePort<WorkRequirementFact>
{
    private WorkRequirementFact? _fact;
    public ValueTask<OwnerAppendReceipt<WorkRequirementFact>> CompareBindAppendAsync(OwnerAppendRequest<WorkRequirementFact> request, CancellationToken cancellationToken = default)
    {
        var disposition = request.ExpectedOwner.Generation.Value != 1 ? OwnerAppendDisposition.Conflict : _fact is null ? OwnerAppendDisposition.Appended : OwnerAppendDisposition.Replay;
        _fact ??= disposition == OwnerAppendDisposition.Appended ? request.Fact : null;
        return ValueTask.FromResult(new OwnerAppendReceipt<WorkRequirementFact>(request.ExpectedOwner, disposition, request.ExpectedOwner.Generation,
            disposition is OwnerAppendDisposition.Appended or OwnerAppendDisposition.Replay ? _fact : null,
            disposition switch { OwnerAppendDisposition.Appended => "appended", OwnerAppendDisposition.Replay => "replay", _ => "conflict" }));
    }

    public ValueTask<OwnerHistoryPage<WorkRequirementFact>> ReadHistoryAsync(OwnerHistoryRequest request, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new OwnerHistoryPage<WorkRequirementFact>(new[] { _fact! }, request.Owner.Generation));
}

sealed class FakeSupportingPort : IRelationPersistencePort, IContinuationPersistencePort, ICustodyPersistencePort
{
    private readonly CanonicalDigest _digest;
    private ContinuationDeclaration? _continuation;
    private CustodyInstance? _custody;
    public FakeSupportingPort(CanonicalDigest digest) => _digest = digest;
    private AtomicDomainReceipt DomainReceipt(AtomicDomain domain, string operation, PersistenceObservation observation) =>
        new(domain, operation, observation, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch), _digest, observation == PersistenceObservation.Unsupported ? "uncertified" : "none");
    public ValueTask<PersistenceReceipt> GuardedRelateAsync(SupportingRelation relation, AtomicDomain domain, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PersistenceReceipt(relation.RelationId, PersistenceObservation.Unsupported, DomainReceipt(domain, "guarded-relate", PersistenceObservation.Unsupported)));
    public ValueTask<PersistenceReceipt> CommitDiscoverableAsync(ContinuationDeclaration continuation, AtomicDomain domain, CancellationToken cancellationToken = default)
    { _continuation = continuation; return ValueTask.FromResult(new PersistenceReceipt(continuation.ContinuationId, PersistenceObservation.Observed, DomainReceipt(domain, "commit-discoverable", PersistenceObservation.Observed))); }
    public ValueTask<ContinuationDiscoveryPage> DiscoverAsync(AtomicDomain domain, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ContinuationDiscoveryPage(_continuation is null ? [] : new[] { _continuation }));
    public ValueTask<PersistenceReceipt> RecordCustodyAsync(CustodyInstance custody, AtomicDomain domain, CancellationToken cancellationToken = default)
    { _custody = custody; return ValueTask.FromResult(new PersistenceReceipt(custody.InstanceId, PersistenceObservation.Observed, DomainReceipt(domain, "record-custody", PersistenceObservation.Observed))); }
    public ValueTask<CustodyPage> ReadCustodyAsync(OwnerReference owner, OwnerGeneration throughGeneration, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CustodyPage(_custody is null ? [] : new[] { _custody }));
}
