using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.DurableWork;

namespace HPD.Payments.Runtime.Tests.DurableWork;

internal static class WorkProtocolProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scope = ScopeId.Create("tenant", "runtime", "work-protocol");
        SemanticId Id(string kind, string local) => SemanticId.Create(scope, "runtime", kind, local);
        var profile = new CanonicalDigestProfileId("runtime", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
        CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));
        NamedTime Expiry(long seconds) => NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
        var requirement = new WorkRequirementFact(Id("work", "one"), Id("fact", "owner"), Digest("payload"),
            ContractVersion.Create(1, 0), Revision.Create("deployment", 1),
            NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 2);

        var initial = WorkProtocolState.Create(requirement);
        var claim1 = initial.TryClaim("worker-a", Expiry(10));
        Check(claim1.Accepted && claim1.State.ClaimEpoch.Value == 1, "first claim epoch was not one");
        Check(!claim1.State.TryClaim("worker-b", Expiry(10)).Accepted, "active claim was stolen");

        var returned = claim1.State.Observe(claim1.State.ClaimEpoch, WorkAttemptObservation.HandlerReturned);
        Check(returned.Accepted && returned.State.RequiresReconciliation && returned.State.Disposition == WorkDisposition.RetryRequired,
            "handler return was treated as verified owner success");
        Check(!returned.State.TryClaim("worker-b", Expiry(20)).Accepted, "indeterminate attempt was blindly retried");

        var reconciled = returned.State.Reconcile(false);
        var claim2 = reconciled.State.TryClaim("worker-b", Expiry(20));
        Check(claim2.Accepted && claim2.State.ClaimEpoch.Value == 2, "second claim did not advance epoch");
        Check(!claim2.State.Observe(OwnerGeneration.Create(1), WorkAttemptObservation.OwnerPostconditionVerified).Accepted,
            "stale worker committed after takeover");

        var exhausted = claim2.State.Observe(claim2.State.ClaimEpoch, WorkAttemptObservation.DefiniteFailure);
        Check(exhausted.Accepted && exhausted.State.Disposition == WorkDisposition.Exhausted,
            "attempt budget did not produce explicit exhaustion");
        Check(!exhausted.State.TryClaim("worker-c", Expiry(30)).Accepted, "exhausted work was reclaimed");

        var verifiedClaim = WorkProtocolState.Create(requirement).TryClaim("worker-a", Expiry(10));
        var verified = verifiedClaim.State.Observe(verifiedClaim.State.ClaimEpoch, WorkAttemptObservation.OwnerPostconditionVerified);
        Check(verified.State.Disposition == WorkDisposition.Verified && !verified.State.RequiresReconciliation,
            "fresh owner evidence did not terminate verified");

        var poisonClaim = WorkProtocolState.Create(requirement).TryClaim("worker-a", Expiry(10));
        var poison = poisonClaim.State.Observe(poisonClaim.State.ClaimEpoch, WorkAttemptObservation.Poison);
        Check(poison.State.Disposition == WorkDisposition.Poison, "poison was flattened into retry");
    }
}
