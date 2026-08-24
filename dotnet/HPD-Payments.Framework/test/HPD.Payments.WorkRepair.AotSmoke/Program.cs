using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.DurableWork;
using HPD.Payments.Runtime.Publication;

var scope = ScopeId.Create("tenant", "aot", "work-repair");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "runtime", kind, local);
var profile = new CanonicalDigestProfileId("runtime", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
var digest = CanonicalDigest.Sha256(profile, "payload"u8);
var requirement = new WorkRequirementFact(Id("work", "one"), Id("fact", "owner"), digest, ContractVersion.Create(1, 0),
    Revision.Create("deployment", 1), NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 2);
var claimed = WorkProtocolState.Create(requirement).TryClaim("worker-a", NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(10))).State;
var expired = claimed.ExpireClaim(NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch.AddSeconds(10))).State;
if (!expired.RequiresReconciliation || expired.TryClaim("worker-b", NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(20))).Accepted)
    return 1;
var retryable = expired.Reconcile(false).State;
var second = retryable.TryClaim("worker-b", NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(20))).State;
if (second.ClaimEpoch.Value != 2 || second.Observe(OwnerGeneration.Create(1), WorkAttemptObservation.OwnerPostconditionVerified).Accepted)
    return 1;
var obligation = new PublicationObligationFact(Id("publication", "one"), Id("fact", "source"), "merchant", "events", digest,
    ContractVersion.Create(1, 0), NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch));
var sent = PublicationProtocolState.Create(obligation).Dispatch(Id("delivery", "one")).State;
if (!sent.AwaitingReconciliation || sent.Dispatch(Id("delivery", "two")).Accepted ||
    sent.Reconcile(false, false).State.Disposition != PublicationDisposition.RedeliveryRequired)
    return 1;
Console.WriteLine("PASS durable work/publication Native AOT fencing graph");
return 0;
