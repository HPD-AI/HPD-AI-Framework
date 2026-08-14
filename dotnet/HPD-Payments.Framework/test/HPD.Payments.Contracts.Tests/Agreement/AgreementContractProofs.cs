using HPD.Payments.Contracts.Agreement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.Agreement;

internal static class AgreementContractProofs
{
    internal static void RunAll()
    {
        EveryAgreementRouteIsIndependentlyAddressable();
        AmendmentRequiresLineage();
        EffectiveIntervalsAndScopesAreGuarded();
        ClosedResultVariantsRemainDistinct();
    }

    private static void EveryAgreementRouteIsIndependentlyAddressable()
    {
        var routes = Enum.GetValues<AgreementRoute>().Where(static value => value != AgreementRoute.None).ToArray();
        Equal(11, routes.Length);
        foreach (var route in routes)
            _ = Command(route, AgreementOperation.Accept);
    }

    private static void AmendmentRequiresLineage()
    {
        Throws<ArgumentException>(() => _ = Command(AgreementRoute.Comm004, AgreementOperation.Amend));
        var predecessor = Id("agreement-fact", "f1");
        _ = Command(AgreementRoute.Comm004, AgreementOperation.Amend, predecessor);
    }

    private static void EffectiveIntervalsAndScopesAreGuarded()
    {
        var subject = Id("agreement", "a1");
        var fact = Id("agreement-fact", "f1");
        _ = new AcceptedAgreementFact(fact, subject, Digest("terms"), OwnerGeneration.Create(1), Time(TimeKind.Effective, 2), Time(TimeKind.Record, 3));
        Throws<ArgumentException>(() => _ = new AcceptedAgreementFact(fact, subject, Digest("terms"), OwnerGeneration.Create(1), Time(TimeKind.Effective, 2), Time(TimeKind.Record, 3), effectiveUntil: Time(TimeKind.Effective, 1)));
        var otherScope = SemanticId.Create(ScopeId.Create("tenant-b", "live", "agreement"), "commercial", "agreement-fact", "f2");
        Throws<ArgumentException>(() => _ = new AcceptedAgreementFact(otherScope, subject, Digest("terms"), OwnerGeneration.Create(1), Time(TimeKind.Effective, 2), Time(TimeKind.Record, 3)));
    }

    private static void ClosedResultVariantsRemainDistinct()
    {
        Equal(ResultKind.Conflict, AgreementResults.Conflict("generation-conflict").Kind);
        Equal(ResultKind.Indeterminate, AgreementResults.Indeterminate("authorization-unavailable").Kind);
        Equal(ResultKind.Unsupported, AgreementResults.Unsupported("profile-unsupported").Kind);
    }

    private static AgreementCommand Command(AgreementRoute route, AgreementOperation operation, SemanticId? predecessor = null) => new(route, operation, Id("agreement", "a1"), Digest("terms"), OwnerGeneration.Create(1), Revision.Create("authorization", 1), Revision.Create("terms-manifest", 1), Time(TimeKind.Requested, 1), Time(TimeKind.Effective, 2), predecessor);
    private static SemanticId Id(string kind, string local) => SemanticId.Create(ScopeId.Create("tenant-a", "live", "agreement"), "commercial", kind, local);
    private static CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(new CanonicalDigestProfileId("agreement", ContractVersion.Create(1, 0), "terms-v1", "none", "utc-v1", "ordered", "sha256-keyless"), System.Text.Encoding.UTF8.GetBytes(value));
    private static NamedTime Time(TimeKind kind, long seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static void Equal<T>(T expected, T actual) where T : notnull { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
