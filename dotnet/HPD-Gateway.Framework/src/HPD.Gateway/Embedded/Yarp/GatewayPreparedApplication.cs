using System.Collections.Immutable;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal enum GatewayPreparedMembershipDisposition : byte
{
    Fresh = 0,
    LastKnownMembership = 1,
    UnavailableWhenStale = 2,
    RefreshFailed = 3,
}

internal sealed record GatewayPreparedDependencyResolution(
    string UpstreamId,
    long MembershipGeneration,
    GatewayPreparedMembershipDisposition Disposition,
    int DestinationCount,
    ContentHash MembershipIdentity);

internal sealed class GatewayPreparedApplication
{
    internal const int MaximumNativeRevisionIdLength = 256;

    private GatewayPreparedApplication(
        GatewayRuntimePlan plan,
        string nativeRevisionId,
        ImmutableArray<ClusterConfig> clusters,
        ImmutableArray<GatewayPreparedDependencyResolution> resolutions)
    {
        Plan = plan;
        NativeRevisionId = nativeRevisionId;
        Clusters = clusters;
        Resolutions = resolutions;
        NativeGraphIdentity = GatewayRuntimeGraphIdentity.ComputeNativeGeneration(plan.Routes, clusters);
    }

    internal GatewayRuntimePlan Plan { get; }
    internal string NativeRevisionId { get; }
    internal ImmutableArray<ClusterConfig> Clusters { get; }
    internal ImmutableArray<GatewayPreparedDependencyResolution> Resolutions { get; }
    internal ContentHash NativeGraphIdentity { get; }

    internal static GatewayPreparedApplication Create(
        GatewayRuntimeApplicationPreparer.NativeValidationReceipt validation,
        string? nativeRevisionId)
    {
        ArgumentNullException.ThrowIfNull(validation);
        GatewayRuntimePlan plan = validation.Plan;
        ImmutableArray<ClusterConfig> clusters = validation.Clusters;
        ImmutableArray<GatewayPreparedDependencyResolution> resolutions = validation.Resolutions;
        ValidateResolvedGraph(plan, clusters, resolutions);
        PublicationCandidateIdentity identity = plan.Identity;
        ImmutableArray<RouteConfig> routes = plan.Routes;
        GatewayEffectiveProjectionBuilder.PreparedProjection preparedEffective = plan.Effective;
        var effectiveSnapshot = preparedEffective.Snapshot;
        ValidatePreparedProjectionSnapshot(identity, routes, effectiveSnapshot);
        var revision = nativeRevisionId ?? $"hpd-{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(revision) || revision.Length > MaximumNativeRevisionIdLength || revision.Any(char.IsControl))
            throw new ArgumentException($"Native revision identity must be nonblank, at most {MaximumNativeRevisionIdLength} characters, and contain no control characters.", nameof(nativeRevisionId));
        return new GatewayPreparedApplication(plan, revision, clusters, resolutions);
    }

    internal static void ValidateResolvedGraph(
        GatewayRuntimePlan plan,
        ImmutableArray<ClusterConfig> clusters,
        ImmutableArray<GatewayPreparedDependencyResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        PublicationCandidateIdentity identity = plan.Identity;
        ImmutableArray<RouteConfig> routes = plan.Routes;
        if (!GatewayIdentifier.IsCanonical(identity.CandidateId.Value)) throw new ArgumentException("Candidate identity is not canonical.", nameof(identity));
        if (string.IsNullOrWhiteSpace(identity.AuthorityId) || string.IsNullOrWhiteSpace(identity.AuthorityEpoch)) throw new ArgumentException("Authority identity and epoch are required.", nameof(identity));
        if (identity.ContentHash.Algorithm != "sha-256" || identity.ContentHash.Value?.Length != 64) throw new ArgumentException("A canonical SHA-256 content identity is required.", nameof(identity));
        if (routes.IsDefault || clusters.IsDefault || resolutions.IsDefault)
            throw new ArgumentException("Prepared application arrays must be initialized.");
        if (plan.Dependencies.IsEmpty && (!clusters.Equals(plan.Clusters) || !resolutions.IsEmpty))
            throw new ArgumentException("A static prepared application must retain the exact planned Cluster array and have no resolutions.", nameof(clusters));
        if (!clusters.Select(static cluster => cluster.ClusterId)
            .SequenceEqual(plan.Clusters.Select(static cluster => cluster.ClusterId), StringComparer.Ordinal))
            throw new ArgumentException("Prepared Cluster identity does not match the complete runtime plan.", nameof(clusters));
        if (clusters.Any(static cluster => cluster.Destinations?.Values.Any(static destination =>
                destination.Metadata?.ContainsKey(GatewayRuntimePlanner.SymbolicDestinationMetadata) == true) == true))
            throw new ArgumentException("A prepared application cannot contain symbolic destinations.", nameof(clusters));
        ValidateResolvedClusters(plan, clusters, resolutions);
    }

    internal PublicationCandidateIdentity Identity => Plan.Identity;
    internal string ApplicationId => Plan.ApplicationId;
    internal ContentHash SymbolicPlanIdentity => Plan.SymbolicPlanIdentity;
    internal ImmutableArray<RouteConfig> Routes => Plan.Routes;
    internal GatewayPreparedProjectionSnapshot PreparedProjectionSnapshot => Plan.Effective.Snapshot;

    internal static GatewayPreparedDependencyResolution DescribeResolution(
        GatewayRuntimeDependencyBinding dependency,
        IReadOnlyDictionary<string, DestinationConfig> destinations,
        long membershipGeneration,
        GatewayPreparedMembershipDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(destinations);
        return new GatewayPreparedDependencyResolution(
            dependency.UpstreamId,
            membershipGeneration,
            disposition,
            destinations.Count,
            GatewayRuntimeGraphIdentity.ComputeMembership(destinations, disposition));
    }

    private static void ValidateResolvedClusters(
        GatewayRuntimePlan plan,
        ImmutableArray<ClusterConfig> clusters,
        ImmutableArray<GatewayPreparedDependencyResolution> resolutions)
    {
        if (plan.Dependencies.IsEmpty) return;
        if (resolutions.Length != plan.Dependencies.Length ||
            !resolutions.Select(static value => value.UpstreamId)
                .SequenceEqual(plan.Dependencies.Select(static value => value.UpstreamId), StringComparer.Ordinal))
            throw new ArgumentException("Every discovery dependency must have exactly one ordered resolution.", nameof(resolutions));

        ImmutableDictionary<string, GatewayRuntimeDependencyBinding> dependencies = plan.Dependencies
            .ToImmutableDictionary(static value => value.UpstreamId, StringComparer.Ordinal);
        var totalResolvedEndpoints = 0;
        for (var index = 0; index < clusters.Length; index++)
        {
            ClusterConfig planned = plan.Clusters[index];
            ClusterConfig resolved = clusters[index];
            if (!dependencies.TryGetValue(planned.ClusterId, out GatewayRuntimeDependencyBinding? dependency))
            {
                if (!ReferenceEquals(planned, resolved))
                    throw new ArgumentException("Static Clusters must retain the exact planned instance.", nameof(clusters));
                continue;
            }

            ClusterConfig expected = planned with { Destinations = resolved.Destinations };
            if (resolved != expected)
                throw new ArgumentException("Discovery preparation may replace only the correlated symbolic destination seam.", nameof(clusters));
            IReadOnlyDictionary<string, DestinationConfig> destinations = resolved.Destinations
                ?? throw new ArgumentException("A resolved discovery Cluster requires an explicit destination dictionary.", nameof(clusters));
            GatewayPreparedDependencyResolution evidence = resolutions.Single(value =>
                StringComparer.Ordinal.Equals(value.UpstreamId, dependency.UpstreamId));
            if (evidence.MembershipGeneration <= 0 || evidence.DestinationCount != destinations.Count ||
                destinations.Count > dependency.MaximumEndpoints ||
                evidence.MembershipIdentity != GatewayRuntimeGraphIdentity.ComputeMembership(destinations, evidence.Disposition) ||
                !DispositionAllowed(dependency.StaleBehavior, evidence.Disposition, destinations.Count) ||
                !ValidDestinations(dependency, destinations))
                throw new ArgumentException("Resolved discovery membership or its evidence is invalid.", nameof(resolutions));
            totalResolvedEndpoints = checked(totalResolvedEndpoints + destinations.Count);
            if (totalResolvedEndpoints > GatewayRuntimePlan.MaximumResolvedEndpoints)
                throw new ArgumentException("The complete prepared application exceeds the resolved-endpoint bound.", nameof(clusters));
        }
    }

    private static bool DispositionAllowed(
        DiscoveryStaleBehavior staleBehavior,
        GatewayPreparedMembershipDisposition disposition,
        int destinationCount) => disposition switch
    {
        GatewayPreparedMembershipDisposition.Fresh => true,
        GatewayPreparedMembershipDisposition.LastKnownMembership =>
            staleBehavior == DiscoveryStaleBehavior.PermitLastKnownMembership,
        GatewayPreparedMembershipDisposition.UnavailableWhenStale =>
            staleBehavior == DiscoveryStaleBehavior.ServeUnavailableWhenStale && destinationCount == 0,
        GatewayPreparedMembershipDisposition.RefreshFailed =>
            staleBehavior == DiscoveryStaleBehavior.RejectActivationUntilFresh,
        _ => false,
    };

    private static bool ValidDestinations(
        GatewayRuntimeDependencyBinding dependency,
        IReadOnlyDictionary<string, DestinationConfig> destinations)
    {
        if (destinations is not IImmutableDictionary<string, DestinationConfig>) return false;
        foreach (KeyValuePair<string, DestinationConfig> pair in destinations)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || System.Text.Encoding.UTF8.GetByteCount(pair.Key) > 256 ||
                pair.Key.Any(char.IsControl) || pair.Value is null ||
                !Uri.TryCreate(pair.Value.Address, UriKind.Absolute, out Uri? address) ||
                !SchemeAndTlsAuthorityMatch(dependency, address) || address.UserInfo.Length > 0 ||
                address.Query.Length > 0 || address.Fragment.Length > 0 ||
                System.Text.Encoding.UTF8.GetByteCount(pair.Value.Address) > 2048 ||
                !ValidOptionalAddress(dependency, pair.Value.Health) || !ValidOptionalHost(pair.Value.Host) ||
                !ValidMetadata(pair.Value.Metadata))
                return false;
        }
        return true;
    }

    private static bool ValidOptionalAddress(GatewayRuntimeDependencyBinding dependency, string? value)
    {
        if (value is null) return true;
        return System.Text.Encoding.UTF8.GetByteCount(value) <= 2048 &&
            Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            SchemeAndTlsAuthorityMatch(dependency, uri) && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    }

    private static bool SchemeAndTlsAuthorityMatch(GatewayRuntimeDependencyBinding dependency, Uri uri)
    {
        ServiceDiscoveryScheme scheme = uri.Scheme switch
        {
            "http" => ServiceDiscoveryScheme.Http,
            "https" => ServiceDiscoveryScheme.Https,
            _ => (ServiceDiscoveryScheme)byte.MaxValue,
        };
        if (!dependency.Schemes.Contains(scheme)) return false;
        if (scheme != ServiceDiscoveryScheme.Https) return true;
        return dependency.TlsServerName is { } tlsServerName &&
            !System.Net.IPAddress.TryParse(uri.Host, out _) &&
            StringComparer.Ordinal.Equals(uri.Host, tlsServerName);
    }

    private static bool ValidOptionalHost(string? value) => value is null ||
        (!string.IsNullOrWhiteSpace(value) && System.Text.Encoding.UTF8.GetByteCount(value) <= 253 && !value.Any(char.IsControl));

    private static bool ValidMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return true;
        if (metadata.Count > 64) return false;
        foreach (KeyValuePair<string, string> pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.StartsWith("hpd.gateway.", StringComparison.Ordinal) ||
                System.Text.Encoding.UTF8.GetByteCount(pair.Key) > 256 ||
                System.Text.Encoding.UTF8.GetByteCount(pair.Value) > 1024 ||
                pair.Key.Any(char.IsControl) || pair.Value.Any(char.IsControl))
                return false;
        }
        return true;
    }

    private static void ValidatePreparedProjectionSnapshot(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        GatewayPreparedProjectionSnapshot snapshot)
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
        GatewayPreparedProjectionSnapshot snapshot)
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
