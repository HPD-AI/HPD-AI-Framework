using HPD.Payments.Contracts.MeasuredFact;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.MeasuredFact;

internal static class MeasuredFactContractTests
{
    internal static void Run()
    {
        var fact = Id("measured-fact", "fact", "f1");
        var subject = Id("agreement", "subject", "s1");
        var source = Id("source-receipt", "event", "e1");
        var command = new AdmitMeasuredFactCommand(fact, subject, source, Digest("measured-fact"), MeasuredQuantity.Create(0m, "request"),
            At(TimeKind.Source, 0), At(TimeKind.Source, 1), Revision.Create("meter", 1), OwnerGeneration.Create(1));
        Equal(0m, command.Quantity.Value);
        Throws<ArgumentException>(() => Consume(new AdmitMeasuredFactCommand(fact, subject, source, Digest("measured-fact"), MeasuredQuantity.Create(1m, "request"),
            At(TimeKind.Source, 1), At(TimeKind.Source, 1), Revision.Create("meter", 1), OwnerGeneration.Create(1))));
        Throws<ArgumentException>(() => Consume(new CorrectMeasuredFactCommand(fact, OwnerGeneration.Create(1), command, "correction")));
        var tombstone = new CorrectMeasuredFactCommand(fact, OwnerGeneration.Create(1), null, "source-deleted");
        Equal(null, tombstone.Successor);
    }

    private static SemanticId Id(string authority, string kind, string local) => SemanticId.Create(ScopeId.Create("tenant-a", "test", authority), "contract-test", kind, local);
    private static NamedTime At(TimeKind kind, int minutes) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddMinutes(minutes));
    private static CanonicalDigest Digest(string discriminator) => CanonicalDigest.Sha256(new(discriminator, ContractVersion.Create(1, 0), "fields", "none", "decimal-time-v1", "ordered", "sha256-keyless"), "semantic"u8);
    private static void Consume<T>(T value) { _ = value; }
    private static void Equal<T>(T expected, T actual) where T : IEquatable<T>? { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
