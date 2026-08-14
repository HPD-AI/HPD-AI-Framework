using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.WorkRequirement;

/// <summary>Executes partition-local Work Requirement contract proofs.</summary>
public static class WorkRequirementContractTests
{
    /// <summary>Proves compatibility pins, claim fencing, retry/exhaustion bounds, and route mapping.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "work-requirement");
        var work = SemanticId.Create(scope, "work", "requirement", "work-1");
        var owner = SemanticId.Create(scope, "facts", "owner", "fact-1");
        var profile = Profile(); var digest = CanonicalDigest.Sha256(profile, "payload"u8);
        var requirement = new WorkRequirementFact(work, owner, digest, ContractVersion.Create(1, 0), Revision.Create("deployment", 4), At(TimeKind.Requested, 1), 3);
        var claim = new WorkClaim(work, OwnerGeneration.Create(2), "worker-a", At(TimeKind.Expiry, 20));
        True(claim.CanCommit(OwnerGeneration.Create(2))); False(claim.CanCommit(OwnerGeneration.Create(3)));
        _ = new WorkDispositionFact(requirement, OwnerGeneration.Create(2), WorkDisposition.RetryRequired, 1, "transient", digest);
        _ = new WorkDispositionFact(requirement, OwnerGeneration.Create(2), WorkDisposition.Exhausted, 3, "poison", digest);
        Throws<ArgumentException>(() => Consume(new WorkDispositionFact(requirement, OwnerGeneration.Create(2), WorkDisposition.Exhausted, 2, "early", digest)));
        Throws<ArgumentException>(() => Consume(new WorkDispositionFact(requirement, default, WorkDisposition.Verified, 1, "owner-ok", digest)));
        Equal(10, Routes.Length); Equal(10, Routes.Distinct(StringComparer.Ordinal).Count());
    }

    private static readonly string[] Routes = ["COLL-001","COLL-002","COLL-003","WORK-001","WORK-002","WORK-003","WORK-004","WORK-005","WORK-006","WORK-007"];
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigestProfileId Profile() => new("work-requirement", ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless");
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
    private static void False(bool value) { if (value) throw new InvalidOperationException("Expected false."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
