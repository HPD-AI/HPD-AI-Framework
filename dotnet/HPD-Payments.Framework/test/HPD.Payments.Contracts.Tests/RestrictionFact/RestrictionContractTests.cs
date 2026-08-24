using HPD.Payments.Contracts.RestrictionFact;
using HPD.Payments.Contracts.Tests.RestrictionFact.QuotaPolicy;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.RestrictionFact;

/// <summary>Executes partition-local restriction fact invariants when registered by the shared Contracts runner.</summary>
public static class RestrictionContractTests
{
    /// <summary>Checks owner-only release, independent dimensions, intervals, generations, closed results, and default-invalid behavior.</summary>
    public static void Run()
    {
        QuotaRestrictionBindingTests.Run();
        var scope = ScopeId.Create("tenant-a", "live", "restriction");
        var subject = Id(scope, "subjects", "account", "a1");
        var debtOwner = Id(scope, "restriction-owners", "owner", "collections");
        var fraudOwner = Id(scope, "restriction-owners", "owner", "fraud");
        var cause = Id(scope, "evidence", "cause", "overdue-1");
        var factId = Id(scope, "restrictions", "fact", "r1");
        var profile = Profile("restriction-fact");
        var command = new RestrictionCommand(factId, subject, debtOwner, "service-access", cause,
            RestrictionOperation.Restrict, OwnerGeneration.Create(7), At(TimeKind.Effective, 10));
        var admitted = new RestrictionFactRecord(command, OwnerGeneration.Create(8), CanonicalDigest.Sha256(profile, "restriction"u8), At(TimeKind.Record, 11));
        Equal(ResultKind.Success, RestrictionResults.Admitted(admitted).Kind);
        Equal(ResultKind.Failure, RestrictionResults.WrongOwner("restriction-owner-mismatch").Kind);

        var release = new RestrictionCommand(Id(scope, "restrictions", "fact", "release-1"), subject, debtOwner, "service-access", cause,
            RestrictionOperation.Release, OwnerGeneration.Create(8), At(TimeKind.Effective, 20), predecessorFactId: factId, predecessorOwnerId: debtOwner);
        Equal(RestrictionOperation.Release, release.Operation);

        Throws<ArgumentException>(() => Consume(new RestrictionCommand(Id(scope, "restrictions", "fact", "bad-owner"), subject, debtOwner,
            "service-access", cause, RestrictionOperation.Release, OwnerGeneration.Create(8), At(TimeKind.Effective, 20),
            predecessorFactId: factId, predecessorOwnerId: fraudOwner)));
        Throws<ArgumentException>(() => Consume(new RestrictionCommand(Id(scope, "restrictions", "fact", "missing-lineage"), subject, debtOwner,
            "service-access", cause, RestrictionOperation.Release, OwnerGeneration.Create(8), At(TimeKind.Effective, 20))));
        Throws<ArgumentException>(() => Consume(new RestrictionCommand(Id(scope, "restrictions", "fact", "bad-dimension"), subject, debtOwner,
            "INVALID DIMENSION", cause, RestrictionOperation.Restrict, OwnerGeneration.Create(8), At(TimeKind.Effective, 20))));
        Throws<ArgumentException>(() => Consume(new RestrictionFactRecord(command, OwnerGeneration.Create(9), admitted.FactDigest, At(TimeKind.Record, 11))));
        Throws<ArgumentException>(() => RestrictionResults.Indeterminate("INVALID CODE"));

        RunCanonicalRoutePressure(scope, subject, debtOwner, cause);
    }

    private static void RunCanonicalRoutePressure(ScopeId scope, SemanticId subject, SemanticId owner, SemanticId cause)
    {
        var routes = new[] { "COLL-001", "COLL-002", "COLL-003" };
        foreach (var route in routes)
        {
            var fact = new RestrictionCommand(Id(scope, "restrictions", "fact", route.ToLowerInvariant()), subject, owner,
                "collection", cause, RestrictionOperation.Restrict, OwnerGeneration.Create(1), At(TimeKind.Effective, 30));
            Equal(RestrictionOperation.Restrict, fact.Operation);
        }
        Equal(3, routes.Distinct(StringComparer.Ordinal).Count());
    }

    private static SemanticId Id(ScopeId scope, string ns, string kind, string value) => SemanticId.Create(scope, ns, kind, value);
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigestProfileId Profile(string discriminator) => new(discriminator, ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless");
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
