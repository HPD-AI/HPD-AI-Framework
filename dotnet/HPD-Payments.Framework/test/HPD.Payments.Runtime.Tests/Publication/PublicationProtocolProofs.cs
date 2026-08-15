using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Publication;

namespace HPD.Payments.Runtime.Tests.Publication;

internal static class PublicationProtocolProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scope = ScopeId.Create("tenant", "runtime", "publication");
        SemanticId Id(string kind, string local) => SemanticId.Create(scope, "runtime", kind, local);
        var profile = new CanonicalDigestProfileId("runtime", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
        var digest = CanonicalDigest.Sha256(profile, "payload"u8);
        var obligation = new PublicationObligationFact(Id("publication", "one"), Id("fact", "source"), "merchant",
            "events", digest, ContractVersion.Create(1, 0), NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch));

        var initial = PublicationProtocolState.Create(obligation);
        var delivery1 = Id("delivery", "one");
        var attempted = initial.Dispatch(delivery1);
        Check(attempted.Accepted && attempted.State.AwaitingReconciliation, "dispatch did not retain acknowledgement uncertainty");
        Check(!attempted.State.Dispatch(Id("delivery", "two")).Accepted, "publication blindly redelivered");
        Check(!attempted.State.Acknowledge(Id("delivery", "wrong")).Accepted, "wrong delivery acknowledgement was admitted");
        var redelivery = attempted.State.Reconcile(false, false);
        Check(redelivery.State.Disposition == PublicationDisposition.RedeliveryRequired, "missing acknowledgement did not require redelivery");
        var attempted2 = redelivery.State.Dispatch(Id("delivery", "two"));
        var acknowledged = attempted2.State.Acknowledge(Id("delivery", "two"));
        Check(acknowledged.State.Disposition == PublicationDisposition.Acknowledged, "exact acknowledgement did not terminate");

        var residual = PublicationProtocolState.Create(obligation).RetainResidue();
        Check(residual.Accepted && residual.State.Disposition == PublicationDisposition.Residual, "recipient residue was flattened");
    }
}
