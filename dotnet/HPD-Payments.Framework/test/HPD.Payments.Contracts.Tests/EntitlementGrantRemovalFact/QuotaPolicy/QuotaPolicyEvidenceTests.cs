using HPD.Payments.Contracts.EntitlementGrantRemovalFact.QuotaPolicy;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Contracts.Tests.EntitlementGrantRemovalFact.QuotaPolicy;

internal static class QuotaPolicyEvidenceTests
{
    internal static void Run()
    {
        ScopeId scope = ScopeId.Create("tenant", "live", "quota");
        var subject = SemanticId.Create(scope, "quota", "subject", "one");
        var fact = SemanticId.Create(scope, "entitlement", "fact", "one");
        var profile = new CanonicalDigestProfileId("quota", ContractVersion.Create(1, 0), "all", "ordinal", "utc", "canonical", "test");
        var evidence = new QuotaEntitlementEvidence(subject, fact, CanonicalDigest.Sha256(profile, "grant"u8), "api-calls", "request",
            Revision.Create("policy", 1), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1));
        Equal(QuotaEligibilityKind.Eligible, evidence.Evaluate(DateTimeOffset.UnixEpoch.AddMinutes(1), true));
        Equal(QuotaEligibilityKind.Rejected, evidence.Evaluate(DateTimeOffset.UnixEpoch.AddHours(1), true));
        Equal(QuotaEligibilityKind.Rejected, evidence.Evaluate(DateTimeOffset.UnixEpoch.AddMinutes(1), false));
        Equal(QuotaEligibilityKind.Indeterminate, evidence.Evaluate(DateTimeOffset.UnixEpoch.AddMinutes(1), true, true));
        Throws<ArgumentException>(() => Consume(new QuotaEntitlementEvidence(subject, fact, evidence.EntitlementDigest, "api-calls", "USD",
            Revision.Create("policy", 1), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1))));
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException(); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException(); }
}
