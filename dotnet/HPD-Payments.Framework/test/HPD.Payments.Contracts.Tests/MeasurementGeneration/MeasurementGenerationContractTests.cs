using HPD.Payments.Contracts.MeasurementGeneration;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.MeasurementGeneration;

internal static class MeasurementGenerationContractTests
{
    internal static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "test", "measurement-generation");
        var generation = SemanticId.Create(scope, "contract-test", "generation", "g1");
        var subject = SemanticId.Create(ScopeId.Create("tenant-a", "test", "agreement"), "contract-test", "subject", "s1");
        var member = SemanticId.Create(ScopeId.Create("tenant-a", "test", "measured-fact"), "contract-test", "fact", "f1");
        var cut = new HistoricalCut(HistoricalFrameKind.AsKnownAt, At(TimeKind.Record, 2), Array.Empty<OwnerCut>(), ContractVersion.Create(1, 0));
        var algebra = new MeasurementAlgebraContract(MeasurementAlgebraKind.UniqueCount, Revision.Create("algebra", 1), true, false, false, true);
        var source = new[] { member };
        var command = new CreateMeasurementGenerationCommand(generation, subject, At(TimeKind.Effective, 0), At(TimeKind.Effective, 1), cut, algebra, source, GenerationCompleteness.Complete, OwnerGeneration.Create(1));
        source[0] = default;
        True(command.Members[0].IsValid);
        Throws<ArgumentException>(() => Consume(new MeasurementAlgebraContract(MeasurementAlgebraKind.Maximum, Revision.Create("algebra", 1), false, false, true, true)));
        Throws<ArgumentException>(() => Consume(new CreateMeasurementGenerationCommand(generation, subject, At(TimeKind.Effective, 0), At(TimeKind.Effective, 1), cut, algebra,
            new[] { member, member }, GenerationCompleteness.Complete, OwnerGeneration.Create(1))));
        var incomplete = new CreateMeasurementGenerationCommand(SemanticId.Create(scope, "contract-test", "generation", "g2"), subject,
            At(TimeKind.Effective, 0), At(TimeKind.Effective, 1), cut, algebra, Array.Empty<SemanticId>(), GenerationCompleteness.Incomplete, OwnerGeneration.Create(1));
        Equal(GenerationCompleteness.Incomplete, incomplete.Completeness);
    }

    private static NamedTime At(TimeKind kind, int minutes) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddMinutes(minutes));
    private static void Consume<T>(T value) { _ = value; }
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
    private static void Equal<T>(T expected, T actual) where T : struct, Enum { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
