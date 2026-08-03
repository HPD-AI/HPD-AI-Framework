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
        GatewayEffectiveProjectionBuilder.PreparedProjection preparedEffective)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!GatewayIdentifier.IsCanonical(identity.CandidateId.Value)) throw new ArgumentException("Candidate identity is not canonical.", nameof(identity));
        if (string.IsNullOrWhiteSpace(identity.AuthorityId) || string.IsNullOrWhiteSpace(identity.AuthorityEpoch)) throw new ArgumentException("Authority identity and epoch are required.", nameof(identity));
        if (identity.ContentHash.Algorithm != "sha-256" || identity.ContentHash.Value?.Length != 64) throw new ArgumentException("A canonical SHA-256 content identity is required.", nameof(identity));
        if (routes.IsDefault || clusters.IsDefault) throw new ArgumentException("Native publication arrays must be initialized.");
        ArgumentNullException.ThrowIfNull(preparedEffective);
        if (!preparedEffective.Routes.Equals(routes))
            throw new ArgumentException("The effective projection was not prepared with the exact native Route array.", nameof(preparedEffective));
        var effectiveSnapshot = preparedEffective.Snapshot;
        ValidateEffectiveSnapshot(identity, routes, effectiveSnapshot);
        var revision = nativeRevisionId ?? $"hpd-{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(revision) || revision.Length > MaximumNativeRevisionIdLength || revision.Any(char.IsControl))
            throw new ArgumentException($"Native revision identity must be nonblank, at most {MaximumNativeRevisionIdLength} characters, and contain no control characters.", nameof(nativeRevisionId));
        return new NativePublicationBundle(identity, revision, routes, clusters, effectiveSnapshot);
    }

    private static void ValidateEffectiveSnapshot(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        GatewayEffectiveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1 || snapshot.IsTruncated || snapshot.CandidateId != identity.CandidateId || snapshot.CandidateContentHash != identity.ContentHash)
            throw new ArgumentException("A complete schema-1 effective snapshot for the exact publication identity is required.", nameof(snapshot));
        if (snapshot.Records.IsDefault || snapshot.Records.Length > GatewayEffectiveBounds.MaximumRecords)
            throw new ArgumentException("Effective records must be initialized and bounded.", nameof(snapshot));

        var previousTarget = string.Empty;
        var previousFamily = string.Empty;
        var diagnostics = 0;
        var routeMap = routes.ToImmutableDictionary(static route => route.RouteId, StringComparer.Ordinal);
        var seen = new HashSet<(string Target, string Family)>();
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
            if (!routeMap.TryGetValue(record.TargetId, out var nativeRoute) || !seen.Add((record.TargetId, record.Family)) ||
                !CompositionIsValid(record) || !ContributionsAreSemanticallyValid(record) || !NativeSelectionMatches(record, nativeRoute))
                throw new ArgumentException("An effective record is not semantically correlated with its native Route.", nameof(snapshot));

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
        foreach (var route in routes)
        {
            RequireRecord(route.RouteId, GatewayEffectiveFamilies.Authorization, route.AuthorizationPolicy is not null, seen, snapshot);
            RequireRecord(route.RouteId, GatewayEffectiveFamilies.Cors, route.CorsPolicy is not null, seen, snapshot);
            RequireRecord(route.RouteId, GatewayEffectiveFamilies.TrafficAdmission, route.RateLimiterPolicy is not null, seen, snapshot);
            RequireRecord(route.RouteId, GatewayEffectiveFamilies.RequestTimeout, route.TimeoutPolicy is not null || route.Timeout is not null, seen, snapshot);
            RequireRecord(route.RouteId, GatewayEffectiveFamilies.OutputCache, route.OutputCachePolicy is not null, seen, snapshot);
            RequireRecord(route.RouteId, GatewayEffectiveFamilies.Inspection,
                route.Metadata?.ContainsKey("hpd.gateway.inspection.inspector") == true, seen, snapshot);
        }
    }

    private static bool CompositionIsValid(GatewayEffectiveRecord record) => record.Family switch
    {
        GatewayEffectiveFamilies.RequestHeaderTransforms or GatewayEffectiveFamilies.ResponseHeaderTransforms or
        GatewayEffectiveFamilies.ResponseTrailerTransforms => record.Composition == GatewayEffectiveComposition.AdditiveOrdered,
        _ => record.Composition == GatewayEffectiveComposition.ReplaceMoreSpecific
    };

    private static bool ContributionsAreSemanticallyValid(GatewayEffectiveRecord record)
    {
        if (record.Composition == GatewayEffectiveComposition.AdditiveOrdered)
            return record.Contributions.All(static contribution =>
                contribution.SourceKind == GatewayContributionSourceKind.Inline &&
                contribution.Scope == GatewayContributionScope.RouteLocal &&
                contribution.Disposition == GatewayContributionDisposition.Selected &&
                contribution.Definition is null);

        var selected = 0;
        foreach (var contribution in record.Contributions)
        {
            var valid = contribution.SourceKind switch
            {
                GatewayContributionSourceKind.RootDefault => contribution.Scope == GatewayContributionScope.RootDefault &&
                    contribution.Disposition is GatewayContributionDisposition.Selected or GatewayContributionDisposition.Overridden && contribution.Definition is null,
                GatewayContributionSourceKind.Inline => contribution.Scope == GatewayContributionScope.RouteLocal &&
                    contribution.Disposition == GatewayContributionDisposition.Selected && contribution.Definition is null,
                GatewayContributionSourceKind.ReusableDefinition => contribution.Scope is GatewayContributionScope.RootDefault or GatewayContributionScope.RouteLocal &&
                    contribution.Disposition is GatewayContributionDisposition.Selected or GatewayContributionDisposition.Overridden && contribution.Definition is not null,
                GatewayContributionSourceKind.HostProfile => contribution.Scope == GatewayContributionScope.Host &&
                    contribution.Disposition == GatewayContributionDisposition.Correlated && contribution.Definition is null,
                _ => false
            };
            if (!valid) return false;
            if (contribution.Disposition == GatewayContributionDisposition.Selected) selected++;
        }
        return selected == 1 && record.Contributions[^1].Disposition != GatewayContributionDisposition.Overridden;
    }

    private static bool NativeSelectionMatches(GatewayEffectiveRecord record, RouteConfig route) => record.Family switch
    {
        GatewayEffectiveFamilies.Authorization => route.AuthorizationPolicy is not null && record.NativeProjection.Seam == "RouteConfig.AuthorizationPolicy",
        GatewayEffectiveFamilies.Cors => route.CorsPolicy is not null && record.NativeProjection.Seam == "RouteConfig.CorsPolicy",
        GatewayEffectiveFamilies.TrafficAdmission => route.RateLimiterPolicy is not null && record.NativeProjection.Seam == "RouteConfig.RateLimiterPolicy",
        GatewayEffectiveFamilies.RequestTimeout => (route.TimeoutPolicy is not null || route.Timeout is not null) && record.NativeProjection.Seam == "RouteConfig.TimeoutPolicy/Timeout",
        GatewayEffectiveFamilies.OutputCache => route.OutputCachePolicy is not null && record.NativeProjection.Seam == "RouteConfig.OutputCachePolicy",
        GatewayEffectiveFamilies.Inspection => route.Metadata?.ContainsKey("hpd.gateway.inspection.inspector") == true && record.NativeProjection.Seam == "RouteConfig.Metadata/HPD inspection",
        GatewayEffectiveFamilies.CredentialDisposition => record.NativeProjection.Seam == "RouteConfig.Transforms/request-header-remove",
        GatewayEffectiveFamilies.RequestHeaderTransforms => record.NativeProjection.Seam == "RouteConfig.Transforms/request-header",
        GatewayEffectiveFamilies.ResponseHeaderTransforms => record.NativeProjection.Seam == "RouteConfig.Transforms/response-header",
        GatewayEffectiveFamilies.ResponseTrailerTransforms => record.NativeProjection.Seam == "RouteConfig.Transforms/response-trailer",
        _ => false
    };

    private static void RequireRecord(
        string routeId,
        string family,
        bool nativePresent,
        HashSet<(string Target, string Family)> seen,
        GatewayEffectiveSnapshot snapshot)
    {
        if (nativePresent != seen.Contains((routeId, family)))
            throw new ArgumentException("Native Route policy presence does not match effective provenance completeness.", nameof(snapshot));
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
