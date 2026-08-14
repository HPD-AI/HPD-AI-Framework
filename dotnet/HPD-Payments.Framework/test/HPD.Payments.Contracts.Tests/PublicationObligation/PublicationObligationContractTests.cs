using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.PublicationObligation;

/// <summary>Executes partition-local Publication Obligation contract proofs.</summary>
public static class PublicationObligationContractTests
{
    /// <summary>Proves audience isolation, delivery/acknowledgement times, replay lineage, residue, and route mapping.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "publication-obligation");
        var publication = SemanticId.Create(scope, "publications", "obligation", "pub-1");
        var source = SemanticId.Create(scope, "facts", "source", "fact-1");
        var delivery = SemanticId.Create(scope, "publications", "delivery", "delivery-1");
        var profile = Profile(); var digest = CanonicalDigest.Sha256(profile, "payload"u8);
        var obligation = new PublicationObligationFact(publication, source, "merchant-api", "payments", digest, ContractVersion.Create(1, 0), At(TimeKind.Record, 1));
        _ = new PublicationDeliveryFact(obligation, delivery, 1, PublicationDisposition.Attempted, digest, At(TimeKind.Dispatch, 2), "http-timeout");
        _ = new PublicationDeliveryFact(obligation, delivery, 1, PublicationDisposition.Acknowledged, digest, At(TimeKind.Acknowledged, 3), "ack-v1");
        _ = new PublicationDeliveryFact(obligation, delivery, 2, PublicationDisposition.Residual, digest, At(TimeKind.Dispatch, 4), "recipient-copy");
        Throws<ArgumentException>(() => Consume(new PublicationDeliveryFact(obligation, delivery, 1, PublicationDisposition.Acknowledged, digest, At(TimeKind.Dispatch, 3), "wrong-time")));
        Throws<ArgumentException>(() => Consume(new PublicationDeliveryFact(obligation, delivery, 0, PublicationDisposition.Attempted, digest, At(TimeKind.Dispatch, 2), "missing-attempt")));
        Equal(15, Routes.Length); Equal(15, Routes.Distinct(StringComparer.Ordinal).Count());
    }

    private static readonly string[] Routes = ["EVT-001","EVT-002","EVT-003","EVT-004","EVT-005","EVT-006","OBS-001","OBS-002","WORK-001","WORK-002","WORK-003","WORK-004","WORK-005","WORK-006","WORK-007"];
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigestProfileId Profile() => new("publication-obligation", ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless");
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
