using System.Collections.Immutable;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Effective;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Yarp;

internal sealed record NativePublicationBundle(
    PublicationCandidateIdentity Identity,
    string NativeRevisionId,
    ImmutableArray<RouteConfig> Routes,
    ImmutableArray<ClusterConfig> Clusters,
    GatewayEffectiveSnapshot EffectiveSnapshot)
{
    internal const int MaximumNativeRevisionIdLength = 256;

    internal static NativePublicationBundle Create(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters,
        string? nativeRevisionId,
        GatewayEffectiveSnapshot effectiveSnapshot)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!GatewayIdentifier.IsCanonical(identity.CandidateId.Value)) throw new ArgumentException("Candidate identity is not canonical.", nameof(identity));
        if (string.IsNullOrWhiteSpace(identity.AuthorityId) || string.IsNullOrWhiteSpace(identity.AuthorityEpoch)) throw new ArgumentException("Authority identity and epoch are required.", nameof(identity));
        if (identity.ContentHash.Algorithm != "sha-256" || identity.ContentHash.Value?.Length != 64) throw new ArgumentException("A canonical SHA-256 content identity is required.", nameof(identity));
        if (routes.IsDefault || clusters.IsDefault) throw new ArgumentException("Native publication arrays must be initialized.");
        ValidateEffectiveSnapshot(identity, effectiveSnapshot);
        var revision = nativeRevisionId ?? $"hpd-{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(revision) || revision.Length > MaximumNativeRevisionIdLength || revision.Any(char.IsControl))
            throw new ArgumentException($"Native revision identity must be nonblank, at most {MaximumNativeRevisionIdLength} characters, and contain no control characters.", nameof(nativeRevisionId));
        return new NativePublicationBundle(identity, revision, routes, clusters, effectiveSnapshot);
    }

    private static void ValidateEffectiveSnapshot(PublicationCandidateIdentity identity, GatewayEffectiveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1 || snapshot.IsTruncated || snapshot.CandidateId != identity.CandidateId || snapshot.CandidateContentHash != identity.ContentHash)
            throw new ArgumentException("A complete schema-1 effective snapshot for the exact publication identity is required.", nameof(snapshot));
        if (snapshot.Records.IsDefault || snapshot.Records.Length > GatewayEffectiveBounds.MaximumRecords)
            throw new ArgumentException("Effective records must be initialized and bounded.", nameof(snapshot));

        var previousTarget = string.Empty;
        var previousFamily = string.Empty;
        var diagnostics = 0;
        foreach (var record in snapshot.Records)
        {
            if (record is null || record.SchemaVersion != 1 || record.TargetKind != GatewayEffectiveTargetKind.Route ||
                !GatewayIdentifier.IsCanonical(record.TargetId) || !IsKnownFamily(record.Family) ||
                record.Composition is not (GatewayEffectiveComposition.ReplaceMoreSpecific or GatewayEffectiveComposition.AdditiveOrdered) ||
                record.Disposition != GatewayMaterializationDisposition.Materialized || !ValidHash(record.EffectiveContentHash) ||
                record.Contributions.IsDefaultOrEmpty || record.Contributions.Length > GatewayEffectiveBounds.MaximumContributionsPerRecord ||
                record.Diagnostics.IsDefault || record.Diagnostics.Length > GatewayEffectiveBounds.MaximumDiagnosticsPerRecord ||
                !Bounded(record.CompilerPackage) || !Bounded(record.CompilerVersion) || record.NativeProjection is null ||
                !Bounded(record.NativeProjection.Owner) || !Bounded(record.NativeProjection.Seam) || !Bounded(record.NativeProjection.PackageIdentity))
                throw new ArgumentException("An effective record is structurally invalid.", nameof(snapshot));

            var targetOrder = StringComparer.Ordinal.Compare(previousTarget, record.TargetId);
            if (targetOrder > 0 || (targetOrder == 0 && StringComparer.Ordinal.Compare(previousFamily, record.Family) >= 0))
                throw new ArgumentException("Effective records must be uniquely sorted by target and family.", nameof(snapshot));
            previousTarget = record.TargetId;
            previousFamily = record.Family;

            for (var index = 0; index < record.Contributions.Length; index++)
            {
                var contribution = record.Contributions[index];
                if (contribution is null || contribution.DeterministicOrder != index || !Bounded(contribution.SourceIdentity, 512) ||
                    !ValidHash(contribution.ContentHash) || !Enum.IsDefined(contribution.SourceKind) ||
                    !Enum.IsDefined(contribution.Scope) || !Enum.IsDefined(contribution.Disposition))
                    throw new ArgumentException("An effective contribution is structurally invalid.", nameof(snapshot));
            }
            foreach (var diagnostic in record.Diagnostics)
            {
                if (diagnostic is null || !Bounded(diagnostic.Code) || !Bounded(diagnostic.SafeMessage))
                    throw new ArgumentException("An effective diagnostic is structurally invalid.", nameof(snapshot));
            }
            diagnostics += record.Diagnostics.Length;
            if (diagnostics > GatewayEffectiveBounds.MaximumDiagnostics)
                throw new ArgumentException("The effective diagnostic bound was exceeded.", nameof(snapshot));
        }
    }

    private static bool ValidHash(ContentHash hash) =>
        hash.Algorithm == "sha-256" && hash.Value is { Length: 64 } value && value.All(static character => char.IsAsciiHexDigit(character));

    private static bool Bounded(string? value, int maximum = 256) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private static bool IsKnownFamily(string family) => family is
        GatewayEffectiveFamilies.Authorization or
        GatewayEffectiveFamilies.Cors or
        GatewayEffectiveFamilies.TrafficAdmission or
        GatewayEffectiveFamilies.RequestTimeout or
        GatewayEffectiveFamilies.OutputCache or
        GatewayEffectiveFamilies.Inspection or
        GatewayEffectiveFamilies.CredentialDisposition or
        GatewayEffectiveFamilies.RequestHeaderTransforms or
        GatewayEffectiveFamilies.ResponseHeaderTransforms or
        GatewayEffectiveFamilies.ResponseTrailerTransforms;
}
