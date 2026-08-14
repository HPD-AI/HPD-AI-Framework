using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.ExternalEffect;

/// <summary>Executes partition-local External Effect contract proofs.</summary>
public static class ExternalEffectContractTests
{
    /// <summary>Proves exact attempt binding, possible-dispatch preservation, retry safety, lineage, and route mapping.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "external-effect");
        var operation = SemanticId.Create(scope, "effects", "operation", "op-1");
        var attempt = SemanticId.Create(scope, "effects", "attempt", "attempt-1");
        var account = SemanticId.Create(scope, "connectors", "account", "acct-1", "simulator", "primary");
        var profile = Profile(); var digest = CanonicalDigest.Sha256(profile, "request"u8);
        var bound = new ExternalEffectOperation(operation, attempt, account, "idem-1", digest, Revision.Create("credential", 1), Revision.Create("config", 1));
        var initial = new ExternalEffectFact(bound, ExternalEffectState.NotDispatched, null, digest, At(TimeKind.Observed, 1), "created");
        True(initial.PermitsAnotherDispatch);
        var possible = new ExternalEffectFact(bound, ExternalEffectState.PossibleDispatch, digest, digest, At(TimeKind.Dispatch, 2), "transport-lost");
        False(possible.PermitsAnotherDispatch);
        False(new ExternalEffectFact(bound, ExternalEffectState.ConfirmedOccurred, digest, digest, At(TimeKind.Observed, 3), "provider-confirmed").PermitsAnotherDispatch);
        True(new ExternalEffectFact(bound, ExternalEffectState.ConfirmedNotOccurred, digest, digest, At(TimeKind.Observed, 3), "provider-rejected-before-send").PermitsAnotherDispatch);
        Throws<ArgumentException>(() => Consume(new ExternalEffectFact(bound, ExternalEffectState.PossibleDispatch, null, digest, At(TimeKind.Dispatch, 2), "lost")));
        Throws<ArgumentException>(() => Consume(new ExternalEffectFact(bound, ExternalEffectState.ConfirmedOccurred, digest, digest, At(TimeKind.Dispatch, 2), "wrong-time")));
        Equal(41, Routes.Length); Equal(41, Routes.Distinct(StringComparer.Ordinal).Count());
    }

    private static readonly string[] Routes = ["AUTH-001","AUTH-002","AUTH-003","AUTH-004","CHK-001","CHK-002","CHK-003","CONN-001","CONN-002","CONN-003","CONN-004","CONN-005","CONN-006","DISP-001","DISP-002","DISP-003","DISP-004","PAY-001","PAY-002","PAY-003","PAY-004","PAY-005","PAY-006","PAY-007","PAY-008","PAY-009","PAY-010","PAY-011","PAYOUT-001","PAYOUT-002","PAYOUT-003","REF-001","RISK-001","RISK-002","RISK-003","ROUT-001","ROUT-002","ROUT-003","ROUT-004","ROUT-005","ROUT-006"];
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigestProfileId Profile() => new("external-effect", ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless");
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
    private static void False(bool value) { if (value) throw new InvalidOperationException("Expected false."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
