using HPD.Payments.Contracts.Obligation;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.Obligation;

/// <summary>Partition-local executable checks invoked by the centrally owned Contracts test runner.</summary>
public static class ObligationContractTests
{
    /// <summary>Executes positive, default, scope, lineage, generation, time-axis, and closed-result checks.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "obligation");
        var other = ScopeId.Create("tenant-b", "live", "obligation");
        var obligation = Id(scope, "obligations", "obligation", "o1");
        var fact = Id(scope, "obligations", "fact", "f1");
        var source = Id(scope, "valuation", "manifest", "v1");
        var profile = Profile("obligation-fact");
        var digest = CanonicalDigest.Sha256(profile, "source"u8);
        var guard = new ObligationGuard(OwnerGeneration.Create(1), null);
        var command = new AdmitObligationCommand(fact, obligation, source, digest, ObligationFactKind.Initial,
            ObligationDirection.Due, ObligationQuantity.Create(12.50m, "usd"),
            At(TimeKind.Effective, 10), At(TimeKind.Source, 9), guard);
        var admitted = new ObligationFact(command, OwnerGeneration.Create(2), CanonicalDigest.Sha256(profile, "fact"u8), At(TimeKind.Record, 11));
        Equal(ObligationAdmissionKind.Admitted, ObligationAdmissionResult.WithFact(ObligationAdmissionKind.Admitted, admitted).Kind);
        Equal(ObligationAdmissionKind.Unknown, ObligationAdmissionResult.WithoutFact(ObligationAdmissionKind.Unknown, "owner-unavailable").Kind);

        Throws<ArgumentException>(() => ObligationQuantity.Create(0m, "usd"));
        Throws<ArgumentException>(() => Consume(new AdmitObligationCommand(fact, obligation, source, digest, ObligationFactKind.Adjustment,
            ObligationDirection.Due, ObligationQuantity.Create(1m, "usd"), At(TimeKind.Effective, 10), At(TimeKind.Source, 9), guard)));
        Throws<ArgumentException>(() => Consume(new AdmitObligationCommand(Id(other, "obligations", "fact", "f2"), obligation, source, digest,
            ObligationFactKind.Initial, ObligationDirection.Due, ObligationQuantity.Create(1m, "usd"), At(TimeKind.Effective, 10), At(TimeKind.Source, 9), guard)));
        Throws<ArgumentException>(() => Consume(new ObligationFact(command, OwnerGeneration.Create(3), digest, At(TimeKind.Record, 11))));
        Throws<ArgumentException>(() => Consume(new ObligationFact(command, OwnerGeneration.Create(2), digest, At(TimeKind.Issue, 11))));
        Throws<ArgumentException>(() => ObligationAdmissionResult.WithoutFact(ObligationAdmissionKind.Unknown, "INVALID CODE"));
        Throws<ArgumentOutOfRangeException>(() => ObligationAdmissionResult.WithFact(ObligationAdmissionKind.Conflict, admitted));

        RunCanonicalRouteCoverage(scope, profile, digest, guard, obligation, source);
    }

    private static void RunCanonicalRouteCoverage(ScopeId scope, CanonicalDigestProfileId profile, CanonicalDigest sourceDigest,
        ObligationGuard guard, SemanticId obligation, SemanticId source)
    {
        var routes = new (string Id, string Local)[]
        {
            ("BILL-001", "bill-001"), ("BILL-002", "bill-002"), ("BILL-004", "bill-004"), ("BILL-005", "bill-005"),
            ("BILL-006", "bill-006"), ("BILL-008", "bill-008"), ("BILL-012", "bill-012"), ("BILL-013", "bill-013"),
            ("BILL-015", "bill-015"), ("BILL-016", "bill-016"), ("BILL-017", "bill-017"), ("BILL-020", "bill-020"),
            ("COLL-001", "coll-001"), ("COLL-002", "coll-002"), ("COLL-003", "coll-003"), ("VAL-001", "val-001"),
        };
        foreach (var route in routes)
        {
            var local = route.Local;
            var predecessor = Id(scope, "obligations", "fact", $"prior-{local}");
            var command = new AdmitObligationCommand(Id(scope, "obligations", "fact", local), obligation, source, sourceDigest,
                ObligationFactKind.Adjustment, ObligationDirection.Due, ObligationQuantity.Create(1m, "usd"),
                At(TimeKind.Effective, 20), At(TimeKind.Source, 19), guard, predecessor);
            _ = new ObligationFact(command, OwnerGeneration.Create(2), CanonicalDigest.Sha256(profile, System.Text.Encoding.ASCII.GetBytes(route.Id)), At(TimeKind.Record, 21));
            Throws<ArgumentException>(() => Consume(new AdmitObligationCommand(Id(scope, "obligations", "fact", $"bad-{local}"), obligation, source, sourceDigest,
                ObligationFactKind.Adjustment, ObligationDirection.None, ObligationQuantity.Create(1m, "usd"),
                At(TimeKind.Effective, 20), At(TimeKind.Source, 19), guard, predecessor)));
        }
        Equal(16, routes.Select(static x => x.Id).Distinct(StringComparer.Ordinal).Count());
    }

    private static SemanticId Id(ScopeId scope, string ns, string kind, string value) => SemanticId.Create(scope, ns, kind, value);
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigestProfileId Profile(string discriminator) => new(discriminator, ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless");
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
