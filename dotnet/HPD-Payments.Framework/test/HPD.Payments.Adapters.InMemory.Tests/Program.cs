using HPD.Payments.Adapters.InMemory;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.History;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;

var tests = new (string Name, Func<Task> Run)[]
{
    ("compare-bind-race-generation-history", CompareBindRaceGenerationHistory),
    ("continuation-and-request-binding", ContinuationAndRequestBinding),
    ("guarded-relation-custody-residue-sweep", GuardedRelationCustodyResidueSweep),
    ("unsupported-domain", UnsupportedDomain),
    ("death-and-restore", DeathAndRestore),
    ("conservation-h7", ConservationH7),
};
foreach (var test in tests) { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
Console.WriteLine($"PASS InMemory E-LOCAL reference ({tests.Length} groups)");

static async Task CompareBindRaceGenerationHistory()
{
    var store = new InMemoryPersistenceStore(); var port = store.CreateOwnerPort<TestFact>(); var owner = Owner(1); var domain = Domain(AtomicDomainKind.Local);
    var a = new OwnerAppendRequest<TestFact>(owner, Digest("a"), domain, new("a", 10));
    var b = new OwnerAppendRequest<TestFact>(owner, Digest("b"), domain, new("b", 20));
    var outcomes = await Task.WhenAll(Task.Run(async () => await port.CompareBindAppendAsync(a)), Task.Run(async () => await port.CompareBindAppendAsync(b)));
    Assert(outcomes.Count(x => x.Disposition == OwnerAppendDisposition.Appended) == 1, "one race winner");
    Assert(outcomes.Count(x => x.Disposition == OwnerAppendDisposition.Conflict) == 1, "one race loser");
    var winner = outcomes.Single(x => x.Disposition == OwnerAppendDisposition.Appended);
    var replayRequest = winner.Fact == a.Fact ? a : b;
    Assert((await port.CompareBindAppendAsync(replayRequest)).Disposition == OwnerAppendDisposition.Replay, "digest replay");
    var nextOwner = new OwnerReference(owner.Authority, owner.SubjectId, winner.ObservedGeneration);
    var second = await port.CompareBindAppendAsync(new(nextOwner, Digest("c"), domain, new("c", 30)));
    Assert(second.Disposition == OwnerAppendDisposition.Appended && second.ObservedGeneration.Value == 3, "monotonic generation");
    var frame = new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(new(owner.Authority, owner.SubjectId, second.ObservedGeneration))]);
    var request = new OwnerHistoryRequest(new(owner.Authority, owner.SubjectId, second.ObservedGeneration), frame, 1);
    var page1 = await port.ReadHistoryAsync(request); var page2 = await port.ReadHistoryAsync(request, page1.Continuation);
    Assert(page1.Facts.Count == 1 && page2.Facts.Count == 1 && page2.Continuation.IsEmpty, "bounded complete history");
    Assert(page1.Facts.Sum(x => x.Amount) + page2.Facts.Sum(x => x.Amount) == winner.Fact!.Amount + 30, "history conservation");
}

static async Task ContinuationAndRequestBinding()
{
    var store = new InMemoryPersistenceStore(); var port = store.CreateOwnerPort<TestFact>(); var domain = Domain(AtomicDomainKind.Local); var owner = Owner(1);
    var seed = await port.CompareBindAppendAsync(new(owner, Digest("seed"), domain, new("seed", 1)));
    var current = new OwnerReference(owner.Authority, owner.SubjectId, seed.ObservedGeneration);
    for (var i = 0; i < 3; i++) await store.CommitDiscoverableAsync(new(current, Id("continuation", $"c{i}"), Digest($"c{i}")), domain);
    var page = await store.DiscoverAsync(domain, 2); Assert(page.Items.Count == 2 && !page.Continuation.IsEmpty, "continuation page");
    Assert((await store.DiscoverAsync(domain, 2, page.Continuation)).Items.Count == 1, "continuation recovery");
    await Throws<ArgumentException>(async () => await store.DiscoverAsync(domain, 1, page.Continuation));
}

static async Task UnsupportedDomain()
{
    var store = new InMemoryPersistenceStore(); var owner = Owner(1); var port = store.CreateOwnerPort<TestFact>();
    var receipt = await port.CompareBindAppendAsync(new(owner, Digest("x"), Domain(AtomicDomainKind.DistributedOwner), new("x", 1)));
    Assert(receipt.Disposition == OwnerAppendDisposition.Unsupported, "distributed owner explicit unsupported");
}

static async Task GuardedRelationCustodyResidueSweep()
{
    var store = new InMemoryPersistenceStore(); var port = store.CreateOwnerPort<TestFact>(); var domain = Domain(AtomicDomainKind.Local);
    var left = Owner(1); var rightInitial = new OwnerReference(FrozenAuthority.Obligation, Id("owner", "two"), OwnerGeneration.Create(1));
    var leftReceipt = await port.CompareBindAppendAsync(new(left, Digest("left"), domain, new("left", 1)));
    var rightReceipt = await port.CompareBindAppendAsync(new(rightInitial, Digest("right"), domain, new("right", 1)));
    var leftCurrent = new OwnerReference(left.Authority, left.SubjectId, leftReceipt.ObservedGeneration);
    var rightCurrent = new OwnerReference(rightInitial.Authority, rightInitial.SubjectId, rightReceipt.ObservedGeneration);
    var relation = new SupportingRelation(Id("relation", "one"), SupportingRelationKind.Application, leftCurrent, rightCurrent, Revision.Create("relation", 1));
    Assert((await store.GuardedRelateAsync(relation, domain)).Observation == PersistenceObservation.Observed, "guarded common-domain relation");
    var stale = new SupportingRelation(Id("relation", "stale"), SupportingRelationKind.Match, left, rightCurrent, Revision.Create("relation", 1));
    Assert((await store.GuardedRelateAsync(stale, domain)).Observation == PersistenceObservation.Failed, "stale endpoint rejected");
    var mark = ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable);
    var residual = new CustodyInstance(Id("custody", "residual"), leftCurrent, Id("controller", "one"), OwnerGeneration.Create(1), mark, Revision.Create("policy", 1), Revision.Create("hold", 1), CustodyState.Residual, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch));
    var absent = new CustodyInstance(Id("custody", "absent"), leftCurrent, Id("controller", "one"), OwnerGeneration.Create(1), mark, Revision.Create("policy", 1), Revision.Create("hold", 1), CustodyState.VerifiedAbsent, NamedTime.Create(TimeKind.Verify, DateTimeOffset.UnixEpoch));
    Assert((await store.RecordCustodyAsync(residual, domain)).DomainReceipt.Limitation == "residue-retained", "residue named");
    await store.RecordCustodyAsync(absent, domain);
    Assert(store.SweepVerifiedAbsent(OwnerGeneration.Create(1)) == 1, "only verified absence swept");
    var page = await store.ReadCustodyAsync(leftCurrent, OwnerGeneration.Create(1), 10);
    Assert(page.Items.Count == 1 && page.Items[0].State == CustodyState.Residual, "residue preserved");
}

static async Task DeathAndRestore()
{
    var store = new InMemoryPersistenceStore(); var port = store.CreateOwnerPort<TestFact>(); var owner = Owner(1); var domain = Domain(AtomicDomainKind.Local);
    await port.CompareBindAppendAsync(new(owner, Digest("before"), domain, new("before", 7))); var snapshot = store.CaptureSnapshot(); store.SimulateDeath();
    await Throws<InvalidOperationException>(async () => await port.CompareBindAppendAsync(new(owner, Digest("during"), domain, new("during", 8))));
    store.Restore(snapshot);
    Assert((await port.CompareBindAppendAsync(new(owner, Digest("before"), domain, new("before", 7)))).Disposition == OwnerAppendDisposition.Replay, "restored replay truth");
}

static async Task ConservationH7()
{
    var store = new InMemoryPersistenceStore(); var port = store.CreateOwnerPort<TestFact>(); var owner = Owner(1); var domain = Domain(AtomicDomainKind.Local);
    var requests = Enumerable.Range(0, 32).Select(i => new OwnerAppendRequest<TestFact>(owner, Digest($"race-{i}"), domain, new($"f{i}", i + 1))).ToArray();
    var receipts = await Task.WhenAll(requests.Select(x => Task.Run(async () => await port.CompareBindAppendAsync(x))));
    Assert(receipts.Count(x => x.Disposition == OwnerAppendDisposition.Appended) == 1, "H7 single compare-bind winner");
    Assert(receipts.Where(x => x.Disposition == OwnerAppendDisposition.Appended).Sum(x => x.Fact!.Amount) == receipts.Single(x => x.Disposition == OwnerAppendDisposition.Appended).Fact!.Amount, "no duplicated value");
}

static ScopeId Scope() => ScopeId.Create("tenant", "test", "payments");
static SemanticId Id(string kind, string local) => SemanticId.Create(Scope(), "test", kind, local);
static OwnerReference Owner(ulong generation) => new(FrozenAuthority.MeasuredFact, Id("owner", "one"), OwnerGeneration.Create(generation));
static AtomicDomain Domain(AtomicDomainKind kind) => new(Id("domain", kind == AtomicDomainKind.Local ? "local" : "distributed"), kind, Revision.Create("topology", 1));
static CanonicalDigest Digest(string text) => CanonicalDigest.Sha256(new("test", ContractVersion.Create(1, 0), "all", "ordinal", "utc", "canonical", "test"), System.Text.Encoding.UTF8.GetBytes(text));
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}"); }
internal sealed record TestFact(string Id, decimal Amount);
