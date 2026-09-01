using HPD.Payments.Contracts.EntitlementGrantRemovalFact;
using HPD.Payments.Contracts.RestrictionFact;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Entitlement;

namespace HPD.Payments.Runtime.Tests.Entitlement;

internal static class EntitlementRestrictionProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scope = ScopeId.Create("tenant", "runtime", "entitlement");
        SemanticId Id(string ns, string kind, string value) => SemanticId.Create(scope, ns, kind, value);
        NamedTime At(long seconds) => NamedTime.Create(TimeKind.Effective, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
        var subject = Id("subject", "account", "one"); var provenance = Id("evidence", "agreement", "one");
        var owner = Id("owner", "restriction", "collections"); var cause = Id("evidence", "overdue", "one");
        var digest = CanonicalDigest.Sha256(new("entitlement", ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless"), "premium"u8);
        var grantId = Id("entitlement", "fact", "grant");
        var state = EntitlementRestrictionState.Create(subject, OwnerGeneration.Create(1));
        state = state.Apply(new EntitlementCommand(grantId, subject, "premium", digest, provenance, EntitlementOperation.Grant,
            EntitlementPrecedence.Initial, state.Generation, At(10)), DateTimeOffset.UnixEpoch.AddSeconds(5));
        Check(state.Resolve("premium", "service-access", DateTimeOffset.UnixEpoch.AddSeconds(12), DateTimeOffset.UnixEpoch.AddSeconds(12),
            TimeSpan.FromSeconds(10), EnforcementFailMode.Closed).Kind == EnforcementDecisionKind.Allow, "effective grant did not allow");
        var restrictionId = Id("restriction", "fact", "restrict");
        state = state.Apply(new RestrictionCommand(restrictionId, subject, owner, "service-access", cause, RestrictionOperation.Restrict,
            state.Generation, At(20)), DateTimeOffset.UnixEpoch.AddSeconds(15));
        Check(state.Resolve("premium", "service-access", DateTimeOffset.UnixEpoch.AddSeconds(19), DateTimeOffset.UnixEpoch.AddSeconds(19),
            TimeSpan.FromSeconds(10), EnforcementFailMode.Closed).Kind == EnforcementDecisionKind.Allow, "future restriction activated early");
        Check(state.Resolve("premium", "service-access", DateTimeOffset.UnixEpoch.AddSeconds(21), DateTimeOffset.UnixEpoch.AddSeconds(21),
            TimeSpan.FromSeconds(10), EnforcementFailMode.Closed).Kind == EnforcementDecisionKind.Deny, "effective overdue restriction did not deny");
        var releaseId = Id("restriction", "fact", "release");
        state = state.Apply(new RestrictionCommand(releaseId, subject, owner, "service-access", cause, RestrictionOperation.Release,
            state.Generation, At(30), predecessorFactId: restrictionId, predecessorOwnerId: owner), DateTimeOffset.UnixEpoch.AddSeconds(25));
        Check(state.Resolve("premium", "service-access", DateTimeOffset.UnixEpoch.AddSeconds(31), DateTimeOffset.UnixEpoch.AddSeconds(31),
            TimeSpan.FromSeconds(10), EnforcementFailMode.Closed).Kind == EnforcementDecisionKind.Allow, "release did not preserve grant while clearing its own restriction");
        Check(state.Resolve("premium", "service-access", DateTimeOffset.UnixEpoch.AddSeconds(31), DateTimeOffset.UnixEpoch.AddSeconds(60),
            TimeSpan.FromSeconds(10), EnforcementFailMode.Closed).Kind == EnforcementDecisionKind.Indeterminate, "stale evidence did not fail indeterminate");
        Throws(() => state.Apply(new RestrictionCommand(Id("restriction", "fact", "stale"), subject, owner, "service-access", cause,
            RestrictionOperation.Restrict, OwnerGeneration.Create(1), At(40)), DateTimeOffset.UnixEpoch.AddSeconds(35)), failures, "stale restriction generation admitted");
    }

    private static void Throws(Action action, List<string> failures, string message)
    { try { action(); } catch (InvalidOperationException) { return; } failures.Add(message); }
}
