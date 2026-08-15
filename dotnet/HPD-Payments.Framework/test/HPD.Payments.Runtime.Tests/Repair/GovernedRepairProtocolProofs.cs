using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Repair;
using HPD.Payments.Supporting.Evidence;
using HPD.Payments.Supporting.Operations;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Repair;

namespace HPD.Payments.Runtime.Tests.Repair;

internal static class GovernedRepairProtocolProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scope = ScopeId.Create("tenant", "runtime", "repair");
        SemanticId Id(string kind, string local) => SemanticId.Create(scope, "runtime", kind, local);
        var profile = new CanonicalDigestProfileId("runtime", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
        CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));
        var owner = new OwnerReference(FrozenAuthority.Obligation, Id("obligation", "one"), OwnerGeneration.Create(1));
        EvidenceDescriptor Evidence(string local, EvidenceRole role, TimeKind time) => new(
            Id("evidence", local), role, owner, Id("source", "operator"), Digest(local),
            ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable),
            NamedTime.Create(time, DateTimeOffset.UnixEpoch));
        OperationDeclaration Operation(string local)
        {
            var authorization = new AuthorizationDescriptor(Evidence($"auth-{local}", EvidenceRole.Authorization, TimeKind.Observed),
                "repair", Revision.Create("policy", 1), NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddHours(1)));
            return new OperationDeclaration(Id("operation", local), owner, Digest($"command-{local}"), authorization);
        }

        var branch1 = new RepairBranch(Id("branch", "one"), owner, Operation("one"), "owner-balanced");
        var branch2 = new RepairBranch(Id("branch", "two"), owner, Operation("two"), "effect-reconciled");
        var @case = new GovernedCase(Id("case", "one"), OwnerGeneration.Create(1), [branch1, branch2]);
        var initial = GovernedRepairProtocol.Create(@case);
        Check(!initial.Record(OwnerGeneration.Create(1), new(branch1.BranchId, RepairBranchTerminal.DefiniteFailure)).Accepted,
            "unapproved repair branch executed");
        Check(!initial.Approve(OwnerGeneration.Create(2), Revision.Create("approval", 1)).Accepted, "stale plan was approved");
        var approved = initial.Approve(OwnerGeneration.Create(1), Revision.Create("approval", 1));
        var first = approved.Protocol.Record(OwnerGeneration.Create(1), new(branch1.BranchId,
            RepairBranchTerminal.VerifiedPostcondition, Evidence("verify-one", EvidenceRole.Verification, TimeKind.Verify)));
        Check(first.Accepted && first.Protocol.State == GovernedRepairState.InProgress, "verified first branch did not remain in progress");
        Check(!first.Protocol.Record(OwnerGeneration.Create(1), new(branch1.BranchId, RepairBranchTerminal.DefiniteFailure)).Accepted,
            "terminal branch was overwritten");
        var closed = first.Protocol.Record(OwnerGeneration.Create(1), new(branch2.BranchId, RepairBranchTerminal.Residual, residueCode: "provider-residue"));
        Check(closed.Protocol.State == GovernedRepairState.ClosedWithResidue, "repair residue was flattened into success");

        var replacement = new GovernedCase(@case.CaseId, OwnerGeneration.Create(2), [branch1]);
        var reopened = closed.Protocol.Reopen(replacement);
        Check(reopened.Accepted && reopened.Protocol.State == GovernedRepairState.AwaitingApproval,
            "new plan generation did not reopen without inherited approval");
    }
}
