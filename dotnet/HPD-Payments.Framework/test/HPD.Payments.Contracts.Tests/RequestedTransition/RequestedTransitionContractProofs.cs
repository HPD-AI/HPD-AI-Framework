using HPD.Payments.Contracts.RequestedTransition;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.RequestedTransition;

internal static class RequestedTransitionContractProofs
{
    internal static void RunAll()
    {
        RequestIsPendingAndBounded();
        SupersedeAndCancelRequireLineage();
        SupersededFactsRequireSuccessor();
        ClosedResultsKeepTerminalMeaning();
    }

    private static void RequestIsPendingAndBounded()
    {
        _ = Command(RequestedTransitionOperation.Request);
        var fact = new RequestedTransitionFact(Id("transition", "t1"), Id("subject", "s1"), Digest("target"), OwnerGeneration.Create(1), RequestedTransitionDisposition.Pending, Time(TimeKind.Effective, 10), Time(TimeKind.Record, 2));
        Equal(RequestedTransitionDisposition.Pending, fact.Disposition);
    }

    private static void SupersedeAndCancelRequireLineage()
    {
        Throws<ArgumentException>(() => _ = Command(RequestedTransitionOperation.Supersede));
        Throws<ArgumentException>(() => _ = Command(RequestedTransitionOperation.Cancel));
        _ = Command(RequestedTransitionOperation.Supersede, Id("transition", "t0"));
        _ = Command(RequestedTransitionOperation.Cancel, Id("transition", "t0"));
    }

    private static void SupersededFactsRequireSuccessor()
    {
        Throws<ArgumentException>(() => _ = new RequestedTransitionFact(Id("transition", "t1"), Id("subject", "s1"), Digest("target"), OwnerGeneration.Create(2), RequestedTransitionDisposition.Superseded, Time(TimeKind.Effective, 10), Time(TimeKind.Record, 3), Id("transition", "t0")));
        _ = new RequestedTransitionFact(Id("transition", "t1"), Id("subject", "s1"), Digest("target"), OwnerGeneration.Create(2), RequestedTransitionDisposition.Superseded, Time(TimeKind.Effective, 10), Time(TimeKind.Record, 3), Id("transition", "t0"), Id("transition", "t2"));
    }

    private static void ClosedResultsKeepTerminalMeaning()
    {
        Equal(ResultKind.Conflict, RequestedTransitionResults.Conflict("digest-conflict").Kind);
        Equal(ResultKind.Cancelled, RequestedTransitionResults.Cancelled("request-cancelled").Kind);
        Equal(ResultKind.Superseded, RequestedTransitionResults.Superseded("request-superseded").Kind);
    }

    private static RequestedTransitionCommand Command(RequestedTransitionOperation operation, SemanticId? predecessor = null) => new(Id("transition", "t1"), Id("subject", "s1"), operation, Digest("target"), OwnerGeneration.Create(1), Revision.Create("calculation", 1), Time(TimeKind.Requested, 1), Time(TimeKind.Effective, 10), predecessor);
    private static SemanticId Id(string kind, string local) => SemanticId.Create(ScopeId.Create("tenant-a", "live", "requested-transition"), "commercial", kind, local);
    private static CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(new CanonicalDigestProfileId("requested-transition", ContractVersion.Create(1, 0), "target-v1", "none", "utc-v1", "ordered", "sha256-keyless"), System.Text.Encoding.UTF8.GetBytes(value));
    private static NamedTime Time(TimeKind kind, long seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static void Equal<T>(T expected, T actual) where T : notnull { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
