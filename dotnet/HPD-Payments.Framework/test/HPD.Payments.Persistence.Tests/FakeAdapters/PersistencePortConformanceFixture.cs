using System.Runtime.CompilerServices;
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

namespace HPD.Payments.Persistence.Tests.FakeAdapters;

internal static class PersistencePortConformanceFixture
{
    [ModuleInitializer]
    internal static void Run()
    {
        var fixture = new FixtureValues();
        ExerciseOwnerShapes(fixture);
        ExerciseSupportingShapes(fixture);
        ExerciseNonCertification(fixture);
        Console.WriteLine($"L5-03R fake-adapter conformance passed: {4} receipt domains.");
    }

    private static void ExerciseOwnerShapes(FixtureValues fixture)
    {
        var adapter = new FakeOwnerAdapter();
        var request = new OwnerAppendRequest<WorkRequirementFact>(fixture.Owner, fixture.Digest, fixture.Local, fixture.Fact);

        var appended = adapter.CompareBindAppendAsync(request).AsTask().GetAwaiter().GetResult();
        Require(appended.Disposition == OwnerAppendDisposition.Appended && ReferenceEquals(appended.Fact, fixture.Fact), "append shape lost the authority fact");

        var replay = adapter.CompareBindAppendAsync(request).AsTask().GetAwaiter().GetResult();
        Require(replay.Disposition == OwnerAppendDisposition.Replay && ReferenceEquals(replay.Fact, fixture.Fact), "replay shape was flattened");

        var staleOwner = new OwnerReference(fixture.Owner.Authority, fixture.Owner.SubjectId, OwnerGeneration.Create(2));
        var conflictRequest = new OwnerAppendRequest<WorkRequirementFact>(staleOwner, fixture.Digest, fixture.Local, fixture.Fact);
        var conflict = adapter.CompareBindAppendAsync(conflictRequest).AsTask().GetAwaiter().GetResult();
        Require(conflict.Disposition == OwnerAppendDisposition.Conflict && conflict.Fact is null, "conflict shape was promoted to an append");

        var frame = new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, fixture.RecordedAt, [new HPD.Payments.Supporting.History.OwnerCut(fixture.Owner)]);
        var page = adapter.ReadHistoryAsync(new OwnerHistoryRequest(fixture.Owner, frame, 1)).AsTask().GetAwaiter().GetResult();
        Require(page.Facts.Count == 1 && ReferenceEquals(page.Facts[0], fixture.Fact), "history shape lost the exact authority fact");
        Require(page.Continuation.Span.SequenceEqual("next"u8), "history continuation was not preserved");
        Require(!ReferenceEquals(page.Facts, adapter.MutableHistory), "history exposed fake-adapter mutable storage");
    }

    private static void ExerciseSupportingShapes(FixtureValues fixture)
    {
        var adapter = new FakeSupportingAdapter(fixture.Digest, fixture.ObservedAt);
        var target = new OwnerReference(FrozenAuthority.PublicationObligation, fixture.Id("publication", "p1"), fixture.Owner.Generation);
        var relation = new SupportingRelation(fixture.Id("relation", "r1"), SupportingRelationKind.DerivedFrom, fixture.Owner, target, Revision.Create("relation", 1));
        var relationReceipt = adapter.GuardedRelateAsync(relation, fixture.DistributedRelation).AsTask().GetAwaiter().GetResult();
        Require(relationReceipt.SubjectId == relation.RelationId, "relation receipt lost its exact subject");
        Require(relationReceipt.Observation == PersistenceObservation.Unsupported, "uncertified D-REL fake overclaimed support");

        var declaration = new ContinuationDeclaration(fixture.Owner, fixture.Id("continuation", "c1"), fixture.Digest);
        var continuationReceipt = adapter.CommitDiscoverableAsync(declaration, fixture.DistributedContinuation).AsTask().GetAwaiter().GetResult();
        Require(continuationReceipt.Observation == PersistenceObservation.Untested, "uncertified D-CONT fake overclaimed support");
        var discovery = adapter.DiscoverAsync(fixture.DistributedContinuation, 1).AsTask().GetAwaiter().GetResult();
        Require(discovery.Items.Count == 1 && discovery.Items[0].ContinuationId == declaration.ContinuationId, "continuation discovery shape lost its declaration");
        Require(discovery.Continuation.Span.SequenceEqual("cont"u8), "continuation discovery token was not preserved");

        var custody = new CustodyInstance(fixture.Id("custody", "copy1"), fixture.Owner, fixture.Id("controller", "store1"), fixture.Owner.Generation,
            ClassificationMark.Create(DataClassification.Restricted, RetentionKind.Durable), Revision.Create("retention", 1), Revision.Create("holds", 1),
            CustodyState.Residual, fixture.ObservedAt);
        var custodyReceipt = adapter.RecordCustodyAsync(custody, fixture.Local).AsTask().GetAwaiter().GetResult();
        Require(custodyReceipt.Observation == PersistenceObservation.Indeterminate, "custody uncertainty was flattened");
        var custodyPage = adapter.ReadCustodyAsync(fixture.Owner, fixture.Owner.Generation, 1).AsTask().GetAwaiter().GetResult();
        Require(custodyPage.Items.Count == 1 && custodyPage.Items[0].State == CustodyState.Residual, "custody residue was erased");
    }

    private static void ExerciseNonCertification(FixtureValues fixture)
    {
        foreach (var domain in new[] { fixture.Local, fixture.DistributedOwner, fixture.DistributedRelation, fixture.DistributedContinuation })
        {
            var receipt = new AtomicDomainReceipt(domain, "fixture-probe", PersistenceObservation.Untested, fixture.ObservedAt, fixture.Digest, "fake-not-certification");
            Require(receipt.Observation == PersistenceObservation.Untested, "fake receipt certified an E/D guarantee");
            Require(receipt.Limitation == "fake-not-certification", "fake receipt omitted its certification limitation");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"L5-03R: {message}.");
    }

    private sealed class FixtureValues
    {
        internal ScopeId Scope { get; } = ScopeId.Create("tenant", "conformance", "persistence");
        internal SemanticId Id(string kind, string local) => SemanticId.Create(Scope, "l5-03r", kind, local);
        internal OwnerReference Owner { get; }
        internal CanonicalDigest Digest { get; }
        internal WorkRequirementFact Fact { get; }
        internal NamedTime RecordedAt { get; } = NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch);
        internal NamedTime ObservedAt { get; } = NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch);
        internal AtomicDomain Local { get; }
        internal AtomicDomain DistributedOwner { get; }
        internal AtomicDomain DistributedRelation { get; }
        internal AtomicDomain DistributedContinuation { get; }

        internal FixtureValues()
        {
            Owner = new OwnerReference(FrozenAuthority.WorkRequirement, Id("work", "w1"), OwnerGeneration.Create(1));
            var profile = new CanonicalDigestProfileId("persistence", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
            Digest = CanonicalDigest.Sha256(profile, "fact"u8);
            Fact = new WorkRequirementFact(Owner.SubjectId, Id("fact", "source"), Digest, ContractVersion.Create(1, 0), Revision.Create("deployment", 1), NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 3);
            Local = Domain("local", AtomicDomainKind.Local);
            DistributedOwner = Domain("owner", AtomicDomainKind.DistributedOwner);
            DistributedRelation = Domain("relation", AtomicDomainKind.DistributedRelation);
            DistributedContinuation = Domain("continuation", AtomicDomainKind.DistributedContinuation);
        }

        private AtomicDomain Domain(string local, AtomicDomainKind kind) => new(Id("domain", local), kind, Revision.Create("topology", 1));
    }

    private sealed class FakeOwnerAdapter : IOwnerPersistencePort<WorkRequirementFact>
    {
        private WorkRequirementFact? _fact;
        internal List<WorkRequirementFact> MutableHistory { get; } = [];

        public ValueTask<OwnerAppendReceipt<WorkRequirementFact>> CompareBindAppendAsync(OwnerAppendRequest<WorkRequirementFact> request, CancellationToken cancellationToken = default)
        {
            var disposition = request.ExpectedOwner.Generation.Value != 1
                ? OwnerAppendDisposition.Conflict
                : _fact is null ? OwnerAppendDisposition.Appended : OwnerAppendDisposition.Replay;
            if (disposition == OwnerAppendDisposition.Appended) { _fact = request.Fact; MutableHistory.Add(request.Fact); }
            return ValueTask.FromResult(new OwnerAppendReceipt<WorkRequirementFact>(request.ExpectedOwner, disposition, request.ExpectedOwner.Generation,
                disposition is OwnerAppendDisposition.Appended or OwnerAppendDisposition.Replay ? _fact : null,
                disposition switch
                {
                    OwnerAppendDisposition.Appended => "appended",
                    OwnerAppendDisposition.Replay => "replay",
                    _ => "conflict",
                }));
        }

        public ValueTask<OwnerHistoryPage<WorkRequirementFact>> ReadHistoryAsync(OwnerHistoryRequest request, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OwnerHistoryPage<WorkRequirementFact>(MutableHistory.ToArray(), request.Owner.Generation, "next"u8));
    }

    private sealed class FakeSupportingAdapter(CanonicalDigest digest, NamedTime observedAt) : IRelationPersistencePort, IContinuationPersistencePort, ICustodyPersistencePort
    {
        private ContinuationDeclaration? _continuation;
        private CustodyInstance? _custody;
        private AtomicDomainReceipt DomainReceipt(AtomicDomain domain, string operation, PersistenceObservation observation, string limitation) => new(domain, operation, observation, observedAt, digest, limitation);

        public ValueTask<PersistenceReceipt> GuardedRelateAsync(SupportingRelation relation, AtomicDomain domain, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PersistenceReceipt(relation.RelationId, PersistenceObservation.Unsupported, DomainReceipt(domain, "guarded-relate", PersistenceObservation.Unsupported, "fake-not-certification")));

        public ValueTask<PersistenceReceipt> CommitDiscoverableAsync(ContinuationDeclaration continuation, AtomicDomain domain, CancellationToken cancellationToken = default)
        {
            _continuation = continuation;
            return ValueTask.FromResult(new PersistenceReceipt(continuation.ContinuationId, PersistenceObservation.Untested, DomainReceipt(domain, "commit-discoverable", PersistenceObservation.Untested, "fake-not-certification")));
        }

        public ValueTask<ContinuationDiscoveryPage> DiscoverAsync(AtomicDomain domain, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ContinuationDiscoveryPage(_continuation is null ? [] : [_continuation], "cont"u8));

        public ValueTask<PersistenceReceipt> RecordCustodyAsync(CustodyInstance custody, AtomicDomain domain, CancellationToken cancellationToken = default)
        {
            _custody = custody;
            return ValueTask.FromResult(new PersistenceReceipt(custody.InstanceId, PersistenceObservation.Indeterminate, DomainReceipt(domain, "record-custody", PersistenceObservation.Indeterminate, "fake-not-certification")));
        }

        public ValueTask<CustodyPage> ReadCustodyAsync(OwnerReference owner, OwnerGeneration throughGeneration, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CustodyPage(_custody is null ? [] : [_custody]));
    }
}
