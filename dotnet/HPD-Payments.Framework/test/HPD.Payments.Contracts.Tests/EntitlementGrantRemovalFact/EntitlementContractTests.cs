using HPD.Payments.Contracts.EntitlementGrantRemovalFact;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.EntitlementGrantRemovalFact;

/// <summary>Executes partition-local entitlement fact invariants when registered by the shared Contracts runner.</summary>
public static class EntitlementContractTests
{
    /// <summary>Checks append-only lineage, interval, scope, generation, result, and default-invalid behavior.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "entitlement");
        var other = ScopeId.Create("tenant-b", "live", "entitlement");
        var subject = Id(scope, "subjects", "account", "a1");
        var provenance = Id(scope, "evidence", "agreement-fact", "p1");
        var factId = Id(scope, "entitlements", "fact", "g1");
        var profile = Profile("entitlement-fact");
        var value = CanonicalDigest.Sha256(profile, "premium"u8);
        var command = new EntitlementCommand(factId, subject, "premium", value, provenance,
            EntitlementOperation.Grant, EntitlementPrecedence.Initial, OwnerGeneration.Create(4), At(TimeKind.Effective, 10));
        var admitted = new EntitlementFact(command, OwnerGeneration.Create(5), CanonicalDigest.Sha256(profile, "fact"u8), At(TimeKind.Record, 12));
        Equal(ResultKind.Success, EntitlementResults.Admitted(admitted).Kind);
        Equal(ResultKind.Indeterminate, EntitlementResults.Indeterminate("evidence-stale").Kind);

        var removal = new EntitlementCommand(Id(scope, "entitlements", "fact", "r1"), subject, "premium", value, provenance,
            EntitlementOperation.Remove, EntitlementPrecedence.Removal, OwnerGeneration.Create(5), At(TimeKind.Effective, 20),
            predecessorFactId: factId);
        Equal(EntitlementOperation.Remove, removal.Operation);

        Throws<ArgumentException>(() => Consume(new EntitlementCommand(factId, subject, "INVALID FEATURE", value, provenance,
            EntitlementOperation.Grant, EntitlementPrecedence.Initial, OwnerGeneration.Create(4), At(TimeKind.Effective, 10))));
        Throws<ArgumentException>(() => Consume(new EntitlementCommand(Id(other, "entitlements", "fact", "g2"), subject, "premium", value, provenance,
            EntitlementOperation.Grant, EntitlementPrecedence.Initial, OwnerGeneration.Create(4), At(TimeKind.Effective, 10))));
        Throws<ArgumentException>(() => Consume(new EntitlementCommand(factId, subject, "premium", value, provenance,
            EntitlementOperation.Remove, EntitlementPrecedence.Removal, OwnerGeneration.Create(4), At(TimeKind.Effective, 10))));
        Throws<ArgumentException>(() => Consume(new EntitlementCommand(factId, subject, "premium", value, provenance,
            EntitlementOperation.Grant, EntitlementPrecedence.Initial, OwnerGeneration.Create(4), At(TimeKind.Effective, 10), At(TimeKind.Effective, 9))));
        Throws<ArgumentException>(() => Consume(new EntitlementFact(command, OwnerGeneration.Create(6), value, At(TimeKind.Record, 12))));
        Throws<ArgumentException>(() => Consume(new EntitlementFact(command, OwnerGeneration.Create(5), value, At(TimeKind.Effective, 12))));
        Throws<ArgumentException>(() => EntitlementResults.Conflict("INVALID CODE"));
    }

    private static SemanticId Id(ScopeId scope, string ns, string kind, string value) => SemanticId.Create(scope, ns, kind, value);
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigestProfileId Profile(string discriminator) => new(discriminator, ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless");
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
