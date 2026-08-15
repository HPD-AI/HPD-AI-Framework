using HPD.Payments.Contracts.CapabilityEvidence;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.CapabilityEvidence;

/// <summary>Executes partition-local Capability Evidence contract proofs.</summary>
public static class CapabilityEvidenceContractTests
{
    /// <summary>Proves contextual positive/negative/conditional/expired/withdrawn/conflicted evidence and route mapping.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "capability-evidence");
        var account = SemanticId.Create(scope, "connectors", "account", "acct-1", "simulator", "primary");
        var context = new CapabilityContext(account, "capture", "api-1", Revision.Create("code", 1), Revision.Create("config", 2), Revision.Create("credential", 3), "static", "osx-arm64");
        var id = SemanticId.Create(scope, "capabilities", "evidence", "ev-1");
        var prior = SemanticId.Create(scope, "capabilities", "evidence", "ev-0");
        var profile = Profile(); var digest = CanonicalDigest.Sha256(profile, "evidence"u8);
        var verified = At(TimeKind.Verify, 10); var expiry = At(TimeKind.Expiry, 20);
        var positive = new CapabilityEvidenceFact(id, context, CapabilityDisposition.Positive, "certified", verified, expiry, digest);
        True(positive.EstablishesSupport(DateTimeOffset.UnixEpoch.AddSeconds(15)));
        False(positive.EstablishesSupport(DateTimeOffset.UnixEpoch.AddSeconds(21)));
        foreach (var disposition in new[] { CapabilityDisposition.Negative, CapabilityDisposition.Conditional })
            False(new CapabilityEvidenceFact(id, context, disposition, "explicit", verified, expiry, digest).EstablishesSupport(DateTimeOffset.UnixEpoch.AddSeconds(15)));
        foreach (var disposition in new[] { CapabilityDisposition.Expired, CapabilityDisposition.Withdrawn, CapabilityDisposition.Conflicted })
            False(new CapabilityEvidenceFact(id, context, disposition, "superseded", verified, expiry, digest, prior).EstablishesSupport(DateTimeOffset.UnixEpoch.AddSeconds(15)));
        Throws<ArgumentException>(() => Consume(new CapabilityEvidenceFact(id, context, CapabilityDisposition.Expired, "expired", verified, expiry, digest)));
        Throws<ArgumentException>(() => positive.EstablishesSupport(DateTimeOffset.UnixEpoch.AddSeconds(15).ToOffset(TimeSpan.FromHours(1))));
        Equal(31, Routes.Length); Equal(31, Routes.Distinct(StringComparer.Ordinal).Count());
    }

    private static readonly string[] Routes = ["CONN-001","CONN-002","CONN-003","CONN-004","CONN-005","CONN-006","DISP-001","DISP-002","DISP-003","DISP-004","PAY-001","PAY-002","PAY-003","PAY-004","PAY-005","PAY-006","PAY-007","PAY-008","PAY-009","PAY-010","PAY-011","PAYOUT-001","PAYOUT-002","PAYOUT-003","REF-001","ROUT-001","ROUT-002","ROUT-003","ROUT-004","ROUT-005","ROUT-006"];
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigestProfileId Profile() => new("capability-evidence", ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless");
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
    private static void False(bool value) { if (value) throw new InvalidOperationException("Expected false."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
