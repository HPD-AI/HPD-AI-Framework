using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Custody;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Runtime.Tests.Custody;

internal static class CustodyProtocolProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scope = ScopeId.Create("tenant", "runtime", "custody");
        SemanticId Id(string kind, string local) => SemanticId.Create(scope, "runtime", kind, local);
        var owner = new OwnerReference(FrozenAuthority.Obligation, Id("obligation", "one"), OwnerGeneration.Create(1));
        var mark = ClassificationMark.Create(DataClassification.Restricted, RetentionKind.Durable);
        CustodyInstance Instance(ulong generation, CustodyState state, TimeKind time = TimeKind.Observed) => new(
            Id("instance", "backup-one"), owner, Id("controller", "backup"), OwnerGeneration.Create(generation), mark,
            Revision.Create("policy", 1), Revision.Create("hold", 1), state,
            NamedTime.Create(time, DateTimeOffset.UnixEpoch.AddSeconds(generation)));

        var held = CustodyProtocol.Create(Instance(1, CustodyState.Held));
        Check(!held.Observe(Instance(2, CustodyState.Requested)).Accepted, "held instance admitted deletion request");
        var eligible = CustodyProtocol.Create(Instance(1, CustodyState.Eligible));
        Check(!eligible.Observe(Instance(3, CustodyState.Requested)).Accepted, "custody generation skipped");
        var requested = eligible.Observe(Instance(2, CustodyState.Requested));
        var absent = requested.Protocol.Observe(Instance(3, CustodyState.VerifiedAbsent, TimeKind.Verify));
        Check(absent.Accepted && absent.Protocol.MaySweep(OwnerGeneration.Create(3)), "verified absence was not sweepable at exact cut");
        Check(!absent.Protocol.MaySweep(OwnerGeneration.Create(2)), "verified absence swept before inventory cut");
        Check(!absent.Protocol.Observe(Instance(4, CustodyState.Residual)).Accepted,
            "verified-absent instance reopened as anything except known present");
        var restored = absent.Protocol.Observe(Instance(4, CustodyState.KnownPresent));
        Check(restored.Accepted && !restored.Protocol.MaySweep(OwnerGeneration.Create(4)),
            "restore did not reopen per-instance custody");

        var failedDeletion = requested.Protocol.Observe(Instance(3, CustodyState.Residual));
        Check(failedDeletion.Accepted && !failedDeletion.Protocol.MaySweep(OwnerGeneration.Create(3)),
            "failed deletion residue was treated as absence");
    }
}
