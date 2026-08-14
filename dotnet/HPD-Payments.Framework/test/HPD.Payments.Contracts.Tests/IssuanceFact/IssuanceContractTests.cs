using HPD.Payments.Contracts.IssuanceFact;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.IssuanceFact;

/// <summary>Partition-local executable checks invoked by the centrally owned Contracts test runner.</summary>
public static class IssuanceContractTests
{
    /// <summary>Executes exact-byte ownership/digest, number guard, lineage, scope, generation, time, and result checks.</summary>
    public static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "live", "issuance-fact");
        var other = ScopeId.Create("tenant-b", "live", "issuance-fact");
        var profile = Profile("issued-artifact");
        var sourceDigest = CanonicalDigest.Sha256(Profile("render-manifest"), "source"u8);
        var source = Id(scope, "render", "manifest", "m1");
        var fact = Id(scope, "issuance", "fact", "i1");
        var artifact = Id(scope, "issuance", "artifact", "a1");
        var issuer = Id(scope, "issuance", "issuer", "issuer1");
        var bytesArray = new byte[] { 1, 2, 3, 4 };
        var bytes = new OwnedClassifiedBytes(bytesArray, ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable));
        var artifactDigest = CanonicalDigest.Sha256(profile, bytesArray);
        var claim = new IssuanceNumberClaim(issuer, Revision.Create("numbering", 2), "inv-0001", OwnerGeneration.Create(4));
        var command = new RecordIssuanceCommand(fact, artifact, source, sourceDigest, IssuanceFactKind.Issued, claim, bytes,
            artifactDigest, At(TimeKind.Issue, 10));
        bytesArray[0] = 99;
        Equal((byte)1, command.ArtifactBytes.CopyBytes()[0]);
        var record = new IssuanceFactRecord(command, OwnerGeneration.Create(7), OwnerGeneration.Create(8), OwnerGeneration.Create(5), At(TimeKind.Record, 11));
        Equal(IssuanceAdmissionKind.Admitted, IssuanceAdmissionResult.WithRecord(IssuanceAdmissionKind.Admitted, record).Kind);
        Equal(IssuanceAdmissionKind.Conflict, IssuanceAdmissionResult.WithoutRecord(IssuanceAdmissionKind.Conflict, "number-occupied").Kind);

        Throws<ArgumentException>(() => Consume(new RecordIssuanceCommand(fact, artifact, source, sourceDigest, IssuanceFactKind.Issued, claim, bytes,
            CanonicalDigest.Sha256(profile, "wrong"u8), At(TimeKind.Issue, 10))));
        Throws<ArgumentException>(() => Consume(new RecordIssuanceCommand(fact, artifact, source, sourceDigest, IssuanceFactKind.Superseded, claim, bytes,
            artifactDigest, At(TimeKind.Issue, 10))));
        Throws<ArgumentException>(() => Consume(new IssuanceNumberClaim(Id(other, "issuance", "issuer", "issuer2"), Revision.Create("numbering", 2), "INV 1", OwnerGeneration.Create(4))));
        Throws<ArgumentException>(() => Consume(new IssuanceFactRecord(command, OwnerGeneration.Create(7), OwnerGeneration.Create(9), OwnerGeneration.Create(5), At(TimeKind.Record, 11))));
        Throws<ArgumentException>(() => IssuanceAdmissionResult.WithoutRecord(IssuanceAdmissionKind.Rejected, "INVALID CODE"));
        Throws<ArgumentOutOfRangeException>(() => IssuanceAdmissionResult.WithRecord(IssuanceAdmissionKind.Unknown, record));

        RunCanonicalRouteCoverage(scope, profile, sourceDigest, source, artifact, issuer, bytes, artifactDigest, claim);
    }

    private static void RunCanonicalRouteCoverage(ScopeId scope, CanonicalDigestProfileId profile, CanonicalDigest sourceDigest,
        SemanticId source, SemanticId artifact, SemanticId issuer, OwnedClassifiedBytes bytes, CanonicalDigest artifactDigest,
        IssuanceNumberClaim claim)
    {
        var routes = new (string Id, string Local)[] { ("BILL-003", "bill-003"), ("BILL-014", "bill-014"), ("BILL-019", "bill-019") };
        foreach (var route in routes)
        {
            var local = route.Local;
            var command = new RecordIssuanceCommand(Id(scope, "issuance", "fact", local), artifact, source, sourceDigest,
                IssuanceFactKind.Issued, claim, bytes, artifactDigest, At(TimeKind.Issue, 30));
            _ = new IssuanceFactRecord(command, OwnerGeneration.Create(7), OwnerGeneration.Create(8), OwnerGeneration.Create(5), At(TimeKind.Record, 31));
            Throws<ArgumentException>(() => Consume(new RecordIssuanceCommand(Id(scope, "issuance", "fact", $"bad-{local}"), artifact, source,
                sourceDigest, IssuanceFactKind.Superseded,
                new IssuanceNumberClaim(issuer, Revision.Create("numbering", 2), $"n-{local}", OwnerGeneration.Create(4)),
                bytes, CanonicalDigest.Sha256(profile, "wrong"u8), At(TimeKind.Issue, 30), Id(scope, "issuance", "fact", $"prior-{local}"))));
        }
        Equal(3, routes.Select(static x => x.Id).Distinct(StringComparer.Ordinal).Count());
    }

    private static SemanticId Id(ScopeId scope, string ns, string kind, string value) => SemanticId.Create(scope, ns, kind, value);
    private static NamedTime At(TimeKind kind, int seconds) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
    private static CanonicalDigestProfileId Profile(string discriminator) => new(discriminator, ContractVersion.Create(1, 0), "semantic", "none", "bytes-time-v1", "ordered", "sha256-keyless");
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Consume(object value) => GC.KeepAlive(value);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
