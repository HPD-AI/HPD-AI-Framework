using System.Diagnostics;
using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Custody;
using HPD.Payments.Runtime.ExternalEffects;
using HPD.Payments.Runtime.Publication;
using HPD.Payments.Runtime.DurableWork;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Ownership;

const int WarmupIterations = 1_024;
const int MeasuredIterations = 20_000;
var scope = ScopeId.Create("tenant", "perf", "static-lane");
SemanticId Id(string kind, string local, string? provider = null, string? account = null) =>
    SemanticId.Create(scope, "static", kind, local, provider, account);
var version = ContractVersion.Create(1, 0);
var profile = new CanonicalDigestProfileId("static", version, "fields", "ordinal", "utc", "ordered", "none");
CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));

var requirement = new WorkRequirementFact(Id("work", "one"), Id("fact", "owner"), Digest("work"), version,
    Revision.Create("deployment", 1), NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 2);
var obligation = new PublicationObligationFact(Id("publication", "one"), Id("fact", "source"), "merchant",
    "events", Digest("publication"), version, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch));
var operation = new ExternalEffectOperation(Id("operation", "capture"), Id("attempt", "one"),
    Id("account", "main", "simulator", "main"), "static-idempotency", Digest("request"),
    Revision.Create("credential", 1), Revision.Create("configuration", 1));
var owner = new OwnerReference(FrozenAuthority.Obligation, Id("obligation", "one"), OwnerGeneration.Create(1));
var custodyInstance = new CustodyInstance(Id("instance", "one"), owner, Id("controller", "one"),
    OwnerGeneration.Create(1), ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable),
    Revision.Create("policy", 1), Revision.Create("hold", 1), CustodyState.Eligible,
    NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch));

var observations = new[]
{
    Measure("work-claim-verify", () =>
    {
        var claimed = WorkProtocolState.Create(requirement).TryClaim("worker",
            NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddMinutes(1)));
        return (int)claimed.State.Observe(claimed.State.ClaimEpoch,
            WorkAttemptObservation.OwnerPostconditionVerified).State.Disposition;
    }),
    Measure("work-stale-claim-rejection", () =>
    {
        var claimed = WorkProtocolState.Create(requirement).TryClaim("worker",
            NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddMinutes(1)));
        var stale = claimed.State.Observe(OwnerGeneration.Create(claimed.State.ClaimEpoch.Value + 1),
            WorkAttemptObservation.OwnerPostconditionVerified);
        return stale.Accepted ? 1 : 2;
    }),
    Measure("work-indeterminate-reconciliation", () =>
    {
        var claimed = WorkProtocolState.Create(requirement).TryClaim("worker",
            NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddMinutes(1)));
        var uncertain = claimed.State.Observe(claimed.State.ClaimEpoch, WorkAttemptObservation.Indeterminate);
        return (int)uncertain.State.Reconcile(ownerPostconditionVerified: false).State.Disposition;
    }),
    Measure("publication-dispatch-ack", () =>
    {
        var dispatched = PublicationProtocolState.Create(obligation).Dispatch(Id("delivery", "one"));
        return (int)dispatched.State.Acknowledge(Id("delivery", "one")).State.Disposition;
    }),
    Measure("publication-ack-mismatch", () =>
    {
        var dispatched = PublicationProtocolState.Create(obligation).Dispatch(Id("delivery", "one"));
        return dispatched.State.Acknowledge(Id("delivery", "other")).Accepted ? 1 : 2;
    }),
    Measure("effect-possible-dispatch", () => (int)ExternalEffectProtocolState.Create(operation, Digest("initial"))
        .BeginDispatch(Digest("dispatch")).State.MarkPossibleDispatch(Digest("possible")).State.State),
    Measure("effect-unsafe-retry-rejection", () =>
    {
        var possible = ExternalEffectProtocolState.Create(operation, Digest("initial"))
            .BeginDispatch(Digest("dispatch")).State.MarkPossibleDispatch(Digest("possible"));
        return possible.State.BeginDispatch(Digest("retry")).Accepted ? 1 : 2;
    }),
    Measure("custody-materialization", () => CustodyProtocol.Create(custodyInstance).Current.InstanceId.IsValid ? 1 : 0),
};

foreach (var observation in observations)
    await Console.Out.WriteLineAsync($"{observation.Path}|iterations={observation.Iterations}|allocatedBytes={observation.AllocatedBytes}|bytesPerOperation={observation.BytesPerOperation:F4}|elapsedTicks={observation.ElapsedTicks}|checksum={observation.Checksum}")
        .ConfigureAwait(false);
return observations.All(static x => x.Iterations == MeasuredIterations && x.AllocatedBytes >= 0 && x.Checksum != 0) ? 0 : 1;

static Measurement Measure(string path, Func<int> action)
{
    var checksum = 0;
    for (var i = 0; i < WarmupIterations; i++) checksum = unchecked(checksum * 31 + action());
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timestamp = Stopwatch.GetTimestamp();
    for (var i = 0; i < MeasuredIterations; i++) checksum = unchecked(checksum * 31 + action());
    var elapsed = Stopwatch.GetTimestamp() - timestamp;
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    GC.KeepAlive(checksum);
    return new(path, MeasuredIterations, allocated, (double)allocated / MeasuredIterations, elapsed, checksum);
}

internal readonly record struct Measurement(string Path, int Iterations, long AllocatedBytes,
    double BytesPerOperation, long ElapsedTicks, int Checksum);
