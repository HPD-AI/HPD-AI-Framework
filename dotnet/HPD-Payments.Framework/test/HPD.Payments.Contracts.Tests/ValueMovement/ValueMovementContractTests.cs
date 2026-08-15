using HPD.Payments.Contracts.ValueMovement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.ValueMovement;

/// <summary>Partition-local executable checks invoked after central runner registration.</summary>
public static class ValueMovementContractTests
{
    private static readonly string[] Routes =
    {
        "BILL-001", "BILL-002", "BILL-004", "BILL-005", "BILL-006", "BILL-008", "BILL-012",
        "BILL-013", "BILL-015", "BILL-016", "BILL-017", "BILL-020", "VAL-001",
    };

    /// <summary>Executes endpoint, conservation, compensation, generation, checked arithmetic, result, and route-substitution checks.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "value-movement");
        var debit = new MovementEndpoint(Id(scope, "endpoints", "obligation", "debit"), OwnerGeneration.Create(4));
        var credit = new MovementEndpoint(Id(scope, "endpoints", "collection", "credit"), OwnerGeneration.Create(8));
        var source = Id(scope, "manifests", "source", "s1");
        var movement = Id(scope, "movements", "movement", "m1");
        var heldScope = ScopeId.Create("tenant-a", "live", "held-position");
        var heldReceipt = Id(heldScope, "positions", "receipt", "h1");
        var command = new AdmitValueMovementCommand(movement, ValueMovementFamily.ObligationApplication, debit, credit,
            MovementQuantity.Create(12.5m, "usd"), source, Digest("source"), OwnerGeneration.Create(1), At(TimeKind.Effective, 1), heldReceipt);
        var admitted = new ValueMovementFact(command, OwnerGeneration.Create(2), Digest("fact"), At(TimeKind.Record, 2));
        Equal(ValueMovementResultKind.Admitted, ValueMovementResult.WithFact(ValueMovementResultKind.Admitted, admitted).Kind);
        Equal(3m, MovementQuantity.Sum(new[] { MovementQuantity.Create(1m, "usd"), MovementQuantity.Create(2m, "usd") }).Magnitude);

        Throws<ArgumentException>(() => Consume(MovementQuantity.Sum(Array.Empty<MovementQuantity>())));
        Throws<ArgumentException>(() => Consume(MovementQuantity.Sum(new[] { MovementQuantity.Create(1m, "usd"), MovementQuantity.Create(1m, "eur") })));
        Throws<OverflowException>(() => Consume(MovementQuantity.Sum(new[] { MovementQuantity.Create(decimal.MaxValue, "usd"), MovementQuantity.Create(1m, "usd") })));
        Throws<ArgumentException>(() => Consume(new AdmitValueMovementCommand(Id(scope, "movements", "movement", "same"), ValueMovementFamily.Allocation,
            debit, debit, MovementQuantity.Create(1m, "usd"), source, Digest("same"), OwnerGeneration.Create(1), At(TimeKind.Effective, 1))));
        Throws<ArgumentException>(() => Consume(new AdmitValueMovementCommand(Id(scope, "movements", "movement", "comp"), ValueMovementFamily.Compensation,
            debit, credit, MovementQuantity.Create(1m, "usd"), source, Digest("comp"), OwnerGeneration.Create(1), At(TimeKind.Effective, 1))));
        Throws<ArgumentException>(() => Consume(new ValueMovementFact(command, OwnerGeneration.Create(3), Digest("bad"), At(TimeKind.Record, 2))));
        Throws<ArgumentException>(() => Consume(ValueMovementResult.WithoutFact(ValueMovementResultKind.Residual, "INVALID CODE")));
        Throws<ArgumentException>(() => Consume(ValueMovementResult.WithFact(ValueMovementResultKind.Unknown, admitted)));

        Equal(13, Routes.Distinct(StringComparer.Ordinal).Count());
        foreach (var route in Routes)
        {
            var routeId = Id(scope, "movements", "movement", route.ToLowerInvariant());
            var family = route is "BILL-005" or "BILL-006" ? ValueMovementFamily.Compensation :
                route == "VAL-001" ? ValueMovementFamily.Allocation : ValueMovementFamily.ObligationApplication;
            var prior = family == ValueMovementFamily.Compensation ? movement : (SemanticId?)null;
            _ = new AdmitValueMovementCommand(routeId, family, debit, credit, MovementQuantity.Create(1m, "usd"), source,
                Digest(route), OwnerGeneration.Create(1), At(TimeKind.Effective, 3), compensatesMovementId: prior);
        }
    }

    private static SemanticId Id(ScopeId scope, string ns, string kind, string value) => SemanticId.Create(scope, ns, kind, value);
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(new("value-movement", ContractVersion.Create(1, 0), "semantic", "none", "decimal-v1", "ordered", "sha256-keyless"), System.Text.Encoding.ASCII.GetBytes(value));
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
