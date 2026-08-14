using HPD.Payments.Contracts.ScopedIdentity;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.ScopedIdentity;

internal static class ScopedIdentityContractProofs
{
    internal static void RunAll()
    {
        EveryRouteIsIndependentlyAddressable();
        ReplayConflictRetirementAndMissingEvidenceRemainDistinct();
        CommandsRejectDefaultsAndWrongTimeCoordinates();
        ProviderAndTenantScopesCannotCollide();
    }

    private static void EveryRouteIsIndependentlyAddressable()
    {
        var routes = Enum.GetValues<ScopedIdentityRoute>().Where(static value => value != ScopedIdentityRoute.None).ToArray();
        Equal(29, routes.Length);
        Equal(29, routes.Distinct().Count());
        foreach (var route in routes)
            _ = new ScopedIdentityCommand(route, ScopedIdentityOperation.Reserve, Identity("tenant-a", "live", route.ToString().ToLowerInvariant()), Digest("meaning"), OwnerGeneration.Create(1), Revision.Create("authorization", 3), Time(TimeKind.Requested, 1));
    }

    private static void ReplayConflictRetirementAndMissingEvidenceRemainDistinct()
    {
        var id = Identity("tenant-a", "live", "subject");
        var digest = Digest("meaning-a");
        var reservation = new ScopedIdentityReservation(id, digest, OwnerGeneration.Create(1), Time(TimeKind.Accepted, 2), Time(TimeKind.Record, 3));
        Equal(ResultKind.Success, ScopedIdentityComparison.Compare(id, Digest("meaning-a"), reservation, null).Kind);
        Equal(ResultKind.Conflict, ScopedIdentityComparison.Compare(id, Digest("meaning-b"), reservation, null).Kind);
        Equal(ResultKind.Indeterminate, ScopedIdentityComparison.Compare(id, digest, null, null).Kind);
        var tombstone = new ScopedIdentityTombstone(id, digest, OwnerGeneration.Create(2), Time(TimeKind.Record, 4));
        Equal(ResultKind.Superseded, ScopedIdentityComparison.Compare(id, digest, reservation, tombstone).Kind);
    }

    private static void CommandsRejectDefaultsAndWrongTimeCoordinates()
    {
        Throws<ArgumentException>(() => _ = new ScopedIdentityCommand(ScopedIdentityRoute.None, ScopedIdentityOperation.Reserve, Identity("tenant-a", "live", "subject"), Digest("meaning"), OwnerGeneration.Create(1), Revision.Create("authorization", 1), Time(TimeKind.Requested, 1)));
        Throws<ArgumentException>(() => _ = new ScopedIdentityCommand(ScopedIdentityRoute.Paym001, ScopedIdentityOperation.None, Identity("tenant-a", "live", "subject"), Digest("meaning"), OwnerGeneration.Create(1), Revision.Create("authorization", 1), Time(TimeKind.Requested, 1)));
        Throws<ArgumentException>(() => _ = new ScopedIdentityCommand(ScopedIdentityRoute.Paym001, ScopedIdentityOperation.Reserve, Identity("tenant-a", "live", "subject"), Digest("meaning"), OwnerGeneration.Create(1), Revision.Create("authorization", 1), Time(TimeKind.Record, 1)));
    }

    private static void ProviderAndTenantScopesCannotCollide()
    {
        var tenantA = Identity("tenant-a", "live", "subject");
        var tenantB = Identity("tenant-b", "live", "subject");
        var providerA = SemanticId.Create(tenantA.Scope, tenantA.Namespace, tenantA.Kind, tenantA.LocalId, "stripe", "acct-a");
        var providerB = SemanticId.Create(tenantA.Scope, tenantA.Namespace, tenantA.Kind, tenantA.LocalId, "stripe", "acct-b");
        False(tenantA == tenantB);
        False(providerA == providerB);
    }

    private static SemanticId Identity(string tenant, string environment, string localId) => SemanticId.Create(ScopeId.Create(tenant, environment, "scoped-identity"), "authority", "subject", localId);
    private static CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(new CanonicalDigestProfileId("scoped-identity", ContractVersion.Create(1, 0), "semantic-fields", "none", "utc-v1", "ordered", "sha256-keyless"), System.Text.Encoding.UTF8.GetBytes(value));
    private static NamedTime Time(TimeKind kind, long seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static void Equal<T>(T expected, T actual) where T : notnull { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void False(bool value) { if (value) throw new InvalidOperationException("Expected false."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
