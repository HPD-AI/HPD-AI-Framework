using HPD.Payments.Contracts.EffectiveCommercialFact;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.EffectiveCommercialFact;

internal static class EffectiveCommercialFactContractProofs
{
    internal static void RunAll()
    {
        EveryRouteIsIndependentlyAddressable();
        CorrectionRequiresLineage();
        TransitionReferenceAndGenerationArePaired();
        SameTimePrecedenceIsExplicit();
    }

    private static void EveryRouteIsIndependentlyAddressable()
    {
        var routes = Enum.GetValues<EffectiveCommercialRoute>().Where(static value => value != EffectiveCommercialRoute.None).ToArray();
        Equal(4, routes.Length);
        foreach (var route in routes) _ = Command(route, EffectiveCommercialOperation.Activate);
    }

    private static void CorrectionRequiresLineage()
    {
        Throws<ArgumentException>(() => _ = Command(EffectiveCommercialRoute.Comm008, EffectiveCommercialOperation.Correct));
        _ = Command(EffectiveCommercialRoute.Comm008, EffectiveCommercialOperation.Correct, predecessor: Id("fact", "f0"));
    }

    private static void TransitionReferenceAndGenerationArePaired()
    {
        Throws<ArgumentException>(() => _ = new EffectiveCommercialCommand(EffectiveCommercialRoute.Comm009, EffectiveCommercialOperation.Activate, Id("subject", "s1"), Id("agreement-fact", "a1"), Digest("applicability"), OwnerGeneration.Create(1), OwnerGeneration.Create(3), Time(TimeKind.Requested, 1), Time(TimeKind.Effective, 2), requestedTransitionId: Id("transition", "t1")));
        _ = new EffectiveCommercialCommand(EffectiveCommercialRoute.Comm009, EffectiveCommercialOperation.Activate, Id("subject", "s1"), Id("agreement-fact", "a1"), Digest("applicability"), OwnerGeneration.Create(1), OwnerGeneration.Create(3), Time(TimeKind.Requested, 1), Time(TimeKind.Effective, 2), Id("transition", "t1"), OwnerGeneration.Create(2));
    }

    private static void SameTimePrecedenceIsExplicit()
    {
        Throws<ArgumentException>(() => _ = new EffectiveCommercialFactRecord(Id("fact", "f1"), Id("subject", "s1"), Digest("applicability"), OwnerGeneration.Create(2), Id("agreement-fact", "a1"), EffectiveCommercialPrecedence.Correction, Time(TimeKind.Effective, 2), Time(TimeKind.Record, 3)));
        var corrected = new EffectiveCommercialFactRecord(Id("fact", "f1"), Id("subject", "s1"), Digest("applicability"), OwnerGeneration.Create(2), Id("agreement-fact", "a1"), EffectiveCommercialPrecedence.Correction, Time(TimeKind.Effective, 2), Time(TimeKind.Record, 3), predecessorFactId: Id("fact", "f0"));
        Equal(EffectiveCommercialPrecedence.Correction, corrected.Precedence);
        Equal(ResultKind.Superseded, EffectiveCommercialResults.Superseded("corrected-by-successor").Kind);
    }

    private static EffectiveCommercialCommand Command(EffectiveCommercialRoute route, EffectiveCommercialOperation operation, SemanticId? predecessor = null) => new(route, operation, Id("subject", "s1"), Id("agreement-fact", "a1"), Digest("applicability"), OwnerGeneration.Create(1), OwnerGeneration.Create(3), Time(TimeKind.Requested, 1), Time(TimeKind.Effective, 2), predecessorFactId: predecessor);
    private static SemanticId Id(string kind, string local) => SemanticId.Create(ScopeId.Create("tenant-a", "live", "effective-commercial-fact"), "commercial", kind, local);
    private static CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(new CanonicalDigestProfileId("effective-commercial", ContractVersion.Create(1, 0), "applicability-v1", "none", "utc-v1", "ordered", "sha256-keyless"), System.Text.Encoding.UTF8.GetBytes(value));
    private static NamedTime Time(TimeKind kind, long seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static void Equal<T>(T expected, T actual) where T : notnull { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
