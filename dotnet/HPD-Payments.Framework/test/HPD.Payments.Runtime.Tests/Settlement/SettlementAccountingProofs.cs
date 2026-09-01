using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Settlement;

namespace HPD.Payments.Runtime.Tests.Settlement;

internal static class SettlementAccountingProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scope = ScopeId.Create("tenant", "runtime", "settlement");
        SemanticId Id(string kind, string local) => SemanticId.Create(scope, "settlement", kind, local);
        var expected = SettlementAccountingState.Create(Id("movement", "one"), 100m, "usd", OwnerGeneration.Create(1), Id("evidence", "expected"));
        var mismatch = expected.Observe(SettlementAccountingObservationKind.Included, Id("evidence", "included-90"), 90m);
        Check(mismatch.Residual && !mismatch.AccountingAcknowledged, "payout mismatch was flattened or exported");
        Throws(() => mismatch.Observe(SettlementAccountingObservationKind.AccountingAcknowledged, Id("evidence", "bad-export")), failures,
            "mismatched settlement was acknowledged to accounting");
        var included = expected.Observe(SettlementAccountingObservationKind.Included, Id("evidence", "included-100"), 100m);
        var acknowledged = included.Observe(SettlementAccountingObservationKind.AccountingAcknowledged, Id("evidence", "export"));
        Check(acknowledged.AccountingAcknowledged && !acknowledged.Residual, "matching settlement export did not close locally");
        var excluded = expected.Observe(SettlementAccountingObservationKind.Excluded, Id("evidence", "excluded"));
        Check(excluded.Excluded && excluded.Residual, "settlement exclusion did not retain mismatch");
        Throws(() => expected.Observe(SettlementAccountingObservationKind.Included, Id("evidence", "unauthenticated"), 100m, false), failures,
            "unauthenticated settlement evidence was admitted");
    }

    private static void Throws(Action action, List<string> failures, string message)
    { try { action(); } catch (InvalidOperationException) { return; } failures.Add(message); }
}
