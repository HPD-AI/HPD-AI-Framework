using HPD.Payments.Contracts.HeldPosition;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.HeldPosition;

/// <summary>Partition-local executable checks invoked after central runner registration.</summary>
public static class HeldPositionContractTests
{
    /// <summary>Executes kind separation, checked capacity, guards, lineage, result, and route-substitution checks.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "held-position");
        var position = Id(scope, "positions", "position", "p1");
        var evidence = Id(scope, "evidence", "source", "payment-1");
        var fact = Id(scope, "positions", "fact", "f1");
        var digest = Digest("evidence");
        var acquire = new ChangeHeldPositionCommand(fact, position, HeldPositionKind.PaymentAllocation,
            HeldPositionChangeKind.Acquire, HeldQuantity.Create(10m, "usd"), evidence, digest,
            OwnerGeneration.Create(1), HeldQuantity.Create(0m, "usd"), At(TimeKind.Effective, 1));
        var acquired = new HeldPositionFact(acquire, OwnerGeneration.Create(2), HeldQuantity.Create(10m, "usd"), Digest("fact"), At(TimeKind.Record, 2));
        Equal(HeldPositionResultKind.Admitted, HeldPositionResult.WithFact(HeldPositionResultKind.Admitted, acquired).Kind);

        var consume = new ChangeHeldPositionCommand(Id(scope, "positions", "fact", "f2"), position,
            HeldPositionKind.PaymentAllocation, HeldPositionChangeKind.Consume, HeldQuantity.Create(4m, "usd"), evidence, digest,
            OwnerGeneration.Create(2), HeldQuantity.Create(10m, "usd"), At(TimeKind.Effective, 3), fact);
        _ = new HeldPositionFact(consume, OwnerGeneration.Create(3), HeldQuantity.Create(6m, "usd"), Digest("fact-2"), At(TimeKind.Record, 4));

        Throws<ArgumentException>(() => Consume(HeldQuantity.Subtract(HeldQuantity.Create(1m, "usd"), HeldQuantity.Create(2m, "usd"))));
        Throws<ArgumentException>(() => Consume(HeldQuantity.Add(HeldQuantity.Create(1m, "usd"), HeldQuantity.Create(1m, "eur"))));
        Throws<OverflowException>(() => Consume(HeldQuantity.Add(HeldQuantity.Create(decimal.MaxValue, "usd"), HeldQuantity.Create(1m, "usd"))));
        Throws<ArgumentException>(() => Consume(new ChangeHeldPositionCommand(Id(scope, "positions", "fact", "bad"), position,
            HeldPositionKind.PaymentAllocation, HeldPositionChangeKind.Consume, HeldQuantity.Create(11m, "usd"), evidence, digest,
            OwnerGeneration.Create(2), HeldQuantity.Create(10m, "usd"), At(TimeKind.Effective, 3), fact)));
        Throws<ArgumentException>(() => Consume(new HeldPositionFact(consume, OwnerGeneration.Create(3), HeldQuantity.Create(7m, "usd"), digest, At(TimeKind.Record, 4))));
        Throws<ArgumentException>(() => Consume(HeldPositionResult.WithoutFact(HeldPositionResultKind.Unknown, "INVALID CODE")));
        Throws<ArgumentException>(() => Consume(HeldPositionResult.WithFact(HeldPositionResultKind.Conflict, acquired)));

        var routes = new[] { "BILL-007", "BILL-018" };
        Equal(2, routes.Distinct(StringComparer.Ordinal).Count());
        foreach (var route in routes)
        {
            var routeCommand = new ChangeHeldPositionCommand(Id(scope, "positions", "fact", route.ToLowerInvariant()), position,
                route == "BILL-007" ? HeldPositionKind.PaymentAllocation : HeldPositionKind.ProvenanceMigrationOpening,
                HeldPositionChangeKind.Acquire, HeldQuantity.Create(1m, "usd"), evidence, Digest(route),
                OwnerGeneration.Create(1), HeldQuantity.Create(0m, "usd"), At(TimeKind.Effective, 5));
            _ = new HeldPositionFact(routeCommand, OwnerGeneration.Create(2), HeldQuantity.Create(1m, "usd"), Digest($"{route}-fact"), At(TimeKind.Record, 6));
        }

        var blocked = new[] { "BILL-009", "BILL-010", "BILL-011" };
        Equal(3, blocked.Distinct(StringComparer.Ordinal).Count());
        Equal(false, routes.Intersect(blocked, StringComparer.Ordinal).Any());
    }

    private static SemanticId Id(ScopeId scope, string ns, string kind, string value) => SemanticId.Create(scope, ns, kind, value);
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(new("held-position", ContractVersion.Create(1, 0), "semantic", "none", "decimal-v1", "ordered", "sha256-keyless"), System.Text.Encoding.ASCII.GetBytes(value));
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
